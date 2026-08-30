using System.Text.Json;
using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bank.Api.Features.Transfers.IdempotentTransfer;

public sealed record IdempotentWithdrawResult
{
    public required bool Applied { get; init; }
    public required decimal BalanceAfter { get; init; }
    public required string Outcome { get; init; }
}

public sealed record IdempotencyDemoRequest
{
    /// <summary>How many times the "same" request is retried, concurrently.</summary>
    public int Retries { get; init; } = 5;

    public decimal Amount { get; init; } = 100m;
    public decimal StartingBalance { get; init; } = 1000m;

    /// <summary>Turn the protection off to see what retries cost without it.</summary>
    public bool UseIdempotencyKey { get; init; } = true;
}

public sealed record IdempotencyDemoResponse
{
    public required bool UseIdempotencyKey { get; init; }
    public required int Retries { get; init; }
    public required decimal StartingBalance { get; init; }
    public required decimal FinalBalance { get; init; }
    public required decimal ExpectedBalance { get; init; }
    public required int TimesApplied { get; init; }
    public required string Verdict { get; init; }
}

/// <summary>
/// At scale, retries are not hypothetical: a timeout, a load-balancer retry, a client
/// double-click or a queue's at-least-once delivery all resend the same request.
/// </summary>
/// <remarks>
/// <para>Everything in lessons 01–06 makes <i>concurrent</i> operations safe. None of it
/// makes a <i>repeated</i> operation safe. A withdrawal that is correctly applied twice
/// is still money gone twice.</para>
/// <para>The fix is an idempotency key, and the important part is that the uniqueness
/// check lives in the database. Checking "have I seen this key?" in application code and
/// then inserting is the read-modify-write race from lesson 01 — two instances would
/// both check, both see nothing, and both proceed.</para>
/// </remarks>
public sealed class IdempotentTransferHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    PostgresAdvisoryAccountLock accountLock)
{
    private const string Operation = "withdraw";

    /// <summary>PostgreSQL SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<IdempotentWithdrawResult> WithdrawAsync(
        Guid accountId,
        decimal amount,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await using var lookup = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var existing = await lookup.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Key == idempotencyKey, cancellationToken);

            // A fast path, not the guarantee. The guarantee is the insert below failing.
            if (existing is not null)
            {
                return JsonSerializer.Deserialize<IdempotentWithdrawResult>(existing.ResponseJson)!
                    with { Applied = false, Outcome = "Replayed the stored result; nothing was applied twice." };
            }
        }

        await using var handle = await accountLock.AcquireAsync(accountId, cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, cancellationToken);

        if (account.Balance < amount)
        {
            return new IdempotentWithdrawResult
            {
                Applied = false,
                BalanceAfter = account.Balance,
                Outcome = "Insufficient funds.",
            };
        }

        account.Balance -= amount;

        var result = new IdempotentWithdrawResult
        {
            Applied = true,
            BalanceAfter = account.Balance,
            Outcome = "Applied.",
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Written in the SAME transaction as the balance change. If these were two
            // transactions, a crash between them would either lose the record (allowing
            // a replay) or record work that never happened.
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = idempotencyKey,
                Operation = Operation,
                ResponseJson = JsonSerializer.Serialize(result),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == UniqueViolation)
        {
            // Somebody else inserted this key while we were working. Their write stands;
            // ours is rolled back untouched. This is the real guarantee — the database
            // refused, so "exactly once" is enforced by a constraint rather than by
            // hopeful application logic.
            return result with
            {
                Applied = false,
                Outcome = "A concurrent duplicate won the race; this attempt was rolled back.",
            };
        }

        return result;
    }
}

public sealed class IdempotencyDemoHandler(
    IDbContextFactory<BankDbContext> dbContextFactory,
    IdempotentTransferHandler transfers)
{
    public async Task<IdempotencyDemoResponse> HandleAsync(
        IdempotencyDemoRequest request,
        CancellationToken cancellationToken)
    {
        var retries = Math.Clamp(request.Retries, 1, 32);
        var accountId = await SeedAsync(request.StartingBalance, cancellationToken);

        // One logical operation, retried N times — as a flaky network would.
        var key = request.UseIdempotencyKey ? $"demo-{Guid.NewGuid():N}" : null;

        var results = await Task.WhenAll(Enumerable.Range(0, retries).Select(_ =>
            transfers.WithdrawAsync(accountId, request.Amount, key, cancellationToken)));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var finalBalance = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync(cancellationToken);

        var timesApplied = results.Count(r => r.Applied);
        var expected = request.StartingBalance - request.Amount;

        return new IdempotencyDemoResponse
        {
            UseIdempotencyKey = request.UseIdempotencyKey,
            Retries = retries,
            StartingBalance = request.StartingBalance,
            FinalBalance = finalBalance,
            ExpectedBalance = expected,
            TimesApplied = timesApplied,
            Verdict = request.UseIdempotencyKey
                ? $"One logical withdrawal retried {retries} times was applied {timesApplied} time(s). "
                  + $"Balance {finalBalance:0.00}, expected {expected:0.00}. The uniqueness constraint on the "
                  + "idempotency key is what makes that true — not application logic."
                : $"Without a key, {retries} retries of ONE withdrawal took {request.Amount * timesApplied:0.00} "
                  + $"instead of {request.Amount:0.00}. Balance {finalBalance:0.00}, expected {expected:0.00}. "
                  + "Every retry was individually correct and concurrency-safe; the customer was still charged "
                  + $"{timesApplied} times.",
        };
    }

    private async Task<Guid> SeedAsync(decimal openingBalance, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = $"IDEM-{Guid.NewGuid():N}"[..16],
            Owner = "Idempotency demo",
            Balance = openingBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return account.Id;
    }
}

public sealed class IdempotentTransferEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lab/idempotency", async (
                IdempotencyDemoRequest request,
                IdempotencyDemoHandler handler,
                CancellationToken cancellationToken) =>
                Results.Ok(await handler.HandleAsync(request, cancellationToken)))
            .WithName("RunIdempotencyDemo")
            .WithTags("Lab")
            .WithSummary("Retries of one withdrawal, with and without an idempotency key.");
    }
}

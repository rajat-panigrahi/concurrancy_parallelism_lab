using Bank.Api.Features.Transfers.IdempotentTransfer;
using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Bank.Api.IntegrationTests.Features.Transfers;

/// <summary>
/// Concurrency safety and idempotency are different guarantees. Everything in lessons
/// 01-06 makes concurrent operations safe; none of it makes a repeated one safe.
/// </summary>
[Collection(PostgresCollection.Name)]
public class IdempotencyTests(PostgresFixture fixture)
{
    [Fact]
    public async Task WithoutAKey_RetriesChargeTheCustomerEveryTime()
    {
        var response = await Demo(useKey: false, retries: 5, amount: 100m, startingBalance: 1000m);

        // Every one of those five was individually correct and fully concurrency-safe.
        // The customer was still charged five times for one instruction.
        response.TimesApplied.ShouldBe(5);
        response.FinalBalance.ShouldBe(500m);
        response.ExpectedBalance.ShouldBe(900m);
    }

    [Fact]
    public async Task WithAKey_ConcurrentRetriesAreAppliedExactlyOnce()
    {
        var response = await Demo(useKey: true, retries: 5, amount: 100m, startingBalance: 1000m);

        response.TimesApplied.ShouldBe(1);
        response.FinalBalance.ShouldBe(900m);
        response.FinalBalance.ShouldBe(response.ExpectedBalance);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task NoMatterHowManyConcurrentDuplicates_ExactlyOneIsApplied(int retries)
    {
        // The guarantee comes from the database's uniqueness constraint, not from
        // application logic. Checking "have I seen this key?" and then inserting would
        // be the read-modify-write race from lesson 01 in a different costume — every
        // instance would check, see nothing, and proceed.
        var response = await Demo(useKey: true, retries: retries, amount: 50m, startingBalance: 1000m);

        response.TimesApplied.ShouldBe(1, $"{retries} concurrent duplicates must still apply once");
        response.FinalBalance.ShouldBe(950m);
    }

    [Fact]
    public async Task AReplayedRequest_ReturnsTheStoredResult_RatherThanFailing()
    {
        var factory = new HarnessFactory(fixture.ConnectionString);
        var handler = new IdempotentTransferHandler(factory, new PostgresAdvisoryAccountLock(factory));
        var accountId = await SeedAsync(500m);
        var key = $"replay-{Guid.NewGuid():N}";

        var first = await handler.WithdrawAsync(accountId, 100m, key, CancellationToken.None);
        var second = await handler.WithdrawAsync(accountId, 100m, key, CancellationToken.None);

        first.Applied.ShouldBeTrue();

        // A duplicate is not an error — the caller asked for something already done, so
        // they get the original answer. Returning a failure would push retry logic back
        // onto a client that did nothing wrong.
        second.Applied.ShouldBeFalse();
        second.BalanceAfter.ShouldBe(first.BalanceAfter);

        await using var db = fixture.NewDbContext();
        var balance = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();

        balance.ShouldBe(400m, "the second call must not have moved any money");
    }

    private async Task<IdempotencyDemoResponse> Demo(
        bool useKey, int retries, decimal amount, decimal startingBalance)
    {
        var factory = new HarnessFactory(fixture.ConnectionString);
        var handler = new IdempotencyDemoHandler(
            factory, new IdempotentTransferHandler(factory, new PostgresAdvisoryAccountLock(factory)));

        return await handler.HandleAsync(
            new IdempotencyDemoRequest
            {
                Retries = retries,
                Amount = amount,
                StartingBalance = startingBalance,
                UseIdempotencyKey = useKey,
            },
            CancellationToken.None);
    }

    private async Task<Guid> SeedAsync(decimal openingBalance)
    {
        await using var db = fixture.NewDbContext();

        var account = new Account
        {
            Id = Guid.NewGuid(), AccountNumber = $"I-{Guid.NewGuid():N}"[..16], Owner = "Idempotency test",
            Balance = openingBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return account.Id;
    }

    private sealed class HarnessFactory(string connectionString) : IDbContextFactory<BankDbContext>
    {
        public BankDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<BankDbContext>().UseNpgsql(connectionString).Options);
    }
}

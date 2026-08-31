using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Accounts.OpenAccount;

public sealed record OpenAccountRequest(string AccountNumber, string Owner, decimal OpeningBalance);

public sealed class OpenAccountHandler(BankDbContext db)
{
    public async Task<Account> HandleAsync(OpenAccountRequest request, CancellationToken cancellationToken)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            AccountNumber = request.AccountNumber,
            Owner = request.Owner,
            Balance = request.OpeningBalance,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return account;
    }
}

public sealed class OpenAccountEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/accounts", async (
                OpenAccountRequest request,
                OpenAccountHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.AccountNumber))
                {
                    return Results.BadRequest(new { error = "AccountNumber is required." });
                }

                if (request.OpeningBalance < 0m)
                {
                    return Results.BadRequest(new { error = "OpeningBalance cannot be negative." });
                }

                try
                {
                    var account = await handler.HandleAsync(request, cancellationToken);
                    return Results.Created($"/api/accounts/{account.Id}", account);
                }
                catch (DbUpdateException)
                {
                    return Results.Conflict(new { error = $"Account number '{request.AccountNumber}' already exists." });
                }
            })
            .WithName("OpenAccount")
            .WithTags("Accounts");
    }
}

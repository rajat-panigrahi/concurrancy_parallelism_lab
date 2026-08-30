using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Features.Accounts.GetAccount;

public sealed class GetAccountEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/accounts/{id:guid}", async (
                Guid id,
                BankDbContext db,
                CancellationToken cancellationToken) =>
            {
                // AsNoTracking because this is a read. The change tracker exists to
                // work out what to UPDATE; on a read-only query it is pure overhead.
                var account = await db.Accounts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

                return account is null ? Results.NotFound() : Results.Ok(account);
            })
            .WithName("GetAccount")
            .WithTags("Accounts");

        app.MapGet("/api/accounts", async (BankDbContext db, CancellationToken cancellationToken) =>
                Results.Ok(await db.Accounts.AsNoTracking()
                    .OrderBy(a => a.AccountNumber)
                    .Take(200)
                    .ToListAsync(cancellationToken)))
            .WithName("ListAccounts")
            .WithTags("Accounts");
    }
}

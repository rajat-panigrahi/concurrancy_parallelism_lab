using Bank.Api.IntegrationTests.Infrastructure;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Bank.Api.IntegrationTests;

/// <summary>
/// M0 smoke tests. These do not teach concurrency — they prove the foundation the
/// concurrency slices are about to be built on actually works, so that a failure in
/// M2 means "our handler is wrong" and never "the mapping was never right".
/// </summary>
[Collection(PostgresCollection.Name)]
public class FoundationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Account_RoundTrips_ThroughPostgres()
    {
        await using var db = fixture.NewDbContext();
        var account = NewAccount(openingBalance: 250.75m);

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        await using var readBack = fixture.NewDbContext();
        var loaded = await readBack.Accounts.SingleAsync(a => a.Id == account.Id);

        loaded.Balance.ShouldBe(250.75m);
        loaded.Owner.ShouldBe(account.Owner);
    }

    [Fact]
    public async Task Version_IsPopulatedByPostgres_AndChangesOnEveryUpdate()
    {
        await using var db = fixture.NewDbContext();
        var account = NewAccount(openingBalance: 100m);

        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var versionAfterInsert = account.Version;

        versionAfterInsert.ShouldNotBe(0u, "Postgres assigns xmin on INSERT; if this is 0 the column mapping is wrong");

        account.Balance -= 10m;
        await db.SaveChangesAsync();

        account.Version.ShouldNotBe(versionAfterInsert, "xmin must change on UPDATE or it is useless as a concurrency token");
    }

    /// <summary>
    /// The single most important assertion in the foundation: two contexts read the
    /// same row, both write, and the second one is *rejected* rather than silently
    /// overwriting the first. Everything in the optimistic slice depends on this.
    /// </summary>
    [Fact]
    public async Task SecondWriterOfTheSameRow_IsRejected_WithConcurrencyException()
    {
        await using var setup = fixture.NewDbContext();
        var account = NewAccount(openingBalance: 100m);
        setup.Accounts.Add(account);
        await setup.SaveChangesAsync();

        // Two independent contexts = two independent change trackers, which is the
        // closest single-process equivalent of two servers handling two requests.
        await using var alice = fixture.NewDbContext();
        await using var bob = fixture.NewDbContext();

        var aliceView = await alice.Accounts.SingleAsync(a => a.Id == account.Id);
        var bobView = await bob.Accounts.SingleAsync(a => a.Id == account.Id);

        aliceView.Balance -= 30m;
        await alice.SaveChangesAsync();

        bobView.Balance -= 50m;
        var bobsWrite = async () => await bob.SaveChangesAsync();

        await bobsWrite.ShouldThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = fixture.NewDbContext();
        var final = await verify.Accounts.SingleAsync(a => a.Id == account.Id);
        final.Balance.ShouldBe(70m, "Alice's write must stand; Bob's must not have silently landed");
    }

    private static Account NewAccount(decimal openingBalance) => new()
    {
        Id = Guid.NewGuid(),
        AccountNumber = $"ACC-{Guid.NewGuid():N}"[..16],
        Owner = "Foundation Test",
        Balance = openingBalance,
    };
}

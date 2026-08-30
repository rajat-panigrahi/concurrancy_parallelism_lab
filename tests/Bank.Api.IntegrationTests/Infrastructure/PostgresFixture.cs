using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bank.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Points the integration tests at a real PostgreSQL database.
/// </summary>
/// <remarks>
/// Optimistic and pessimistic concurrency are database behaviours. Faking them
/// against an in-memory provider would test our mock, not Postgres — and the
/// EF Core in-memory provider does not implement concurrency tokens or row locks
/// at all, so the tests would pass while the real system stayed broken.
/// See ADR-0015 for why a local database rather than Testcontainers.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5432;Database=banklab_test;Username=banklab;Password=banklab;Include Error Detail=true";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("BANKLAB_TEST_DB") ?? DefaultConnectionString;

    public BankDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new BankDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

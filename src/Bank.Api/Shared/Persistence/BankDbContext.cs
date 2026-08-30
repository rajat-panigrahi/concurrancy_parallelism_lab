using Microsoft.EntityFrameworkCore;

namespace Bank.Api.Shared.Persistence;

public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();

        account.HasKey(a => a.Id);
        account.Property(a => a.AccountNumber).HasMaxLength(32).IsRequired();
        account.HasIndex(a => a.AccountNumber).IsUnique();
        account.Property(a => a.Owner).HasMaxLength(128).IsRequired();

        // 18,2 is plenty for this lab and keeps the money type exact end to end.
        account.Property(a => a.Balance).HasPrecision(18, 2);

        // This is what turns every SaveChanges() into an optimistic concurrency
        // check. We bind our Version property onto Postgres' system column xmin,
        // so EF appends "WHERE xmin = @original" to the UPDATE and raises
        // DbUpdateConcurrencyException when that matches zero rows.
        //
        // Note we map the real column rather than calling UseXminAsConcurrencyToken(),
        // which would create a *shadow* property we could not read or return to the
        // client. The lab needs the version visible: showing which version each actor
        // read is half of "who won". See ADR-0006.
        account.Property(a => a.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        var idempotency = modelBuilder.Entity<IdempotencyRecord>();

        // The key IS the primary key. The uniqueness constraint is the concurrency
        // control — two instances racing to insert the same key means exactly one wins.
        idempotency.HasKey(r => r.Key);
        idempotency.Property(r => r.Key).HasMaxLength(128);
        idempotency.Property(r => r.Operation).HasMaxLength(64).IsRequired();
        idempotency.Property(r => r.ResponseJson).IsRequired();
    }
}

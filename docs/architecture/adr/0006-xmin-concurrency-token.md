# ADR-0006 — `xmin` as the optimistic concurrency token

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The optimistic slice needs a concurrency token: a value EF Core can add to the UPDATE's
`WHERE` clause so a write from a stale read matches zero rows and raises
`DbUpdateConcurrencyException`.

Complicating it slightly: the lab needs the version to be **readable**. Half the value
of the timeline is showing *which version each actor read* — that's the column that
makes the lost update obvious in lesson 01. So a hidden token is not enough.

## Options considered

- **`xmin`**, a PostgreSQL system column holding the id of the transaction that last
  wrote the row. Maintained by the database on every UPDATE, for free.
- **A `byte[] RowVersion`**, the SQL Server `rowversion`/`timestamp` idiom, which most
  .NET developers have used and which EF supports via `[Timestamp]`.
- **An application-maintained `int Version`** column, incremented in code on each write.

## Decision

Map the entity's `uint Version` property onto the `xmin` system column:

```csharp
account.Property(a => a.Version)
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
```

Note this maps the real column rather than calling Npgsql's
`UseXminAsConcurrencyToken()`, which creates a **shadow** property the lab could not
read or return to the client.

## Why not the others

- **An application-maintained `int Version`** is the most portable option and the
  easiest to get subtly wrong. It only protects writes that remember to increment it.
  Any code path that forgets — a bulk update, a hand-written `UPDATE` in a migration,
  a DBA fixing data at 2am — silently disables the protection for every reader holding
  that version. `xmin` cannot be forgotten, because Postgres maintains it whether or
  not the writer knows it exists.
- **`byte[] RowVersion`** is the SQL Server idiom, and on SQL Server it is the right
  answer. On PostgreSQL it would mean adding a column and triggers to maintain it,
  reimplementing by hand something the engine already does. It is also less convenient
  to display: an opaque byte array rather than a number you can put in a table column
  and compare by eye.

## Trade-offs accepted

- **It is PostgreSQL-specific.** The optimistic slice does not port to SQL Server
  unchanged. Mitigated by documenting the mapping explicitly in lesson 03 rather than
  pretending `xmin` is the only way.
- **`xmin` is a 32-bit transaction id and it wraps.** Postgres handles wraparound with
  freezing, and in practice a row's `xmin` being reused while a request holds it is not
  a realistic concern — but it is not a monotonic counter, and describing it as a
  "version number" is a simplification.
- **The type is `uint`**, which is unusual in EF models and needs casting to `long` at
  every boundary where the lab records it.
- **It reflects the transaction, not the row.** Two rows written by one transaction get
  the same `xmin`. Irrelevant for row-level checks; confusing if you assume uniqueness
  per row.
- **The migration mentions a column that is never created.** Npgsql correctly emits no
  DDL for a system column, so the generated migration lists `xmin` while the table has
  no such user column (`pg_attribute.attnum = -2`). This looks like a bug the first
  time you read it; verified working in `FoundationTests`.

## Consequences

- The concurrency token costs **nothing** in schema — no column, no index, no migration.
- `RunSummaryCalculator` was written to need version values that are *unique*, never
  *ordered*, precisely so the same "who won" replay works for both the in-memory
  counter and `xmin`.
- The pessimistic slice's raw SQL must name `xmin` explicitly — `SELECT *, xmin FROM …`
  — because system columns are not in `SELECT *` and EF needs every mapped column.
- Lesson 03 carries a portability table so a reader on SQL Server can translate.

## Interview angle

**The 60-second version:** "Postgres already tracks which transaction last wrote each
row in the `xmin` system column, so I mapped that as the concurrency token. EF appends
`WHERE xmin = @original` to the UPDATE; zero rows matched means someone wrote first, and
you get `DbUpdateConcurrencyException`. On SQL Server I'd use `rowversion` with
`[Timestamp]` — same concept, and there it's a real column you add."

**What a good interviewer asks next:** *"Why not just keep your own version column?"*
Because it only protects the write paths that remember to bump it. A raw SQL fix or a
bulk update bypasses it silently, and the failure is invisible. A database-maintained
token has no such hole. That's the answer that shows you've thought about how systems
actually decay, rather than how they look on day one.

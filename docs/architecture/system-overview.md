# System overview

One page on the shape of the system: what talks to what, and — the part that matters
for this project — **where mutable state lives**. Almost every concurrency question in
here reduces to that second question.

## Context

```mermaid
flowchart LR
    dev["You<br/>(learning / demoing)"]
    ui["Bank Lab UI<br/>Angular 19"]
    api["Bank.Api<br/>ASP.NET Core 8"]
    pg[("PostgreSQL 16")]
    bench["Bank.Benchmarks<br/>BenchmarkDotNet"]
    load["Bank.LoadTests<br/>in-repo load harness"]

    dev --> ui
    ui -->|"REST: start a run"| api
    ui <-->|"SignalR: live contention events"| api
    api --> pg
    dev --> bench
    dev --> load
    load -->|"HTTP under load"| api
```

`Bank.Benchmarks` measures code *in process* and never touches the API.
`Bank.LoadTests` only ever speaks HTTP. That separation is deliberate and is the
whole of ADR-0010.

## Where state lives

This is the diagram to remember. The two stores behave completely differently the
moment there is more than one instance.

```mermaid
flowchart TB
    subgraph proc["Bank.Api — one process"]
        direction TB
        mem["InMemoryAccountStore<br/><b>singleton, process-local</b><br/>naive + lock slices"]
        locks["In-process locks<br/>SemaphoreSlim per account"]
    end
    pg[("PostgreSQL<br/><b>shared by every instance</b><br/>optimistic + pessimistic slices")]

    proc --> pg

    style mem fill:#fde2e2,stroke:#c0392b,color:#111
    style locks fill:#fde2e2,stroke:#c0392b,color:#111
    style pg fill:#e2f0d9,stroke:#2d7a2d,color:#111
```

Red boxes are **per process**. Green is **shared**. Scale the app to three instances
and the red boxes are duplicated three times — three separate stores, three separate
sets of locks, coordinating nothing.

## The same system at three replicas

```mermaid
flowchart TB
    nginx["nginx<br/>round-robin"]
    a1["Bank.Api #1<br/>own memory + own locks"]
    a2["Bank.Api #2<br/>own memory + own locks"]
    a3["Bank.Api #3<br/>own memory + own locks"]
    pg[("PostgreSQL<br/>one shared truth")]

    nginx --> a1 & a2 & a3
    a1 & a2 & a3 --> pg

    style a1 fill:#fde2e2,stroke:#c0392b,color:#111
    style a2 fill:#fde2e2,stroke:#c0392b,color:#111
    style a3 fill:#fde2e2,stroke:#c0392b,color:#111
    style pg fill:#e2f0d9,stroke:#2d7a2d,color:#111
```

Nothing about the *code* changed between these two diagrams. What changed is that
`lock` now guards one third of the traffic, which is the same as guarding none of it.
The database slices are unaffected, because their coordination point was never in
process memory.

This picture is the answer to "can your app scale?" — see `lessons/09-scaling.md`.

## A contended withdrawal, four ways

Five actors try to withdraw ₹100 each from an account holding ₹100. Only one should
succeed. What follows is what each strategy actually does.

### Naive — the lost update

```mermaid
sequenceDiagram
    participant A as Actor A
    participant B as Actor B
    participant S as Store

    A->>S: read balance
    S-->>A: 100
    B->>S: read balance
    S-->>B: 100
    Note over A,B: both decided "100 >= 100, allowed"
    A->>S: write 0
    B->>S: write 0
    Note over S: balance = 0, but ₹200 left the bank<br/>A's write was silently overwritten
```

The gap between read and write is the bug. Nothing here is "simultaneous" — B simply
read a value that was true, and stale by the time it wrote.

### In-process lock — correct, on one instance

```mermaid
sequenceDiagram
    participant A as Actor A
    participant B as Actor B
    participant L as SemaphoreSlim
    participant S as Store

    A->>L: acquire
    L-->>A: granted
    B->>L: acquire
    Note over B: blocked
    A->>S: read 100, write 0
    A->>L: release
    L-->>B: granted
    B->>S: read 0 -> rejected
```

Correct — and only because both actors ran in the same process, holding the same
semaphore object.

### Optimistic — detect and retry

```mermaid
sequenceDiagram
    participant A as Actor A
    participant B as Actor B
    participant DB as PostgreSQL

    A->>DB: SELECT (balance 100, xmin 746)
    B->>DB: SELECT (balance 100, xmin 746)
    A->>DB: UPDATE ... WHERE xmin = 746
    DB-->>A: 1 row -> committed, xmin now 747
    B->>DB: UPDATE ... WHERE xmin = 746
    DB-->>B: 0 rows -> DbUpdateConcurrencyException
    Note over B: re-read (balance 0), re-decide -> rejected
```

Nobody waited. The loser did wasted work and retried. Cheap when conflicts are rare,
increasingly wasteful as they get common.

### Pessimistic — queue up

```mermaid
sequenceDiagram
    participant A as Actor A
    participant B as Actor B
    participant DB as PostgreSQL

    A->>DB: BEGIN; SELECT ... FOR UPDATE
    DB-->>A: row locked, balance 100
    B->>DB: BEGIN; SELECT ... FOR UPDATE
    Note over B: blocked by the row lock
    A->>DB: UPDATE balance = 0; COMMIT
    DB-->>B: unblocked, balance 0
    B->>DB: rejected (insufficient funds); COMMIT
```

Nobody did wasted work. B waited instead. Cheap when conflicts are common, and a
throughput disaster if the transaction is long.

The trade between these last two is ADR-0007 and `lessons/05-choosing-between-them.md`.

## Request path through a slice

```mermaid
flowchart LR
    http["HTTP request"] --> ep["Endpoint.cs<br/>(slice-owned)"]
    ep --> h["Handler.cs<br/>(slice-owned)"]
    h --> gate["IInterleaveGate<br/>no-op in prod"]
    h --> store["store / DbContext"]
    h --> rec["IContentionRecorder"]
    rec --> hub["SignalR hub"]
    hub --> ui["Angular timeline"]
```

Two files per slice, no dispatcher and no pipeline in between (ADR-0002). The
`IInterleaveGate` call is a no-op in production and the reason race tests are
deterministic rather than flaky (ADR-0008).

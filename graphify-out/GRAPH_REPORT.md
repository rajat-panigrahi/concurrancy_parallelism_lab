# Graph Report - .  (2026-09-03)

## Corpus Check
- 171 files · ~71,798 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 911 nodes · 1537 edges · 71 communities (57 shown, 14 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 80 edges (avg confidence: 0.89)
- Token cost: 431,689 input · 0 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]
- [[_COMMUNITY_Community 19|Community 19]]
- [[_COMMUNITY_Community 20|Community 20]]
- [[_COMMUNITY_Community 21|Community 21]]
- [[_COMMUNITY_Community 22|Community 22]]
- [[_COMMUNITY_Community 23|Community 23]]
- [[_COMMUNITY_Community 24|Community 24]]
- [[_COMMUNITY_Community 25|Community 25]]
- [[_COMMUNITY_Community 26|Community 26]]
- [[_COMMUNITY_Community 27|Community 27]]
- [[_COMMUNITY_Community 28|Community 28]]
- [[_COMMUNITY_Community 29|Community 29]]
- [[_COMMUNITY_Community 30|Community 30]]
- [[_COMMUNITY_Community 31|Community 31]]
- [[_COMMUNITY_Community 32|Community 32]]
- [[_COMMUNITY_Community 33|Community 33]]
- [[_COMMUNITY_Community 34|Community 34]]
- [[_COMMUNITY_Community 35|Community 35]]
- [[_COMMUNITY_Community 36|Community 36]]
- [[_COMMUNITY_Community 37|Community 37]]
- [[_COMMUNITY_Community 38|Community 38]]
- [[_COMMUNITY_Community 39|Community 39]]
- [[_COMMUNITY_Community 40|Community 40]]
- [[_COMMUNITY_Community 41|Community 41]]
- [[_COMMUNITY_Community 42|Community 42]]
- [[_COMMUNITY_Community 43|Community 43]]
- [[_COMMUNITY_Community 44|Community 44]]
- [[_COMMUNITY_Community 45|Community 45]]
- [[_COMMUNITY_Community 46|Community 46]]
- [[_COMMUNITY_Community 47|Community 47]]
- [[_COMMUNITY_Community 48|Community 48]]
- [[_COMMUNITY_Community 49|Community 49]]
- [[_COMMUNITY_Community 50|Community 50]]
- [[_COMMUNITY_Community 51|Community 51]]
- [[_COMMUNITY_Community 52|Community 52]]
- [[_COMMUNITY_Community 53|Community 53]]
- [[_COMMUNITY_Community 54|Community 54]]
- [[_COMMUNITY_Community 55|Community 55]]
- [[_COMMUNITY_Community 56|Community 56]]
- [[_COMMUNITY_Community 57|Community 57]]
- [[_COMMUNITY_Community 58|Community 58]]
- [[_COMMUNITY_Community 59|Community 59]]
- [[_COMMUNITY_Community 60|Community 60]]
- [[_COMMUNITY_Community 61|Community 61]]
- [[_COMMUNITY_Community 62|Community 62]]

## God Nodes (most connected - your core abstractions)
1. `Trade-off matrix` - 19 edges
2. `CLAUDE.md project instructions` - 17 edges
3. `concurrency-reviewer agent` - 16 edges
4. `Lesson 09 — Can your app scale?` - 16 edges
5. `ContentionRecorder` - 14 edges
6. `BankDbContext` - 13 edges
7. `LabApiService` - 13 edges
8. `ContentionEvent` - 13 edges
9. `Lessons index` - 13 edges
10. `Concurrency & Parallelism Lab README` - 12 edges

## Surprising Connections (you probably didn't know these)
- `SemaphoreSlim, never lock, around await` --semantically_similar_to--> `ScaleEndpoints sync-over-async endpoint`  [INFERRED] [semantically similar]
  .claude/rules/concurrency-slices.md → CLAUDE.md
- `In-process lock breaks across three replicas` --semantically_similar_to--> `SignalR groups are per-process`  [INFERRED] [semantically similar]
  deploy/docker-compose.scale.yml → .claude/rules/angular-ui.md
- `Lost-update verdict (5 approved, 4 silent losers)` --references--> `NaiveWithdrawHandler (deliberately lossy)`  [INFERRED]
  README.md → CLAUDE.md
- `In-process lock breaks across three replicas` --references--> `PostgresAdvisoryAccountLock`  [INFERRED]
  deploy/docker-compose.scale.yml → .claude/rules/concurrency-slices.md
- `LabHarness` --references--> `ContentionRecorder`  [EXTRACTED]
  tests/Bank.Api.UnitTests/Infrastructure/LabHarness.cs → src/Bank.Api/Shared/Contention/ContentionRecorder.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Deliberate defects that must stay broken** — claude_naivewithdrawhandler, claude_inmemoryaccountstore, claude_scaleendpoints, claude_intereststrategies, claude_do_not_fix, rules_testing_invariantsuitehasteethtests [EXTRACTED 1.00]
- **Adding a concurrency slice end to end** — add_slice_skill, add_slice_slice_template, rules_concurrency_slices_iwithdrawstrategy, rules_concurrency_slices_icontentionrecorder, rules_testing_three_tiers, commands_adr, commands_lab [EXTRACTED 1.00]
- **Making a race deterministic without flaky tests** — docs_plan_iinterleavegate, rules_concurrency_slices_interleavecheckpoints, rules_concurrency_slices_gate_placement, claude_first_attempt_joins_barrier, rules_testing_no_unforced_ordering [INFERRED 0.85]
- **Process-local state that silently stops working at N replicas** — adr_0004_hybrid_persistence_inmemoryaccountstore, adr_0009_signalr_for_live_timeline_contentionhub, adr_0012_postgres_advisory_locks_inprocessaccountlock, adr_0013_in_memory_ring_buffer_bounded_buffer, architecture_system_overview_three_replica_topology [INFERRED 0.85]
- **The machinery that makes a race reproduce 100% of the time** — adr_0008_interleave_gate_in_production_code_iinterleavegate, adr_0008_interleave_gate_in_production_code_asyncbarrier, adr_0008_interleave_gate_in_production_code_interleavecheckpoints, adr_0016_three_tier_test_strategy_deterministic_tier, lessons_01_race_conditions_forcerace [EXTRACTED 1.00]
- **Blocked CDN, no Docker daemon, no proxy egress — constraints that decided three ADRs** — adr_0003_target_dotnet_8_lts_net8_target, adr_0005_postgresql_over_sqlserver_sqlite_sqlserver_rejected, adr_0010_three_perf_tools_k6, adr_0014_compose_not_kubernetes_compose_replicas, adr_0015_local_postgres_over_testcontainers_local_postgres [INFERRED 0.85]
- **The coordination point must live in shared state, not process memory** — lessons_02_in_process_locking_correct_on_one_instance_broken_on_three, lessons_09_scaling_statelessness, lessons_09_scaling_signalr_group_scale_out_gap, lessons_03_optimistic_concurrency_xmin_concurrency_token, lessons_04_pessimistic_concurrency_select_for_update [EXTRACTED 1.00]
- **Two correct strategies paying in different currencies** — lessons_03_optimistic_concurrency_pays_in_wasted_work, lessons_04_pessimistic_concurrency_pays_in_waiting, lessons_05_choosing_between_them_conflict_rate, lessons_05_choosing_between_them_crossover_point, compare_compare_component [EXTRACTED 1.00]
- **The family of concurrency failure modes** — lessons_06_deadlocks_coffman_conditions, lessons_06_deadlocks_livelock, lessons_06_deadlocks_starvation, lessons_09_scaling_threadpool_starvation, lessons_04_pessimistic_concurrency_connection_pool_exhaustion [INFERRED 0.85]

## Communities (71 total, 14 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.06
Nodes (65): add-slice skill, Slice handler template, ADR-0001 Vertical slice architecture, ADR-0002 Plain handler classes, no MediatR, concurrency-reviewer agent, Architecture index, CLAUDE.md project instructions, DbContext is not thread-safe (+57 more)

### Community 1 - "Community 1"
Cohesion: 0.07
Nodes (27): BackgroundService, CancellationToken, Completed, FraudTiming, DistributedLockWithdrawHandler, Failed, FraudRequest, FraudVerdict (+19 more)

### Community 2 - "Community 2"
Cohesion: 0.05
Nodes (23): Alice, Bob, CalculateInterestEndpoint, CalculateInterestHandler, DeadlockDemoRequest, DeadlockDemoResponse, DeadlockDemoEndpoint, DeadlockDemoHandler (+15 more)

### Community 3 - "Community 3"
Cohesion: 0.05
Nodes (44): build, extract-i18n, serve, test, architect, prefix, projectType, root (+36 more)

### Community 4 - "Community 4"
Cohesion: 0.07
Nodes (15): Benchmark, AggregationBenchmarks, AsyncOverheadBenchmarks, SynchronizationBenchmarks, RunLog, GlobalSetup, int, AsyncBarrier (+7 more)

### Community 5 - "Community 5"
Cohesion: 0.08
Nodes (17): TransferResult, DbContext, DeadlockTransferHandler, IAsyncLifetime, ICollectionFixture, IDbContextFactory, PostgresCollection, PostgresFixture (+9 more)

### Community 6 - "Community 6"
Cohesion: 0.06
Nodes (33): dependencies, @angular/common, @angular/compiler, @angular/core, @angular/forms, @angular/platform-browser, @angular/platform-browser-dynamic, @angular/router (+25 more)

### Community 7 - "Community 7"
Cohesion: 0.08
Nodes (21): BenchmarkDotNet (0.14.0), Microsoft.AspNetCore.Mvc.Testing (8.0.13), Microsoft.EntityFrameworkCore.Design (8.0.11), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.11), Respawn (6.2.1), Swashbuckle.AspNetCore (6.9.0), Microsoft.NET.Sdk.Web, Microsoft.NET.Sdk (+13 more)

### Community 8 - "Community 8"
Cohesion: 0.17
Nodes (13): Row, LabApiService, DeadlockResponse, FraudResponse, IdempotencyResponse, InterestResponse, StartRunRequest, StartRunResponse (+5 more)

### Community 9 - "Community 9"
Cohesion: 0.16
Nodes (5): Channel, ContentionRecorder, ContentionRecorderExtensions, IContentionRecorder, Guid

### Community 10 - "Community 10"
Cohesion: 0.16
Nodes (4): Fact, InterestStrategiesTests, NaiveWithdrawTests, PessimisticWithdrawTests

### Community 11 - "Community 11"
Cohesion: 0.18
Nodes (18): ADR-0004 Hybrid persistence: in-memory and PostgreSQL, BankDbContext over PostgreSQL, EF Core in-memory provider (rejected), Hybrid storage split (group A in-memory, group B PostgreSQL), ADR-0005 PostgreSQL as the database, ADR-0007 Optimistic by default, pessimistic by exception, ADR-0012 PostgreSQL advisory locks for distributed locking, ADR-0014 Docker Compose replicas; Kubernetes deliberately excluded (+10 more)

### Community 12 - "Community 12"
Cohesion: 0.11
Nodes (18): schematics, skipTests, skipTests, style, skipTests, skipTests, skipTests, skipTests (+10 more)

### Community 13 - "Community 13"
Cohesion: 0.17
Nodes (8): ConcurrentDictionary, IAsyncDisposable, IAccountLock, IAccountLockHandle, Handle, InProcessAccountLock, Handle, PostgresAdvisoryAccountLock

### Community 14 - "Community 14"
Cohesion: 0.17
Nodes (6): ContentionHubService, ContentionEvent, ContentionPhase, Lane, Marker, RaceLabComponent

### Community 15 - "Community 15"
Cohesion: 0.15
Nodes (17): Never hold a lock across I/O you don't control, Lesson 03 — Optimistic concurrency, Bounded retry loop with re-read and re-decide, The hot row, Optimistic concurrency pays in wasted work, xmin as an EF Core concurrency token, Lesson 04 — Pessimistic concurrency, Connection pool exhaustion from blocked transactions (+9 more)

### Community 16 - "Community 16"
Cohesion: 0.13
Nodes (16): BenchmarkDotNet (micro, in-process), Timing assertions must be relative, never absolute, TimingSensitiveCollection, ValueTask on hot sync paths, AggregationBenchmarks report, Parallel + thread-local sums (964.6 μs, 0.28× sequential), AsyncOverheadBenchmarks report, Task.Run wrapping sync work — 1,857× slower (+8 more)

### Community 17 - "Community 17"
Cohesion: 0.20
Nodes (5): AccountId, PostgresLabHarness, Summary, Version, DistributedLockWithdrawTests

### Community 18 - "Community 18"
Cohesion: 0.13
Nodes (8): Bank.Api.Shared.Persistence.Migrations, InitialAccounts, AddIdempotencyRecords, Bank.Api.Shared.Persistence.Migrations, Bank.Api.Shared.Persistence.Migrations, BankDbContextModelSnapshot, ModelBuilder, ModelSnapshot

### Community 19 - "Community 19"
Cohesion: 0.26
Nodes (4): AccountSnapshot, IReadOnlyCollection, InMemoryAccountStore, MutableAccount

### Community 20 - "Community 20"
Cohesion: 0.23
Nodes (3): InterestEngine, InterestStrategies, InterestAccount

### Community 21 - "Community 21"
Cohesion: 0.20
Nodes (12): ADR-0009 SignalR for the live timeline, ContentionBroadcaster BackgroundService, ContentionHub (WatchRun / StopWatchingRun), SignalR live contention feed, Server-Sent Events (rejected, close call), ADR-0013 Contention events in a bounded in-memory buffer, Bounded per-run event buffer (20,000 events, 50 runs), Channel<ContentionEvent> producer/consumer split (+4 more)

### Community 22 - "Community 22"
Cohesion: 0.21
Nodes (4): IDisposable, IInterleaveGate, LabInterleaveGate, RegistrationHandle

### Community 23 - "Community 23"
Cohesion: 0.20
Nodes (12): Lesson 07 — Parallelism (CPU-bound), Amdahl's law, False sharing, Parallel.ForEach with an async lambda is async void, Preconditions for parallelism paying off, Lesson 08 — Async fan-out (I/O-bound), await inside a loop serialises independent calls, await releases threads rather than creating them (+4 more)

### Community 24 - "Community 24"
Cohesion: 0.21
Nodes (6): Migration, MigrationBuilder, Bank.Api.Shared.Persistence.Migrations, InitialAccounts, AddIdempotencyRecords, Bank.Api.Shared.Persistence.Migrations

### Community 25 - "Community 25"
Cohesion: 0.26
Nodes (3): Task, InvariantSuiteHasTeethTests, OptimisticWithdrawTests

### Community 26 - "Community 26"
Cohesion: 0.18
Nodes (11): ADR-0003 Target .NET 8 LTS, CS1705 assembly-version build break, Directory.Build.props central pinning, Multi-targeting net8.0;net10.0 (rejected), net8.0 as pinned target framework, System.Threading.Lock (.NET 9+), Lesson 02 — Locking inside one process, Lock granularity: global vs per-account vs per-field (+3 more)

### Community 27 - "Community 27"
Cohesion: 0.22
Nodes (11): InMemoryAccountStore (singleton, deliberately unsafe), static ConcurrentDictionary as source of truth (trap), Redis backplane and sticky sessions, Three-replica Compose demo behind nginx, INSTANCE_ID per service, Kubernetes (rejected), nginx round-robin (not sticky), Three replicas behind nginx topology (+3 more)

### Community 28 - "Community 28"
Cohesion: 0.24
Nodes (11): k6 load scenarios, k6 scenario: hot-account.js, k6 scenario: threadpool.js, In-process lock: correct on one instance, silently broken on three, Lesson 09 — Can your app scale?, The costs of scaling out, SignalR groups are per-process (ContentionHub), What stateless actually means (+3 more)

### Community 29 - "Community 29"
Cohesion: 0.20
Nodes (10): PostgreSQL 16, SELECT … FOR UPDATE row locking, SQLite (rejected on capability), SQL Server (rejected on friction), Pessimistic used deliberately for named reasons, ADR-0011 Angular standalone components + signals, no NgRx, ContentionHubService (degrades silently), LabApiService (+2 more)

### Community 30 - "Community 30"
Cohesion: 0.24
Nodes (10): xmin system column as concurrency token, UI holds no business logic, LabRunStore / RunSummary retention, Vacuous test failure mode, A contended withdrawal, four ways, The lost update (measured: -400 discrepancy), RunSummaryCalculator, The sawVer column — every actor read version 1 (+2 more)

### Community 31 - "Community 31"
Cohesion: 0.20
Nodes (10): Conflict rate as the deciding variable, Jittered exponential backoff (named, not implemented), OptimisticWithdrawHandler.MaxAttempts = 5, Optimistic concurrency as the default, The five sentences worth memorising, Idempotency key, One atomic statement (SET balance = balance - n WHERE …), Optimistic (apologise later) vs pessimistic (ask permission first) (+2 more)

### Community 32 - "Community 32"
Cohesion: 0.20
Nodes (10): InterleaveCheckpoints (AfterRead / BeforeAcquire), k6 scripts (loadtests/k6), pg_advisory_lock as distributed lock, Fencing token, IAccountLock abstraction, InProcessAccountLock, PostgresAdvisoryAccountLock, Redis / Redlock (rejected) (+2 more)

### Community 33 - "Community 33"
Cohesion: 0.27
Nodes (5): AppComponent, appConfig, routes, BankLabUi README (Angular CLI 19.2.27), UI host page (app-root)

### Community 34 - "Community 34"
Cohesion: 0.42
Nodes (3): Handler, RunId, DeadlockTransferTests

### Community 35 - "Community 35"
Cohesion: 0.31
Nodes (3): InlineData, Theory, IdempotencyTests

### Community 36 - "Community 36"
Cohesion: 0.24
Nodes (10): Partition the work, aggregate at the end, Per-item synchronisation makes parallel slower than sequential, Lesson 10 — Benchmarking vs load testing, Micro-benchmarking with BenchmarkDotNet, Load testing the whole system, Profile, benchmark, load test, measure in production, A benchmark whose conditions you cannot state is a number, not evidence, Lesson 11 — Interview questions with defensible answers (+2 more)

### Community 37 - "Community 37"
Cohesion: 0.20
Nodes (9): applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, ASPNETCORE_ENVIRONMENT, profiles, Bank.Api (+1 more)

### Community 38 - "Community 38"
Cohesion: 0.28
Nodes (5): IdempotencyDemoRequest, IdempotencyDemoResponse, IdempotencyDemoHandler, IdempotentTransferHandler, IdempotentWithdrawResult

### Community 39 - "Community 39"
Cohesion: 0.25
Nodes (8): Only the first attempt joins the interleaving barrier, AsyncBarrier, Microsoft Coyote / CHESS deterministic schedulers, IInterleaveGate injected seam, LabInterleaveGate, Thread.Sleep/Task.Delay in the handler (rejected), Statistical brute force (rejected), Tier 1 — deterministic forced-interleaving tests

### Community 40 - "Community 40"
Cohesion: 0.25
Nodes (7): hooks, PostToolUse, permissions, allow, ask, deny, $schema

### Community 43 - "Community 43"
Cohesion: 0.25
Nodes (4): Card, LearnComponent, Lesson, Lessons index

### Community 44 - "Community 44"
Cohesion: 0.29
Nodes (7): ADR-0008 A test-controlled interleaving seam in production code, ADR-0010 Three performance tools, three different jobs, Benchmarking vs load testing separation, In-repo load harness (tests/Bank.LoadTests), NBomber (rejected on licence), System overview, Lesson 01 — Race conditions: who wins, and who loses silently

### Community 45 - "Community 45"
Cohesion: 0.38
Nodes (3): ConcurrentQueue, RunSummary, LabRunStore

### Community 46 - "Community 46"
Cohesion: 0.38
Nodes (4): RunSummaryCalculator, ActorOutcome, IReadOnlySet, RunFacts

### Community 47 - "Community 47"
Cohesion: 0.33
Nodes (7): Lesson 06 — Deadlocks, The four Coffman conditions, Make the timing a parameter, not an accident, Ordered lock acquisition, Postgres deadlock detection (SQLSTATE 40P01), Starvation, Testing a race by making the interleaving a parameter

### Community 51 - "Community 51"
Cohesion: 0.33
Nodes (3): asyncLatency, options, syncLatency

### Community 53 - "Community 53"
Cohesion: 0.60
Nodes (4): hotAccount(), options, spreadAcross(), startRun()

### Community 58 - "Community 58"
Cohesion: 0.67
Nodes (3): Tier 2 — invariant / chaos tests, Shared invariants inherited via IWithdrawStrategy, Never assert an ordering you did not force

## Knowledge Gaps
- **150 isolated node(s):** `validate-adr.sh script`, `$schema`, `allow`, `ask`, `deny` (+145 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `RunSummary` connect `Community 45` to `Community 8`, `Community 14`, `Community 46`, `Community 17`, `Community 57`?**
  _High betweenness centrality (0.150) - this node is a cross-community bridge._
- **Why does `Lesson 10 — Benchmarking vs load testing` connect `Community 36` to `Community 43`, `Community 28`?**
  _High betweenness centrality (0.108) - this node is a cross-community bridge._
- **Why does `Dependency licence screening` connect `Community 28` to `Community 0`, `Community 36`?**
  _High betweenness centrality (0.104) - this node is a cross-community bridge._
- **What connects `validate-adr.sh script`, `$schema`, `allow` to the rest of the system?**
  _170 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.06201923076923077 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06988120195667366 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.05052790346907994 - nodes in this community are weakly interconnected._
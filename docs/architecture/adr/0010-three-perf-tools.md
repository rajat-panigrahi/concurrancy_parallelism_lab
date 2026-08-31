# ADR-0010 — Three performance tools, three different jobs

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The project needs to answer two different performance questions, and they are not the
same question:

1. **"Is this code fast?"** — is `Interlocked` cheaper than `lock`? Does thread-local
   aggregation beat per-item synchronisation? Microseconds, in-process.
2. **"Does this system hold up?"** — what happens to p99 at 500 concurrent users? Does
   the API survive scale-out? Requests per second, over the network.

Using one tool for both produces confident nonsense. A micro-benchmark of an HTTP
endpoint measures a single sequential request with no connection pooling and no
contention — the exact conditions under which the interesting failures don't happen.

## Options considered

- **BenchmarkDotNet** — the .NET standard for micro-benchmarks. MIT licensed. Handles
  JIT warmup, tiered compilation, outlier detection, statistical significance, memory
  diagnostics and dead-code elimination.
- **NBomber** — a .NET-native load testing framework. Runs as a normal project, no
  external binaries, good reporting.
- **k6** — the industry-standard load tool. JavaScript scenarios, excellent output,
  widely recognised.
- **A purpose-built load harness** — a few hundred lines of `HttpClient`, virtual users
  and a latency histogram.

## Decision

Three tools, each for its own job:

| Tool | Job | Where |
|---|---|---|
| **BenchmarkDotNet** | micro, in-process, µs | `src/Bank.Benchmarks` |
| **In-repo load harness** | macro, over HTTP, RPS/p95/p99 | `tests/Bank.LoadTests` |
| **k6 scripts** | macro, industry-standard, run on your machine | `loadtests/k6/` |

## Why not the others

- **NBomber was the original plan and was dropped after reading its licence.** Version
  3.0 (effective September 2025) is a **commercial subscription** agreement — its NuGet
  package sets `requireLicenseAcceptance` and ships a licence file rather than an SPDX
  open-source expression. That is the same disqualifier applied to MediatR and
  FluentAssertions in ADR-0002: a public teaching repository should not hand readers a
  dependency they cannot use at work without buying something. The replacement is a
  small harness with no dependency at all.
- **k6 alone** would have been the better macro tool if it could run here, but its binary
  cannot be downloaded through this environment's proxy. Its scripts are committed
  anyway, because k6 is what most teams actually use and the scripts are useful on a
  developer's own machine.
- **BenchmarkDotNet alone** cannot answer question 2 at all. Pointing it at an HTTP
  endpoint is a category error: no concurrency, no pooling, no contention, and it treats
  a network hop as if it were code.

## Trade-offs accepted

- **The in-repo harness is worse than k6 or NBomber.** No ramp-up profiles, no
  distributed load generation, no polished reporting, and it has not been battle-tested
  by anyone else. It computes RPS and percentiles from a latency array and stops there.
- **Writing our own measurement tool risks measuring it rather than the system.** A
  harness that is itself the bottleneck reports its own limits as the API's. Mitigated by
  keeping it simple and async, and by cross-checking against the committed k6 scripts.
- **Three tools is more to explain** than one.
- **The benchmark numbers in this repo are noisy.** A 4-core shared cloud VM under a
  `ShortRun` job produces error bars of the same order as some means. Good enough for
  the order-of-magnitude claims made (11×, 1,857×), useless for anything subtler — which
  the results README states explicitly rather than quietly hoping nobody checks.

## Consequences

- `Bank.Benchmarks` never makes an HTTP call; `Bank.LoadTests` never calls a method
  directly. The separation is structural, so the category error is hard to make by
  accident.
- BenchmarkDotNet output is committed under `docs/benchmarks/` with an explicit statement
  of the conditions it was produced under.
- The harness writes JSON into `artifacts/`, which the Angular Scale Lab renders.
- Lesson 10 exists to teach the distinction itself, since confusing the two is a common
  and revealing mistake.

## Interview angle

**The 60-second version:** "BenchmarkDotNet for micro-benchmarks — it handles JIT warmup,
statistical significance and allocation tracking, and hand-rolled `Stopwatch` code gets
all of that wrong below about a millisecond. But never point it at an endpoint: that's a
load test, which is a different tool measuring different things. Benchmarking finds slow
code; load testing finds slow systems, and the bottleneck is usually a pool or a lock,
which no in-process benchmark can see."

**What a good interviewer asks next:** *"Give me an example where a fast method still
produced a slow endpoint."* This repo has one measured: sync-over-async. The method
benchmarks fine, and under concurrent load it causes thread-pool starvation and the p99
falls off a cliff on identical hardware. That is the answer that shows you understand why
both tools exist.

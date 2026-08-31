# ADR-0003 — Target .NET 8 LTS

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The concurrency primitives this project teaches — `lock`, `SemaphoreSlim`,
`Interlocked`, `Task.WhenAll`, `Parallel.ForEachAsync`, `CancellationToken`, EF Core
concurrency tokens — are stable across every modern .NET version. The choice is
therefore about tooling, reach and constraints rather than capability.

One hard constraint decided it: the build environment reaches nuget.org and the
Ubuntu package archive, but **`builds.dotnet.microsoft.com` is blocked** (HTTP 403 at
the proxy). That makes Microsoft's `dotnet-install.sh` — the only supported route to
.NET 9 or 10 here — unusable. Ubuntu's archive carries `dotnet-sdk-8.0` (8.0.130).

## Options considered

- **.NET 8 (LTS).** Installable in this environment. Still the most widely deployed
  version in enterprises, so it matches what most interviews are actually about.
- **.NET 10 (current LTS).** Newest. Notably ships `System.Threading.Lock`
  (introduced in .NET 9), a dedicated lock type that replaces `lock` on `object` and
  is a nice talking point.
- **Multi-target** (`net8.0;net10.0`) to demonstrate version differences.

## Decision

Target `net8.0`, pinned centrally in `Directory.Build.props`.

## Why not the others

- **.NET 10** cannot be installed in this environment at all — the SDK download host
  is blocked by the network policy. This isn't a preference, it's a wall. It would
  also have gained little: nothing in the syllabus requires a post-8 API.
- **Multi-targeting** would double build and test time and force `#if NET10_0_OR_GREATER`
  branches through teaching code, to demonstrate one keyword. The `System.Threading.Lock`
  difference is better *described* in a lesson than built into the project structure.

## Trade-offs accepted

- **We can't demonstrate `System.Threading.Lock` in running code.** It's covered in
  `lessons/02-in-process-locking.md` as text instead, which is weaker than a runnable
  example.
- **.NET 8 goes out of support before .NET 10 does.** For a lab that is reread and
  re-run over a couple of years, that will eventually mean an upgrade.
- Anyone reading this repo on a machine with a newer SDK builds under a roll-forward
  runtime rather than the one they have installed — usually invisible, occasionally
  confusing.

## Consequences

- `Directory.Build.props` pins `TargetFramework` once for all five projects, so the
  upgrade later is a one-line change plus a package bump.
- Package versions are pinned to the 8.0.x line (EF Core 8.0.11, Npgsql 8.0.11) and
  must stay mutually consistent — mixing 8.0.11 and 8.0.13 already produced a
  `CS1705` assembly-version build break once during setup.
- The CI workflow pins `dotnet-version: 8.0.x` to match.

## Interview angle

**The 60-second version:** "I targeted .NET 8 because it's LTS and it's what most
production systems I'd be joining actually run. The concurrency APIs are identical on
9 and 10 — the only relevant addition is `System.Threading.Lock` in .NET 9, which
gives you a real lock type instead of locking on a plain object and avoids the classic
mistake of locking on something publicly reachable like `this` or a string."

**What a good interviewer asks next:** *"What would change if you moved to .NET 9 or
10?"* Concretely: `System.Threading.Lock` for in-process locking; otherwise nothing in
this codebase. Being able to say "nothing, and here's why" is a better answer than
listing release notes — it shows you know which changes touch your design and which
are noise.

# ADR-0009 — SignalR for the live timeline

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

The UI's main screen draws a swimlane per actor and fills it in as the run happens. A
run lasts milliseconds, so "live" is partly theatre — but it matters, because watching
events arrive in order is what makes the interleaving legible.

The server needs to push contention events to whoever is watching a given run.

## Options considered

- **SignalR.** The .NET default. WebSockets with automatic fallback to Server-Sent
  Events and long polling, connection lifetime handling, groups, typed hubs, and a
  first-party JS client (`@microsoft/signalr`).
- **Server-Sent Events (SSE).** A plain `text/event-stream` endpoint. No library on
  either side — `EventSource` is built into browsers. One-directional, which is all
  this needs.
- **Poll the completed run.** Fire the run, then `GET /api/lab/runs/{id}` and animate
  client-side from the full timeline.

## Decision

SignalR, with `GET /api/lab/runs/{id}` retained as a fallback and for replay.

## Why not the others

- **SSE** was genuinely close and is arguably the better engineering choice in
  isolation: the feed is one-directional, so SignalR's bidirectionality is unused, and
  SSE needs no client dependency at all. It lost on two counts. First, SignalR's
  group model gives per-run fan-out for free, where SSE would need hand-rolled
  subscriber bookkeeping. Second — and decisively for this project — SignalR is a
  standard .NET interview topic in its own right, and a repo that exists to prepare
  for .NET interviews should use the thing that gets asked about.
- **Polling for the finished run** is the simplest and most reliable option, and it is
  kept as a fallback for exactly that reason. It loses the property that matters
  most: you see events arrive *while contention is happening*, rather than watching a
  reconstruction. For a tool whose job is to make timing intuitive, that's the feature.

## Trade-offs accepted

- **A client dependency and a connection lifecycle.** `@microsoft/signalr` in the
  Angular app, plus reconnection, missed-event and out-of-order handling that a plain
  `GET` would not need.
- **Groups are per server.** This is the significant one. SignalR groups live in the
  process, so with multiple replicas a client connected to instance 2 will not receive
  events published on instance 1. Fixing it needs a backplane (Redis, Azure SignalR).
  This is *the same bug as the in-process lock*, in a different costume — process-local
  state that silently stops working at scale.
- **Sticky sessions.** WebSocket connections are long-lived, so the load balancer must
  keep a client on one instance. Another constraint scale-out imposes on the
  application layer.
- **Recording must not block on the network.** Pushing to SignalR inline from
  `Record()` would put a network call inside the critical section being measured — the
  observer would change the timing it reports. Handled by the producer/consumer split
  in ADR-0013.

## Consequences

- `ContentionHub` exposes `WatchRun`/`StopWatchingRun`; clients join group `run-{id}`.
- `ContentionBroadcaster` is a `BackgroundService` draining the recorder's channel, so
  a slow or broken client can never slow down a bank withdrawal. Broadcast failures are
  logged and swallowed.
- The multi-replica demo (ADR-0014) must either run the UI against a single instance or
  accept missing events — and that limitation is itself worth demonstrating, since it
  makes the same point as the lock slice.
- The REST timeline endpoint stays authoritative; SignalR is an accelerator, not the
  source of truth.

## Interview angle

**The 60-second version:** "SignalR, because it's the .NET-native way to push and it
gave me per-run groups for free. SSE would have been lighter — the feed is
one-directional — but SignalR handles transport fallback and connection lifetime, and
I wanted the group model. The catch worth knowing is that SignalR groups are
per-process: at more than one replica you need a Redis backplane and sticky sessions,
or clients silently miss events."

**What a good interviewer asks next:** *"What happens to SignalR when you scale out?"*
This is the actual question behind the choice, and the answer is the backplane. The
follow-up worth having ready: it is the same class of bug as an in-memory lock —
process-local state that works perfectly on one instance and degrades *silently* on
several. Being able to spot that pattern across two unrelated technologies is what the
question is really testing.

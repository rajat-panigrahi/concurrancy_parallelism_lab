# 05 — Choosing: optimistic or pessimistic?

> The question is not "which is better". It is **"how often do two people actually
> collide on the same row?"** Everything else follows from that number.

This is the lesson to reread before an interview. It is the one you will be asked.

## The measured difference

Both strategies, identical input — 5 actors, ₹100 each, ₹100 balance, forced race:

| | Optimistic | Pessimistic |
|---|---|---|
| Correct | Yes | Yes |
| Attempts | 9 | 5 |
| Conflicts | 4 | 0 |
| Total waiting | 0.0 ms | 244.1 ms |
| **Pays in** | **wasted work** | **waiting** |

Run it yourself — `/api/lab/runs` with each strategy — and change `actors` to watch the
curves diverge.

## The decision table

| Situation | Use | Why |
|---|---|---|
| Web API, short transactions, conflicts rare | **Optimistic** | No locks held across requests; cost is an occasional retry |
| Hot row everyone writes (counter, inventory, one account) | **Pessimistic** | Optimistic would retry-storm: N× the work for 1× the throughput |
| User think time (an edit form open for minutes) | **Optimistic** | Never hold a database lock across a coffee break |
| Redoing the work is expensive or has side effects | **Pessimistic** | A retry means doing it twice; sometimes that's a second email |
| First-come-first-served is a business rule | **Pessimistic** | A queue is fair; retries are not — the unlucky actor can starve |
| Single process, in-memory state | **`lock` / `SemaphoreSlim`** | Cheapest — but dies the moment you add an instance |
| Multiple instances, must serialise non-DB work | **Distributed lock** | Process memory isn't shared (lesson 09) |
| The whole operation fits in one SQL statement | **Neither** | `UPDATE … SET balance = balance - 40 WHERE id = ? AND balance >= 40` — no gap, no coordination |

That last row is worth taking seriously. The cheapest concurrency control is **not
needing any**, because the database does read-decide-write atomically in one statement.
Reach for it first; the other rows are for when your logic doesn't fit.

## The crossover

```
throughput
    │
    │──────────────  optimistic (no blocking)
    │              ╲
    │               ╲          ← conflicts start costing more than waiting would
    │────────────────╲─────
    │                 ╲  pessimistic (serialised, but no rework)
    │                  ────────────────────
    └──────────────────────────────────────► contention
                     ↑
              the crossover point
```

- **Low contention:** optimistic wins easily. Almost nothing conflicts, so you get
  lock-free throughput and pay nothing.
- **High contention:** pessimistic wins. Optimistic's retries multiply the work while
  throughput stays flat — you burn CPU to achieve the same serialisation a lock would
  have given you cheaply.

Where the crossover sits depends on transaction length and conflict rate, so **measure
it** rather than guessing. That is exactly what the lab is for.

## How to actually decide

Ask three questions, in order:

**1. Can it be one atomic statement?** Then do that and stop. No tokens, no locks.

**2. What fraction of writes will touch a row somebody else is writing?**
   Rough, order-of-magnitude:
   - **< 1%** → optimistic, comfortably.
   - **1–10%** → optimistic with bounded retries and jittered backoff. Watch the retry rate.
   - **> 10%** → pessimistic, or redesign so the hot row stops being hot.

**3. How long is the transaction, and does it wait on anything you don't control?**
   Any external call inside the critical section rules out pessimistic locking —
   you'd be holding a database lock hostage to a third party's latency.

## Redesigning so the question goes away

The best answer to a hot row is often to stop having one. Worth naming in an interview,
because it shows you can think past the immediate mechanism:

- **Shard the counter.** Instead of one row incremented by everyone, keep 10 rows and
  sum them on read. Contention drops by 10×.
- **Queue the writes.** Accept the request, put it on a queue, let one consumer apply
  changes serially. Turns contention into latency you control.
- **Event-source it.** Append immutable events instead of mutating a balance. Appends
  don't conflict; you compute the balance by folding them.
- **Batch.** Apply 100 changes in one transaction instead of 100 transactions.

## The trap that catches people

**Both strategies look identical in testing.** With one user there are no conflicts, so
optimistic never retries and pessimistic never waits. The difference only appears under
concurrency — which is exactly when you are least able to experiment.

This is why the lab forces the race, and why the `forceRace: false` option exists: to
show how *rarely* the problem surfaces by chance, and therefore how easily it reaches
production.

## What an interviewer asks next

*"Which would you use for a banking withdrawal?"* — The honest answer is "it depends on
contention", and then commit: for a normal retail account, optimistic, because two
people rarely touch the same account simultaneously. For something genuinely hot — a
merchant settlement account taking thousands of writes a second — pessimistic, or shard
it. Naming the deciding variable is what they're listening for; "optimistic, always" is
the answer that loses.

*"Can you use both?"* — Yes, and mature systems do: optimistic by default, with a
pessimistic path for known-hot entities. You can also start optimistic and escalate
after N conflicts.

*"What about `Serializable` isolation?"* — It prevents the anomaly too, but by aborting
transactions with a serialization failure, so you still need retry logic — it's
optimistic in character, applied by the engine rather than by you. It also serialises
more than you probably intend.

*"What if the two writers are in different services?"* — Neither mechanism helps across
service boundaries with separate databases. That's where sagas, idempotency keys and
compensating transactions come in — the roadmap's next chapter, and lesson 09 names
where they'd attach.

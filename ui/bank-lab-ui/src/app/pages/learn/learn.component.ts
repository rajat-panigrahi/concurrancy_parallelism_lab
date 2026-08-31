import { Component, signal } from '@angular/core';

interface Card {
  question: string;
  answer: string;
  evidence?: string;
}

interface Lesson {
  number: string;
  title: string;
  takeaway: string;
  file: string;
}

@Component({
  selector: 'app-learn',
  standalone: true,
  templateUrl: './learn.component.html',
  styleUrl: './learn.component.scss',
})
export class LearnComponent {
  readonly flipped = signal<Set<number>>(new Set());

  readonly lessons: Lesson[] = [
    { number: '00', title: 'The mental model', takeaway: 'Concurrency is one teller juggling; parallelism is many tellers', file: '00-mental-model.md' },
    { number: '01', title: 'Race conditions', takeaway: 'The bug is the gap between read and write, not simultaneity', file: '01-race-conditions.md' },
    { number: '02', title: 'Locking in one process', takeaway: 'Correct on one instance, silently broken on three', file: '02-in-process-locking.md' },
    { number: '03', title: 'Optimistic concurrency', takeaway: 'Nobody waits; the cost is wasted work', file: '03-optimistic-concurrency.md' },
    { number: '04', title: 'Pessimistic concurrency', takeaway: 'Nobody redoes work; the cost is waiting', file: '04-pessimistic-concurrency.md' },
    { number: '05', title: 'Choosing between them', takeaway: 'The deciding variable is the conflict rate', file: '05-choosing-between-them.md' },
    { number: '06', title: 'Deadlocks', takeaway: 'Two correct operations, one cycle — fixed by ordering locks', file: '06-deadlocks.md' },
    { number: '07', title: 'Parallelism (CPU)', takeaway: 'How you aggregate matters more than whether you parallelise', file: '07-parallelism-cpu-bound.md' },
    { number: '08', title: 'Async fan-out (I/O)', takeaway: '2005ms → 204ms on the same cores; waiting overlapped', file: '08-async-io-bound.md' },
    { number: '09', title: 'Scaling', takeaway: 'Three layers: code, instances, data — in that order', file: '09-scaling.md' },
    { number: '10', title: 'Benchmarking vs load testing', takeaway: 'Benchmarking finds slow code; load testing finds slow systems', file: '10-benchmarking-vs-load-testing.md' },
    { number: '11', title: 'Interview questions', takeaway: 'Every answer backed by something runnable here', file: '11-interview-questions.md' },
  ];

  readonly cards: Card[] = [
    {
      question: 'Concurrency vs parallelism?',
      answer: 'Concurrency is structure — dealing with many things at once. Parallelism is execution — doing many things at once. One teller juggling five customers vs four tellers at four counters.',
      evidence: '10 × 200ms calls finished in 204ms on 5 threads, 4 cores.',
    },
    {
      question: 'Does async create threads?',
      answer: 'No — it releases them. await Task.Delay registers a continuation and returns the thread to the pool. Thread.Sleep holds its thread doing nothing.',
    },
    {
      question: 'What is a race condition?',
      answer: 'Read → think → write, where somebody else wrote during your think. Nothing has to be simultaneous — a single core with one thread still races if interrupted between the read and the write.',
    },
    {
      question: 'Is ConcurrentDictionary thread-safe?',
      answer: 'Yes, and it does not save you. It protects its own internals. TryGetValue, decide, TryUpdate is still a race. Concurrent collections protect their invariants, never yours.',
    },
    {
      question: 'Why SemaphoreSlim instead of lock?',
      answer: 'You cannot await inside a lock. A monitor is owned by the thread that entered it, and an await may resume on a different thread — which would release a lock it never took. SemaphoreSlim is owned by nobody.',
    },
    {
      question: 'Optimistic or pessimistic — which and when?',
      answer: 'Optimistic assumes collisions are rare: let everyone write, reject stale writes, retry the loser. Pessimistic assumes they are common: lock first, queue. The deciding variable is the conflict rate.',
      evidence: 'Same input: optimistic 9 attempts / 4 conflicts / 0ms waiting. Pessimistic 5 attempts / 0 conflicts / 244ms waiting.',
    },
    {
      question: 'What do you do on DbUpdateConcurrencyException?',
      answer: 'Re-read and re-DECIDE, not just re-send — with a fresh DbContext, because reusing one keeps the stale entity tracked and the retry re-sends the same doomed UPDATE forever. And bound the retries: unbounded is a livelock.',
    },
    {
      question: 'How do you prevent deadlocks?',
      answer: 'Acquire locks in a consistent global order — that breaks the circular wait, which is the cheapest of the four Coffman conditions to break. Sorting by primary key is the usual choice.',
      evidence: 'Postgres detects the cycle and kills one transaction (40P01). Money is conserved either way.',
    },
    {
      question: 'I parallelised my loop and it got slower. Why?',
      answer: 'Almost always per-item synchronisation. If every iteration takes a lock, four cores queue at one door instead of computing. Partition the work and aggregate once at the end.',
      evidence: 'Per-item lock: 11× slower than one core. Thread-local sums: 3.68× faster.',
    },
    {
      question: 'Is Task.Run making my code async?',
      answer: 'No. It moves blocking work to a thread-pool thread and waits for it — you now occupy two threads instead of one. Async has to go all the way down.',
      evidence: 'Task.Run around trivial sync work: 1,857× slower, 1.3MB allocated.',
    },
    {
      question: 'Can your app scale?',
      answer: 'Three questions. Code: async all the way down. Instances: only works if stateless. Data: usually row contention, not CPU. And ask which of the three is actually the problem first.',
      evidence: 'One .Result instead of await: 979 RPS → 35 RPS, p99 105ms → 3531ms, same hardware.',
    },
    {
      question: 'What breaks first when you scale out?',
      answer: 'Anything process-local: locks, caches, sessions, rate-limit counters, in-process schedulers, and SignalR groups. Then database connection count.',
    },
    {
      question: 'How would you test a race condition?',
      answer: 'Not by running it a lot and hoping. Make the interleaving a parameter — a gate that holds every actor after its read until all have read — so it reproduces 100% of the time. Then assert invariants, never orderings.',
    },
    {
      question: 'Is BenchmarkDotNet worth it?',
      answer: 'Yes for micro-benchmarks — it handles JIT warmup, statistical significance and allocations. Never point it at an HTTP endpoint; that is a load test. Benchmarking finds slow code, load testing finds slow systems.',
    },
    {
      question: 'What is false sharing?',
      answer: 'Two cores writing to different variables in the same cache line, so each write invalidates the other core’s cache. Symptom: correct, parallel, and slower than expected.',
    },
    {
      question: 'Concurrency-safe means idempotent, right?',
      answer: 'No — completely different guarantees. A withdrawal applied twice is still money gone twice. You need an idempotency key, and the uniqueness check must be in the database.',
      evidence: 'Without a key: 5 retries of ONE withdrawal charged 5×. With a key: applied once.',
    },
  ];

  toggle(index: number): void {
    this.flipped.update((set) => {
      const next = new Set(set);
      next.has(index) ? next.delete(index) : next.add(index);
      return next;
    });
  }

  isFlipped(index: number): boolean {
    return this.flipped().has(index);
  }

  flipAll(open: boolean): void {
    this.flipped.set(open ? new Set(this.cards.map((_, i) => i)) : new Set());
  }
}

import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { LabApiService } from '../../core/lab-api.service';
import { DeadlockResponse, IdempotencyResponse } from '../../core/models';

/** Load-test figures measured by tests/Bank.LoadTests on a 4-core VM. */
interface LoadResult {
  name: string;
  rps: number;
  p50: number;
  p95: number;
  p99: number;
}

@Component({
  selector: 'app-scale',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './scale.component.html',
  styleUrl: './scale.component.scss',
})
export class ScaleComponent {
  private readonly api = inject(LabApiService);

  /**
   * Committed measurements rather than a live run: generating real load from the
   * browser would measure the browser. Reproduce with
   * `dotnet run --project tests/Bank.LoadTests -c Release`.
   */
  readonly loadResults: LoadResult[] = [
    { name: 'async (proper await)', rps: 979.0, p50: 101.9, p95: 104.2, p99: 105.4 },
    { name: 'sync-over-async (.Result)', rps: 35.2, p50: 2760.2, p95: 3530.4, p99: 3530.7 },
  ];

  readonly deadlock = signal<DeadlockResponse | null>(null);
  readonly idempotency = signal<IdempotencyResponse | null>(null);
  readonly busyDeadlock = signal(false);
  readonly busyIdempotency = signal(false);
  readonly error = signal<string | null>(null);

  runDeadlock(orderLocks: boolean): void {
    this.busyDeadlock.set(true);
    this.error.set(null);

    this.api.deadlock(orderLocks).subscribe({
      next: (wrapper) => { this.deadlock.set(wrapper.response); this.busyDeadlock.set(false); },
      error: () => { this.error.set('The deadlock demo failed.'); this.busyDeadlock.set(false); },
    });
  }

  runIdempotency(useKey: boolean): void {
    this.busyIdempotency.set(true);
    this.error.set(null);

    this.api.idempotency(useKey).subscribe({
      next: (response) => { this.idempotency.set(response); this.busyIdempotency.set(false); },
      error: () => { this.error.set('The idempotency demo failed.'); this.busyIdempotency.set(false); },
    });
  }

  rpsWidth(rps: number): number {
    const max = Math.max(...this.loadResults.map((r) => r.rps));
    return Math.max(2, (rps / max) * 100);
  }

  p99Width(p99: number): number {
    const max = Math.max(...this.loadResults.map((r) => r.p99));
    return Math.max(2, (p99 / max) * 100);
  }
}

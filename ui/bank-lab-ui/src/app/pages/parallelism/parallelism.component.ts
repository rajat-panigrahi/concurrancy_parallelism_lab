import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LabApiService } from '../../core/lab-api.service';
import { FraudResponse, InterestResponse } from '../../core/models';

@Component({
  selector: 'app-parallelism',
  standalone: true,
  imports: [DecimalPipe, FormsModule],
  templateUrl: './parallelism.component.html',
  styleUrl: './parallelism.component.scss',
})
export class ParallelismComponent {
  private readonly api = inject(LabApiService);

  readonly interest = signal<InterestResponse | null>(null);
  readonly fraud = signal<FraudResponse | null>(null);
  readonly busyInterest = signal(false);
  readonly busyFraud = signal(false);
  readonly error = signal<string | null>(null);

  accountCount = 200_000;
  checks = 10;
  latencyMs = 200;
  maxConcurrency = 4;

  runInterest(): void {
    this.busyInterest.set(true);
    this.error.set(null);

    this.api.interest(this.accountCount).subscribe({
      next: (response) => { this.interest.set(response); this.busyInterest.set(false); },
      error: () => { this.error.set('The interest run failed.'); this.busyInterest.set(false); },
    });
  }

  runFraud(): void {
    this.busyFraud.set(true);
    this.error.set(null);

    this.api.fraud(this.checks, this.latencyMs, this.maxConcurrency).subscribe({
      next: (response) => { this.fraud.set(response); this.busyFraud.set(false); },
      error: () => { this.error.set('The fraud-check run failed.'); this.busyFraud.set(false); },
    });
  }

  /** Bar width relative to the slowest strategy, so the chart is readable. */
  interestBarWidth(ms: number): number {
    const timings = this.interest()?.timings ?? [];
    const slowest = Math.max(...timings.map((t) => t.bestMs), 1);
    return Math.max(1.5, (ms / slowest) * 100);
  }

  fraudBarWidth(ms: number): number {
    const timings = this.fraud()?.timings ?? [];
    const slowest = Math.max(...timings.map((t) => t.durationMs), 1);
    return Math.max(1.5, (ms / slowest) * 100);
  }

  speedupClass(speedup: number): string {
    if (speedup >= 1.5) return 'good';
    if (speedup >= 0.95) return 'mute';
    return 'bad';
  }
}

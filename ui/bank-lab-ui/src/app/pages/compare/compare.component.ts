import { Component, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { forkJoin } from 'rxjs';
import { LabApiService } from '../../core/lab-api.service';
import { RunSummary, StrategyInfo } from '../../core/models';

interface Row {
  info: StrategyInfo;
  summary: RunSummary;
}

@Component({
  selector: 'app-compare',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './compare.component.html',
  styleUrl: './compare.component.scss',
})
export class CompareComponent implements OnInit {
  private readonly api = inject(LabApiService);

  readonly strategies = signal<StrategyInfo[]>([]);
  readonly rows = signal<Row[]>([]);
  readonly running = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.strategies().subscribe({
      next: (strategies) => this.strategies.set(strategies),
      error: () => this.error.set('Could not reach the API.'),
    });
  }

  /** Runs every strategy against identical input, so the only variable is the handler. */
  runAll(): void {
    const strategies = this.strategies();
    if (strategies.length === 0) return;

    this.running.set(true);
    this.error.set(null);

    forkJoin(strategies.map((s) => this.api.startRun({
      strategy: s.name,
      actors: 5,
      amountEach: 100,
      startingBalance: 100,
      forceRace: true,
      thinkTimeMs: 0,
    }))).subscribe({
      next: (responses) => {
        this.rows.set(responses.map((response, i) => ({
          info: strategies[i],
          summary: response.summary,
        })));
        this.running.set(false);
      },
      error: () => {
        this.error.set('A run failed. Is the API (and PostgreSQL) running?');
        this.running.set(false);
      },
    });
  }

  /** What each strategy actually pays for correctness. */
  currency(row: Row): string {
    if (!row.summary.moneyIsConserved) return 'nothing — it is wrong';
    if (row.summary.conflicts > 0) return 'wasted work';
    if (row.summary.totalWaitedMs > 1) return 'waiting';
    return 'nothing measurable';
  }
}

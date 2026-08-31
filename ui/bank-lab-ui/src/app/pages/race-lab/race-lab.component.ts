import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { ContentionHubService } from '../../core/contention-hub.service';
import { LabApiService } from '../../core/lab-api.service';
import { ContentionEvent, ContentionPhase, RunSummary, StrategyInfo } from '../../core/models';

interface Lane {
  actorId: string;
  events: ContentionEvent[];
  outcome: 'won' | 'lost' | 'rejected';
  label: string;
}

interface Marker {
  event: ContentionEvent;
  leftPercent: number;
  colour: string;
  glyph: string;
  tooltip: string;
}

@Component({
  selector: 'app-race-lab',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './race-lab.component.html',
  styleUrl: './race-lab.component.scss',
})
export class RaceLabComponent implements OnInit {
  private readonly api = inject(LabApiService);
  private readonly hub = inject(ContentionHubService);

  /** Events pushed over SignalR while the run is still in flight. */
  readonly liveEvents = this.hub.liveEvents;
  readonly liveConnected = this.hub.connected;

  readonly strategies = signal<StrategyInfo[]>([]);
  readonly running = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<RunSummary | null>(null);
  readonly timeline = signal<ContentionEvent[]>([]);

  strategy = 'naive';
  actors = 5;
  amountEach = 100;
  startingBalance = 100;
  forceRace = true;
  thinkTimeMs = 0;

  readonly selected = computed(() =>
    this.strategies().find((s) => s.name === this.strategy));

  /** Longest elapsed time in the run — the x-axis scale. */
  private readonly span = computed(() => {
    const events = this.timeline();
    return events.length ? Math.max(...events.map((e) => e.elapsedMs), 0.001) : 1;
  });

  readonly lanes = computed<Lane[]>(() => {
    const summary = this.summary();
    const events = this.timeline();
    if (!summary) return [];

    return summary.outcomes.map((outcome) => ({
      actorId: outcome.actorId,
      events: events.filter((e) => e.actorId === outcome.actorId),
      outcome: outcome.actuallyLanded ? 'won' : outcome.toldItSucceeded ? 'lost' : 'rejected',
      label: outcome.actuallyLanded
        ? 'won'
        : outcome.toldItSucceeded
          ? 'told yes — overwritten'
          : 'refused',
    }));
  });

  markersFor(lane: Lane): Marker[] {
    const span = this.span();

    return lane.events.map((event) => ({
      event,
      leftPercent: Math.min(98, (event.elapsedMs / span) * 100),
      colour: this.phaseColour(event.phase),
      glyph: this.phaseGlyph(event.phase),
      tooltip: this.tooltipFor(event),
    }));
  }

  ngOnInit(): void {
    this.api.strategies().subscribe({
      next: (strategies) => this.strategies.set(strategies),
      error: () => this.error.set('Could not reach the API.'),
    });

    // Best effort. If the websocket never opens the lab still works, because the run
    // response carries the whole timeline — the live feed is an accelerator, not the
    // source of truth.
    void this.hub.connect(this.api.baseUrl).then(() => this.hub.watchAll());
  }

  run(): void {
    this.running.set(true);
    this.error.set(null);
    this.hub.clear();

    this.api.startRun({
      strategy: this.strategy,
      actors: this.actors,
      amountEach: this.amountEach,
      startingBalance: this.startingBalance,
      forceRace: this.forceRace,
      thinkTimeMs: this.thinkTimeMs,
    }).subscribe({
      next: (response) => {
        this.summary.set(response.summary);
        this.timeline.set(response.timeline);
        this.running.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.error ?? 'The run failed. Is the API (and PostgreSQL) running?');
        this.running.set(false);
      },
    });
  }

  private phaseColour(phase: ContentionPhase): string {
    switch (phase) {
      case 'Read': return 'var(--phase-read)';
      case 'LockWait':
      case 'Gate': return 'var(--phase-wait)';
      case 'Write':
      case 'LockAcquired': return 'var(--phase-write)';
      case 'Committed': return 'var(--phase-commit)';
      case 'Conflict':
      case 'Retry': return 'var(--phase-conflict)';
      case 'LostUpdate':
      case 'Failed': return 'var(--phase-lost)';
      default: return 'var(--dim)';
    }
  }

  private phaseGlyph(phase: ContentionPhase): string {
    switch (phase) {
      case 'Started': return '·';
      case 'Read': return 'R';
      case 'Gate': return '⏸';
      case 'LockWait': return '⋯';
      case 'LockAcquired': return '🔒';
      case 'Write': return 'W';
      case 'Committed': return '✓';
      case 'Conflict': return '✕';
      case 'Retry': return '↻';
      case 'Rejected': return '⊘';
      case 'Failed': return '!';
      default: return '•';
    }
  }

  private tooltipFor(event: ContentionEvent): string {
    const parts = [`${event.phase} @ ${event.elapsedMs.toFixed(2)}ms`];
    if (event.balanceSeen !== null) parts.push(`saw balance ${event.balanceSeen}`);
    if (event.versionSeen !== null) parts.push(`saw version ${event.versionSeen}`);
    if (event.versionWritten !== null) parts.push(`wrote version ${event.versionWritten}`);
    if (event.waitedMs !== null) parts.push(`waited ${event.waitedMs.toFixed(1)}ms`);
    if (event.note) parts.push(event.note);
    return parts.join('\n');
  }
}

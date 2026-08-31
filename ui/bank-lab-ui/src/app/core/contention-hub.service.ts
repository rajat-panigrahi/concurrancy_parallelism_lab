import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { ContentionEvent } from './models';

/**
 * Live feed of contention events for a run.
 *
 * Note this only works against a single instance: SignalR groups are per-process, so
 * behind a load balancer without a Redis backplane a client can silently miss events.
 * That is the same bug as the in-memory lock the lab demonstrates — see ADR-0009. The
 * REST timeline stays the source of truth, and the Race Lab falls back to it.
 */
@Injectable({ providedIn: 'root' })
export class ContentionHubService {
  private connection?: signalR.HubConnection;

  readonly connected = signal(false);
  readonly liveEvents = signal<ContentionEvent[]>([]);

  async connect(baseUrl: string): Promise<void> {
    if (this.connection) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/contention`)
      .withAutomaticReconnect()
      .build();

    this.connection.on('contention', (event: ContentionEvent) => {
      this.liveEvents.update((events) => [...events, event]);
    });

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    try {
      await this.connection.start();
      this.connected.set(true);
    } catch {
      // The lab still works without live streaming — the run result carries the full
      // timeline. A demo that dies because a websocket didn't open is a bad demo.
      this.connected.set(false);
      this.connection = undefined;
    }
  }

  /**
   * Subscribes to every run's events.
   *
   * A client cannot join a specific run's group before the run exists — the server
   * generates the id — so the live ticker watches everything and the page filters.
   */
  async watchAll(): Promise<void> {
    await this.connection?.invoke('WatchAllRuns').catch(() => undefined);
  }

  clear(): void {
    this.liveEvents.set([]);
  }
}

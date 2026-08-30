import { Component, OnDestroy, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent implements OnDestroy {
  /** Drives both animations from one clock, so the two halves stay comparable. */
  readonly tick = signal(0);
  readonly playing = signal(true);

  private timer = setInterval(() => {
    if (this.playing()) {
      this.tick.update((t) => (t + 1) % 40);
    }
  }, 130);

  readonly customers = [0, 1, 2, 3, 4];
  readonly counters = [0, 1, 2, 3];

  /** Concurrency: one teller, serving whichever customer is "active" this tick. */
  activeCustomer(): number {
    return this.tick() % this.customers.length;
  }

  /** Parallelism: every counter busy at once. */
  counterProgress(counter: number): number {
    return ((this.tick() * 2.5 + counter * 3) % 100);
  }

  toggle(): void {
    this.playing.update((p) => !p);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}

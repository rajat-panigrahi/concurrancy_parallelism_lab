import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LabApiService } from './core/lab-api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  private readonly api = inject(LabApiService);

  readonly online = signal<boolean | null>(null);
  readonly instanceId = signal<string>('');
  readonly cores = signal<number>(0);

  readonly links = [
    { path: '/', label: 'Mental model', exact: true },
    { path: '/race', label: 'Race lab', exact: false },
    { path: '/compare', label: 'Compare', exact: false },
    { path: '/parallelism', label: 'Parallelism', exact: false },
    { path: '/scale', label: 'Scale', exact: false },
    { path: '/learn', label: 'Learn', exact: false },
  ];

  ngOnInit(): void {
    this.api.health().subscribe({
      next: (health) => {
        this.online.set(true);
        this.instanceId.set(health.instanceId);
        this.cores.set(health.processorCount);
      },
      error: () => this.online.set(false),
    });
  }
}

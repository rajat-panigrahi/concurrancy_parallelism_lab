import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/home/home.component').then((m) => m.HomeComponent),
    title: 'Mental model · Concurrency Lab',
  },
  {
    path: 'race',
    loadComponent: () => import('./pages/race-lab/race-lab.component').then((m) => m.RaceLabComponent),
    title: 'Race lab · Concurrency Lab',
  },
  {
    path: 'compare',
    loadComponent: () => import('./pages/compare/compare.component').then((m) => m.CompareComponent),
    title: 'Strategy comparison · Concurrency Lab',
  },
  {
    path: 'parallelism',
    loadComponent: () => import('./pages/parallelism/parallelism.component').then((m) => m.ParallelismComponent),
    title: 'Parallelism lab · Concurrency Lab',
  },
  {
    path: 'scale',
    loadComponent: () => import('./pages/scale/scale.component').then((m) => m.ScaleComponent),
    title: 'Scale lab · Concurrency Lab',
  },
  {
    path: 'learn',
    loadComponent: () => import('./pages/learn/learn.component').then((m) => m.LearnComponent),
    title: 'Learn · Concurrency Lab',
  },
  { path: '**', redirectTo: '' },
];

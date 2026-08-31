import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  DeadlockResponse, FraudResponse, IdempotencyResponse, InterestResponse,
  StartRunRequest, StartRunResponse, StrategyInfo,
} from './models';

/** Thin HTTP layer. The UI holds no business logic — every verdict is computed server-side. */
@Injectable({ providedIn: 'root' })
export class LabApiService {
  private readonly http = inject(HttpClient);
  readonly baseUrl = 'http://localhost:5080';

  strategies(): Observable<StrategyInfo[]> {
    return this.http.get<StrategyInfo[]>(`${this.baseUrl}/api/lab/strategies`);
  }

  startRun(request: StartRunRequest): Observable<StartRunResponse> {
    return this.http.post<StartRunResponse>(`${this.baseUrl}/api/lab/runs`, request);
  }

  interest(accountCount: number, repeats = 3): Observable<InterestResponse> {
    return this.http.post<InterestResponse>(`${this.baseUrl}/api/lab/interest`, { accountCount, repeats });
  }

  fraud(checks: number, latencyMs: number, maxConcurrency: number): Observable<FraudResponse> {
    return this.http.post<FraudResponse>(`${this.baseUrl}/api/lab/fraud`, { checks, latencyMs, maxConcurrency });
  }

  deadlock(orderLocks: boolean, holdBetweenLocksMs = 100): Observable<{ response: DeadlockResponse }> {
    return this.http.post<{ response: DeadlockResponse }>(
      `${this.baseUrl}/api/lab/deadlock`, { orderLocks, amount: 50, holdBetweenLocksMs });
  }

  idempotency(useIdempotencyKey: boolean, retries = 5): Observable<IdempotencyResponse> {
    return this.http.post<IdempotencyResponse>(`${this.baseUrl}/api/lab/idempotency`, {
      retries, amount: 100, startingBalance: 1000, useIdempotencyKey,
    });
  }

  health(): Observable<{ instanceId: string; processorCount: number }> {
    return this.http.get<{ instanceId: string; processorCount: number }>(`${this.baseUrl}/api/health`);
  }
}

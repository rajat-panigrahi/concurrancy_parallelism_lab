export type ContentionPhase =
  | 'Started' | 'Read' | 'Gate' | 'LockWait' | 'LockAcquired' | 'Write'
  | 'Committed' | 'Conflict' | 'Retry' | 'Won' | 'LostUpdate' | 'Rejected' | 'Failed';

export interface ContentionEvent {
  runId: string;
  actorId: string;
  accountId: string;
  sequence: number;
  phase: ContentionPhase;
  elapsedMs: number;
  attempt: number;
  balanceSeen: number | null;
  versionSeen: number | null;
  versionWritten: number | null;
  amount: number | null;
  waitedMs: number | null;
  note: string | null;
}

export interface ActorOutcome {
  actorId: string;
  finalPhase: ContentionPhase;
  toldItSucceeded: boolean;
  /** Whether this actor's money movement is reflected in the final balance. */
  actuallyLanded: boolean;
  amount: number;
  attempts: number;
  waitedMs: number;
  durationMs: number;
  note: string | null;
}

export interface RunSummary {
  runId: string;
  strategy: string;
  accountId: string;
  startingBalance: number;
  finalBalance: number;
  expectedBalance: number;
  discrepancy: number;
  moneyIsConserved: boolean;
  overdrawnBeyondLimit: boolean;
  outcomes: ActorOutcome[];
  winners: ActorOutcome[];
  /** Told "approved", then overwritten. The number to point at in an interview. */
  silentLosers: ActorOutcome[];
  rejected: ActorOutcome[];
  totalAttempts: number;
  conflicts: number;
  totalWaitedMs: number;
  durationMs: number;
  verdict: string;
}

export interface StartRunRequest {
  strategy: string;
  actors: number;
  amountEach: number;
  startingBalance: number;
  forceRace: boolean;
  thinkTimeMs: number;
}

export interface StartRunResponse {
  runId: string;
  summary: RunSummary;
  timeline: ContentionEvent[];
}

export interface StrategyInfo {
  name: string;
  storage: string;
  summary: string;
  scalesAcrossInstances: boolean;
}

export interface StrategyTiming {
  name: string;
  aggregation: string;
  bestMs: number;
  total: number;
  speedupVsSequential: number;
  note: string;
}

export interface InterestResponse {
  accountCount: number;
  processorCount: number;
  timings: StrategyTiming[];
  verdict: string;
}

export interface FraudTiming {
  name: string;
  durationMs: number;
  distinctThreads: number;
  note: string;
}

export interface FraudResponse {
  checks: number;
  latencyMs: number;
  processorCount: number;
  timings: FraudTiming[];
  verdict: string;
}

export interface TransferResult {
  actorId: string;
  succeeded: boolean;
  deadlocked: boolean;
  reason: string;
}

export interface DeadlockResponse {
  runId: string;
  orderLocks: boolean;
  deadlockDetected: boolean;
  results: TransferResult[];
  totalMoneyBefore: number;
  totalMoneyAfter: number;
  verdict: string;
}

export interface IdempotencyResponse {
  useIdempotencyKey: boolean;
  retries: number;
  startingBalance: number;
  finalBalance: number;
  expectedBalance: number;
  timesApplied: number;
  verdict: string;
}

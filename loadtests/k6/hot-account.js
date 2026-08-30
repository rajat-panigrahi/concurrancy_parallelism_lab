import http from 'k6/http';
import { check } from 'k6';

// Layer 3 of the scaling story: once instances are cheap, the DATABASE is the wall —
// and specifically row contention, not CPU.
//
// Both scenarios offer the same load and do the same work. One aims every withdrawal at
// a single account; the other spreads across many. Same RPS offered, very different RPS
// achieved — because a hot row serialises no matter how many replicas you run.
//
// Against deploy/docker-compose.scale.yml this is also the script that exposes the
// in-process lock: requests go through nginx and land on different replicas, each
// holding its own unrelated semaphore. Run it with strategy 'lock' and watch money
// disappear; run it with 'optimistic' or 'distributed-lock' and it does not.

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';
const STRATEGY = __ENV.STRATEGY || 'optimistic';

export const options = {
  scenarios: {
    hot_single_account: {
      executor: 'constant-vus',
      vus: 50,
      duration: '20s',
      exec: 'hotAccount',
    },
    spread_across_accounts: {
      executor: 'constant-vus',
      vus: 50,
      duration: '20s',
      startTime: '25s',
      exec: 'spreadAcross',
    },
  },
};

function startRun(actors, startingBalance) {
  const response = http.post(
    `${BASE_URL}/api/lab/runs`,
    JSON.stringify({
      strategy: STRATEGY,
      actors: actors,
      amountEach: 1,
      startingBalance: startingBalance,
      forceRace: false,
    }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  check(response, {
    'run started': (r) => r.status === 200,
    // The invariant that must hold no matter how many instances are running. If this
    // fails, the strategy does not survive scale-out.
    'money conserved': (r) => r.status === 200 && r.json('summary.moneyIsConserved') === true,
  });

  return response;
}

// Maximum contention: many actors, one account.
export function hotAccount() {
  startRun(16, 100000);
}

// Minimal contention: the same total work, one actor per run.
export function spreadAcross() {
  startRun(1, 100000);
}

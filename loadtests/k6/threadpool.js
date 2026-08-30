import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

// Same work, same hardware, two ways of waiting.
//
// The async endpoint releases its thread while waiting. The sync-over-async one blocks
// a thread-pool thread on .Result. Under concurrency the pool runs out, and because it
// only grows by a thread or two per second, latency does not degrade gracefully — it
// falls off a cliff.
//
// Measured in the repo's own harness on a 4-core VM at 100 virtual users:
//   async            979.0 RPS   p99   105ms
//   sync-over-async   35.2 RPS   p99  3531ms
// 27.8x the throughput, 33.5x the p99. No infrastructure involved.

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5080';

const asyncLatency = new Trend('latency_async', true);
const syncLatency = new Trend('latency_sync_over_async', true);

export const options = {
  scenarios: {
    proper_async: {
      executor: 'constant-vus',
      vus: 100,
      duration: '20s',
      exec: 'properAsync',
      tags: { mode: 'async' },
    },
    sync_over_async: {
      executor: 'constant-vus',
      vus: 100,
      duration: '20s',
      startTime: '25s', // let the thread pool settle before the second scenario
      exec: 'syncOverAsync',
      tags: { mode: 'sync-over-async' },
    },
  },
  thresholds: {
    // The async endpoint should stay close to the 100ms it actually waits for.
    'latency_async': ['p(99)<400'],
  },
};

export function properAsync() {
  const response = http.get(`${BASE_URL}/api/scale/async?delayMs=100`);
  asyncLatency.add(response.timings.duration);
  check(response, { 'async 200': (r) => r.status === 200 });
}

export function syncOverAsync() {
  const response = http.get(`${BASE_URL}/api/scale/sync-over-async?delayMs=100`);
  syncLatency.add(response.timings.duration);
  check(response, { 'sync 200': (r) => r.status === 200 });
}

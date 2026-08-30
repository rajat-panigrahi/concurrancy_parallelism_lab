# k6 scenarios

[k6](https://k6.io) is the industry-standard load tool. Its binary could not be
downloaded in the environment this repo was built in, so these scripts are committed for
you to run locally. The in-repo harness (`tests/Bank.LoadTests`) covers the same ground
with no install.

```bash
# 1. thread-pool starvation — async vs sync-over-async
k6 run loadtests/k6/threadpool.js

# 2. contention, not CPU, is the wall — one hot account vs spread across many
k6 run loadtests/k6/hot-account.js

# point at a different target
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/threadpool.js
```

Against `deploy/docker-compose.scale.yml`, `hot-account.js` is what makes the in-process
lock visibly fail: it drives concurrent requests through nginx, so they land on
different replicas holding different semaphores.

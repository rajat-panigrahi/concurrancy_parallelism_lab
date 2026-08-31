# ADR-0014 — Docker Compose replicas; Kubernetes deliberately excluded

- **Status:** Accepted
- **Date:** 2026-08-30

## Context

"Can your app scale?" is one of the questions this repo exists to answer, and the
decisive demonstration is running the same code at more than one instance to watch which
strategies silently break.

That demo needs: several instances of the API, one shared database, and a load balancer
in front. It does **not** obviously need an orchestrator — and since the topic is
routinely conflated with Kubernetes, the choice is itself part of the lesson.

## Options considered

- **Kubernetes** (kind/minikube locally) — what people mean when they say "scale". Real
  deployments, services, replica sets, HPA.
- **Docker Compose with three named API services behind nginx.**
- **Multiple processes on different ports**, no containers, with a local reverse proxy.

## Decision

Docker Compose: `deploy/docker-compose.yml` (one instance) and
`deploy/docker-compose.scale.yml` (three replicas + nginx).

## Why not the others

- **Kubernetes adds an enormous amount of operational surface and not one concurrency
  insight.** The lesson is that *process-local state does not survive multiple
  instances*. Compose demonstrates that in about 30 seconds with a file anyone can read
  end to end. Kubernetes would demonstrate exactly the same thing, after manifests, a
  local cluster, an ingress controller and an image registry — and a reader debugging
  their cluster is not learning about race conditions. It would also actively mislead by
  implying that scaling is a Kubernetes topic, when the whole point of lesson 09 is that
  the decisive work happens in the application code long before an orchestrator is
  involved.
- **Bare processes on different ports** would work and is even simpler, but it drifts too
  far from how anyone actually deploys, and it makes the "one shared database, N
  stateless instances" topology harder to see at a glance. Compose expresses that
  topology as data.

## Trade-offs accepted

- **Compose is not production.** No rolling deploys, no health-based routing, no
  autoscaling, no self-healing. A reader who only ever sees this will not know how a real
  deployment works.
- **The demo doesn't run in this build environment.** There is no Docker daemon here
  (ADR-0015), so the compose files are validated with `docker compose config` but the
  three-replica run happens on the reader's machine. Everything else in this repo was
  executed and measured; this one thing is verified only structurally, and that is stated
  rather than glossed.
- **Round-robin nginx is not sticky**, which breaks SignalR across replicas (ADR-0009).
  Rather than fix it with sticky sessions, the config documents it — because it is the
  same lesson as the in-memory lock, and finding it yourself is more instructive than
  having it silently handled.
- **Three replicas triple the load on one database**, which the compose file does not
  address. That's layer 3 of lesson 09, and pretending otherwise would undercut it.

## Consequences

- `INSTANCE_ID` is injected per service and returned by `/api/health` and the scale
  endpoints, so you can see which replica served each request.
- The scale compose file carries a comment table of which strategies break at three
  replicas and why, so the file itself teaches.
- `loadtests/k6/hot-account.js` is the script that exposes the in-process lock, because
  it drives concurrent requests *through nginx* — the lab's own runner drives all actors
  from within one instance and therefore cannot show the failure.
- Lesson 09 can make the claim "Kubernetes multiplies instances; it doesn't make
  un-scalable code scalable" and back it with a runnable demonstration rather than an
  assertion.

## Interview angle

**The 60-second version:** "I used Compose with three replicas behind nginx, because the
thing I wanted to show — that an in-memory lock silently stops working at more than one
instance — needs multiple instances and nothing else. Kubernetes would have added a lot
of surface and no additional insight. Scaling out is an application-design property
first: if the code keeps state in process memory, an orchestrator will just run three
copies of the bug."

**What a good interviewer asks next:** *"So when would you actually want Kubernetes?"*
When you need the *operational* properties, not the performance ones — rolling deploys,
self-healing, autoscaling on real signals, service discovery, and a consistent deployment
model across many services. It's an operations decision. If the answer to "why K8s" is
"to make it fast", that's the wrong reason, and lesson 09's 27.8× throughput difference
from a one-line code change is the evidence.

# KEDA Queue Autoscaling Demo

Event-driven batch processing on Kubernetes that scales **0 → N → 0** based on queue depth, built with .NET, Azure Storage Queue, KEDA, Helm, and GitHub Actions.

A producer accepts HTTP requests and drops messages onto a queue. A worker consumes them, persists each to PostgreSQL, and **KEDA** scales the worker up and down according to how many messages are waiting — including all the way down to zero when the queue is empty.

[![CI](https://github.com/necamat/keda-servicebus-demo/actions/workflows/ci.yml/badge.svg)](https://github.com/necamat/keda-servicebus-demo/actions/workflows/ci.yml)
[![CD](https://github.com/necamat/keda-servicebus-demo/actions/workflows/cd.yml/badge.svg)](https://github.com/necamat/keda-servicebus-demo/actions/workflows/cd.yml)

---

## Architecture

```
        HTTP POST /send
              │
              ▼
      ┌───────────────┐        enqueue        ┌──────────────────┐
      │   Producer    │ ────────────────────► │  Storage Queue   │
      │ (Minimal API) │                       │    (Azurite)     │
      └───────────────┘                       └──────────────────┘
                                                    │         ▲
                                       queue depth  │         │ dequeue
                                       (polled)     ▼         │
                                       ┌──────────────────┐   │
                                       │      KEDA        │   │
                                       │  ScaledObject    │   │
                                       └──────────────────┘   │
                                                 │ scales     │
                                                 ▼            │
                                       ┌──────────────────┐   │
                                       │    Consumer      │ ──┘
                                       │ (Worker Service) │
                                       │   0 … 5 replicas │
                                       └──────────────────┘
                                                 │ persist
                                                 ▼
                                       ┌──────────────────┐
                                       │   PostgreSQL     │
                                       └──────────────────┘
```

Everything runs inside Kubernetes (local: minikube), deployed via a single Helm chart.

---

## Tech stack

| Area              | Technology                                           |
|-------------------|------------------------------------------------------|
| Language / runtime| C# / .NET 10                                         |
| Producer          | ASP.NET Core Minimal API                             |
| Consumer          | .NET Worker Service (`BackgroundService`)            |
| Messaging         | Azure Storage Queue (emulated locally with Azurite)  |
| Persistence       | PostgreSQL + Entity Framework Core                   |
| Autoscaling       | KEDA (`azure-queue` trigger, scale-to-zero)          |
| Packaging         | Docker (multi-stage builds)                          |
| Orchestration     | Kubernetes + Helm                                    |
| Registry          | GitHub Container Registry (ghcr.io)                  |
| CI / CD           | GitHub Actions (build + test, build + push images)   |

---

## How it works

1. A client sends `POST /send` with a JSON payload to the **Producer**.
2. The Producer serialises the payload and enqueues it on the Storage Queue.
3. **KEDA** polls the queue length. When messages pile up, it scales the **Consumer** deployment out (up to 5 replicas); when the queue drains, it scales back in — down to **zero** replicas.
4. Each Consumer replica dequeues messages, writes a `ProcessedMessage` row to **PostgreSQL**, and deletes the message from the queue.

![KEDA autoscaling demo](docs/scaling-demo.gif)

> Consumer scales from 0 → 5 replicas as messages accumulate, then back to 0 once the queue drains.

---

## Design notes & lessons learned

The interesting part of this project wasn't the happy path — it was making KEDA, Azurite, and Kubernetes networking cooperate. Three problems were worth documenting:

## Trade-offs and what I'd do differently in production

This project is intentionally scoped as a local demo. A few conscious trade-offs worth naming:

**Azurite instead of real Azure Storage Queue.**
Azurite keeps the project free and self-contained. In production, Azure Storage Queue would be replaced with a real namespace and KEDA would authenticate via Workload Identity (pod-level Azure AD identity) instead of a connection string with an account key.

**DB migration at application startup.**
The Consumer runs `MigrateAsync()` on startup. This is convenient for a demo but problematic in production — multiple replicas racing to migrate the same schema. The right approach is a dedicated Kubernetes `Job` that runs migrations once before the Consumer deployment rolls out.

**No retry or dead-letter handling.**
If message processing fails, the message becomes visible again after its visibility timeout expires and will be retried. There is no dead-letter queue, no max delivery count check, and no poison-message handling. For production, these would be essential.

**Connection string in Helm values.**
The Azurite account key is the publicly documented emulator key — safe to commit. Real credentials should never appear in a values file; they would come from a sealed secret (e.g. Sealed Secrets or External Secrets Operator) injected at deploy time.

**Local cluster only.**
The CD pipeline builds and publishes images to ghcr.io but does not deploy to the cluster — minikube runs on a local machine with no inbound access from GitHub Actions. In a cloud environment (AKS, EKS, GKE), the CD job would run `helm upgrade` after pushing the image.

### 1. Path-style vs. subdomain-style storage addressing
Azure Storage resolves the account name either from the host (`account.queue.core.windows.net`, *subdomain-style*) or from the URL path (`http://host/devstoreaccount1/...`, *path-style*). When the queue endpoint host contains dots, Azurite tries subdomain-style and reads the first label as the account name — which no longer matches the account the SharedKey signature was built for, producing `400 Bad Request`. Using a **single-label host** keeps Azurite on path-style addressing, which is what the emulator credentials expect.

### 2. Cross-namespace service discovery
The application pods run in the `default` namespace, but the KEDA operator runs in the `keda` namespace. A bare service name like `azurite` only resolves **within the same namespace**, so KEDA couldn't reach the queue.

### 3. Reconciling (1) and (2)
These two requirements conflict: the apps need the **short** host name (for path-style addressing), while KEDA needs to resolve the service **across namespaces**. The fix is an `ExternalName` service named `azurite` in the `keda` namespace that points to `azurite.default.svc.cluster.local`. KEDA keeps using the short host `azurite` (so Azurite stays in path-style mode), while DNS quietly redirects the request into the `default` namespace. One small object resolves both constraints with a single, shared connection string.

---

## Running locally

### Prerequisites
- Docker
- minikube
- kubectl
- Helm

### 1. Start the cluster and install KEDA
```bash
minikube start --driver=docker
helm repo add kedacore https://kedacore.github.io/charts
helm repo update
helm install keda kedacore/keda --namespace keda --create-namespace
```

### 2. Deploy the app
```bash
helm install keda-demo helm/app
```
This deploys the Producer, Consumer, Azurite, PostgreSQL, the KEDA `ScaledObject`, and the cross-namespace `ExternalName` service. Images are pulled from ghcr.io.

### 3. Send messages
```bash
kubectl port-forward service/producer 8080:80
```
In another terminal:
```bash
curl -X POST http://localhost:8080/send \
  -H "Content-Type: application/json" \
  -d '{"id":"001","data":"hello","createdAt":"2026-01-01T00:00:00Z"}'
```

### 4. Watch it scale
```bash
kubectl get pods -w
```
Send a burst of messages and watch the Consumer deployment scale out, then back down to zero once the queue drains:
```bash
for i in $(seq 1 100); do
  curl -s -X POST http://localhost:8080/send \
    -H "Content-Type: application/json" \
    -d "{\"id\":\"$i\",\"data\":\"message $i\",\"createdAt\":\"2026-01-01T00:00:00Z\"}"
done
```

---

## Repository layout

```
.
├── src/
│   ├── Producer/      # Minimal API — enqueues messages
│   └── Consumer/      # Worker Service — dequeues, persists to PostgreSQL
├── tests/             # xUnit unit tests
├── helm/app/          # Helm chart (deployments, KEDA ScaledObject, services)
└── .github/workflows/ # CI (build + test) and CD (build + push to ghcr.io)
```

---

## CI/CD

- **CI** (`ci.yml`) runs on every push and pull request to `main`: restores, builds in Release, and runs the test suite.
- **CD** (`cd.yml`) runs on every push to `main`: builds the Producer and Consumer images and publishes them to ghcr.io.

Deployment to the cluster is done locally with Helm — the images are pulled from the public registry. Pushing to a self-hosted minikube from CI isn't possible (no inbound access), which is the expected pattern for a local cluster.
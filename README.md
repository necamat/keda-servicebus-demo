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
      │ (Minimal API) │                        │    (Azurite)     │
      └───────────────┘                        └──────────────────┘
                                                    │        ▲
                                       queue depth  │        │ dequeue
                                       (polled)     ▼        │
                                       ┌──────────────────┐  │
                                       │      KEDA        │  │
                                       │  ScaledObject    │  │
                                       └──────────────────┘  │
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

The scaling threshold (`queueLength`) and bounds (`minReplicaCount: 0`, `maxReplicaCount: 5`) live in the KEDA `ScaledObject`.

---

## Design notes & lessons learned

The interesting part of this project wasn't the happy path — it was making KEDA, Azurite, and Kubernetes networking cooperate. Three problems were worth documenting:

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
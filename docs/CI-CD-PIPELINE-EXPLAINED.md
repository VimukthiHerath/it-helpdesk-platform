# CI/CD Pipeline — Explained

A complete walkthrough of how this repo builds, tests, and deploys itself: the
GitHub Actions pipelines, the Dockerfiles, docker-compose, and the Azure
infrastructure they deploy to. Written for a viva — it explains *what* each
piece does and *why* it's built that way, plus the honest gaps and what
should come next.

---

## 1. The big picture

This is a **monorepo** containing five independent .NET 8 microservices
(Auth, Ticket, Assignment, Notification, SLA) and one React frontend, each
with its **own CI/CD pipeline**. There is no single "build everything"
pipeline — instead, six separate GitHub Actions workflows live in
`.github/workflows/`, one per service:

```
.github/workflows/
├── auth-ci.yml
├── ticket-ci.yml
├── assignment-ci.yml
├── notification-ci.yml
├── sla-ci.yml
└── frontend-ci.yml
```

Each workflow is **path-filtered** — it only runs when files under its own
service's folder change (e.g. `auth-ci.yml` triggers only on changes under
`services/auth/**`). This is the core architectural decision of the whole
CI/CD setup:

> **Changing the Ticket service should not rebuild, retest, or redeploy
> Auth, SLA, Notification, Assignment, or the frontend.**

That's the main benefit of splitting pipelines per-service in a monorepo:
faster feedback (only what changed gets built) and independent deployability
(one service's bad build doesn't block another's release).

---

## 2. Anatomy of one pipeline (they're all the same shape)

Every backend workflow (`auth-ci.yml`, `ticket-ci.yml`, `assignment-ci.yml`,
`notification-ci.yml`, `sla-ci.yml`) follows an identical template — only
the service name, folder path, and image name differ. Using `auth-ci.yml`
as the reference:

```yaml
on:
  push:
    branches: [main, develop]
    paths:
      - 'services/auth/**'
  pull_request:
    branches: [main, develop]
    paths:
      - 'services/auth/**'
```

**Triggers:** runs on a `push` *or* a `pull_request` targeting `main` or
`develop`, but only if the diff touches `services/auth/**`. This means:
- Opening a PR into `develop` runs the build/test steps (a status check),
  but **does not deploy anything** — good, you don't want a PR to touch
  production/shared infra before it's even reviewed.
- Pushing directly to `develop` (which is what merging a PR does) runs the
  same steps **and** deploys.

### The job itself

```yaml
permissions:
  id-token: write
  contents: read

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: services/auth/Auth.Api
```

`id-token: write` is required for **OIDC federated login to Azure** (see
step 5) — GitHub issues a short-lived OIDC token that Azure trusts, instead
of the pipeline holding a long-lived Azure password/secret. This is the
modern, more secure way to authenticate CI to a cloud provider.

The job then runs these steps, in order:

| # | Step | What it does | Always runs? |
|---|---|---|---|
| 1 | Checkout code | `actions/checkout@v4` — clones the repo into the runner | Yes |
| 2 | Setup .NET 8 | `actions/setup-dotnet@v4` pins the SDK to `8.0.x` | Yes |
| 3 | Restore | `dotnet restore` — pulls NuGet packages | Yes |
| 4 | Build | `dotnet build --no-restore --configuration Release` | Yes |
| 5 | Build Docker image | `docker build -t ithelpdeskacr01.azurecr.io/auth-service:${{ github.sha }} .` | Yes |
| 6 | Azure login | `azure/login@v2`, OIDC via `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` secrets | **Only** on push to `develop` |
| 7 | Push image to ACR | `az acr login` + `docker push` | **Only** on push to `develop` |
| 8 | Deploy | `az containerapp update --image ...` | **Only** on push to `develop` |

Steps 6–8 are gated by:

```yaml
if: github.event_name == 'push' && github.ref == 'refs/heads/develop'
```

So a PR build proves the code compiles and the image builds — it never
touches Azure. Only a push straight to `develop` (i.e. a merged PR) goes
all the way to deployment. **`main` never auto-deploys** — see the gaps
section below, this is a real limitation, not an intentional production
gate.

### Image tagging: `github.sha`, not `latest`

Every image is tagged with the **commit SHA** that built it
(`auth-service:a40ddbe...`), never `latest`. This matters for a viva
answer:
- **Traceability** — you can always look at a running container's image
  tag and know exactly which commit is live.
- **Immutability** — the same tag always refers to the same build; nothing
  can silently change under you.
- **Rollback** — you can `az containerapp update --image ...` back to any
  previous commit's tag directly, no rebuild needed.

### The frontend pipeline

`frontend-ci.yml` follows the exact same shape, swapping the .NET steps for
Node:

```yaml
- uses: actions/setup-node@v4
  with:
    node-version: '20'
- run: npm ci
- run: npm run build
```

Then it builds/pushes/deploys the same way, to a container app named
`it-helpdesk-frontend` (note: the *image* is called `frontend`, but the
*Container App* is `it-helpdesk-frontend` — a naming inconsistency worth
knowing about, not a bug, just a detail if you get asked "why don't the
names match").

---

## 3. The Dockerfiles

### Backend services (identical multi-stage pattern, 5x)

```dockerfile
# Stage 1: build the app using the full SDK (has compilers, tools, everything)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: run the app using only the lightweight runtime (no build tools)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Auth.Api.dll"]
```

This is a **multi-stage build**, and it's a standard, important DevOps
pattern to be able to explain:

- **Stage 1 (`build`)** uses the full SDK image (~800MB+) — it has the
  compiler, MSBuild, everything needed to `dotnet publish`.
- **Stage 2 (`final`)** starts from a *different*, much smaller base image
  — the ASP.NET **runtime-only** image — and copies in just the published
  output (`COPY --from=build`). The SDK, source code, and intermediate
  build artifacts never make it into the final image.

**Why this matters:**
1. **Smaller final image** → faster pulls/deploys, smaller attack surface.
2. **No build tooling in production** → nothing an attacker could use to
   recompile/tamper with the app inside a compromised container.
3. **Reproducibility** → the build environment is pinned by image tag
   (`sdk:8.0`), not whatever happens to be on a dev machine.

`EXPOSE 8080` and `ENTRYPOINT ["dotnet", "X.Api.dll"]` — every backend
service listens on 8080 inside its container; Azure Container Apps handles
external HTTPS termination and maps ingress traffic to that port.

Each service's Dockerfile is byte-identical to Auth's except for the
`.dll` name in `ENTRYPOINT` — another sign of the deliberate
per-service-but-copy-pasted approach used everywhere in this repo (see
"Duplication" in the future-improvements section).

### Frontend (also multi-stage, different tech)

```dockerfile
FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/build /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

Same idea: **Stage 1** (`node:20-alpine`) installs dependencies and runs
the React production build (`npm run build`, via Create React App/
react-scripts) — this produces static HTML/CSS/JS. **Stage 2** throws away
Node entirely and serves the static output with **nginx**, a tiny,
purpose-built web server. The final image doesn't contain Node, npm, or
any of the ~200MB+ of `node_modules` — just static files and nginx.

`nginx.conf`:

```nginx
server {
    listen 80;
    location / {
        root /usr/share/nginx/html;
        index index.html;
        try_files $uri /index.html;
    }
}
```

`try_files $uri /index.html` is the **SPA fallback rule** — it's what
makes client-side routing (React Router) work. Without it, refreshing the
browser on a route like `/my-tickets` would 404 at the nginx level, because
there's no real file called `my-tickets` on disk — only `index.html` exists,
and React Router reads the URL client-side after that loads. This rule says
"if the requested file doesn't exist, serve `index.html` instead and let
the JS router figure out what to render."

### `.dockerignore`

Each backend service has a `.dockerignore` excluding `bin/` and `obj/`
(local build output — including it would bloat the image and risk stale
binaries leaking into the container). The frontend's excludes `node_modules`
and `build` for the same reason.

---

## 4. `docker-compose.yml` — local development only

```yaml
services:
  kafka:
    image: apache/kafka:3.8.0
    container_name: local-kafka
    ports: ["9092:9092"]
    environment:
      KAFKA_PROCESS_ROLES: broker,controller
      ...
  mysql:
    image: mysql:8.0
    container_name: local-mysql
    ports: ["3307:3306"]
    environment:
      MYSQL_ROOT_PASSWORD: localdevpassword
```

This is important to get right in a viva: **this file does not run the
application services** (no Auth, Ticket, Assignment, etc. containers in
here). It only stands up the two pieces of **shared infrastructure** a
developer needs running locally to develop against:

- **Kafka**, in **KRaft mode** (`broker,controller` combined roles) — no
  ZooKeeper needed, a single container acts as its own metadata quorum.
  `KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"` means topics get created on
  first use rather than needing manual provisioning locally.
- **MySQL 8**, exposed on host port **3307** (not the default 3306) —
  deliberately remapped to avoid clashing with a MySQL instance a developer
  might already have running locally on 3306.

Each of the five .NET services is run individually on the developer's
machine (`dotnet run`), pointed at `localhost:3307` and `localhost:9092`
via their `appsettings.json`. The frontend is run with `npm start` and
talks to whichever backend URLs are in `frontend/.env.development`
(`http://localhost:5121` for Auth, `http://localhost:5164` for Ticket).

**This is a deliberate, common pattern**: docker-compose owns
infrastructure dependencies (databases, message brokers) that are
annoying to install natively and identical everywhere; the actual
application code under active development runs natively for the fastest
possible edit-rebuild-test loop (no image rebuild needed on every code
change).

---

## 5. Azure infrastructure — what the pipelines actually deploy to

| Piece | Name / detail |
|---|---|
| Resource group | `it-helpdesk-rg` |
| Container registry | `ithelpdeskacr01.azurecr.io` (Azure Container Registry) |
| Compute platform | **Azure Container Apps** (serverless containers, not raw VMs/AKS) |
| Region | Southeast Asia |
| Container Apps | `auth-service`, `ticket-service`, `assignment-service`, `sla-service`, `notification-service`, `it-helpdesk-frontend`, plus `kafka-broker` (a **cloud** Kafka broker, separate from the local docker-compose one) |
| Auth to Azure | OIDC federated identity (`azure/login@v2`) — no stored client secret |
| ACR pull auth | A user-assigned managed identity (`acr-pull-identity`) attached to each Container App — the app itself authenticates to ACR via Azure identity, not a username/password |
| Revision mode | `Single` — only one revision is "active" and serving traffic at a time; every deploy replaces it |

**Why Container Apps instead of, say, AKS or plain VMs?** It's built for
exactly this shape of workload — small, independently-deployable
HTTP services — without needing to run or patch a Kubernetes control
plane yourself. It gives you scale-to-zero, per-app ingress, and revision
management out of the box.

### Local Kafka vs. cloud Kafka — don't mix these up

There are **two separate Kafka brokers** in this project and it's easy to
conflate them:
1. **`local-kafka`** — the docker-compose container, for a developer's own
   machine only.
2. **`kafka-broker`** — a Container App in Azure, shared by whatever's
   deployed there (the "cloud dev" environment this pipeline deploys to).

They are not the same broker and don't share data. A service running
locally never talks to `kafka-broker`; a service deployed to Azure never
talks to `local-kafka`.

### Cost control: scale-to-zero

Container Apps bill primarily for active replica time. Two scripts,
`start-all.bat` / `stop-all.bat`, toggle every Container App between
`min-replicas: 1` (running) and `min-replicas: 0` (idle, no billing) —
useful on a constrained budget (this project runs on an Azure for
Students grant). MySQL is left running always (free under the grant).

---

## 6. A real incident — worth telling in a viva as a concrete story

This project actually hit a production CI/CD-adjacent incident, which is
good material to talk through because it shows real operational
understanding, not just pipeline syntax:

**What happened:** `assignment-service`, `notification-service`, and
`sla-service` each run a background Kafka consumer. Its `ExecuteAsync`
method was written as a **blocking, non-async loop** — it called
Kafka's `Consume()` (a synchronous, blocking API) directly, without ever
`await`-ing. Since `BackgroundService.StartAsync()` calls `ExecuteAsync`
as part of the **host's own startup sequence**, this meant the entire
app's startup was blocked on Kafka being reachable.

When `kafka-broker` was scaled down (for cost savings), these three
services couldn't connect, host startup hit its timeout, threw an
unhandled `TaskCanceledException`, and **crashed the entire process** —
not just the Kafka listener. Azure restarted the crashed container,
which crashed again immediately, forever — an infinite crash-restart
loop that Container Apps surfaced as `RunningState: ActivationFailed`.

**Why this defeated cost-saving:** even with `min-replicas: 0`, a
revision already stuck in a crash-loop keeps retrying — `min-replicas`
only controls whether Azure *starts* a new replica for demand, it
doesn't interrupt one already looping. So these three services kept
generating restart attempts (and log-ingestion cost) despite looking
"stopped."

**The fix:** wrap the blocking consume loop in `Task.Run(...)` so
`ExecuteAsync` returns immediately, letting the host finish starting
(and Kestrel bind) regardless of whether Kafka is reachable yet. The
consumer just keeps retrying quietly in the background instead of
taking the whole app down with it.

**Process point for the viva:** the fix went through a proper
`hotfix/*` branch cut from `develop`, with each service's fix as its own
commit, verified locally (Kafka stopped on purpose, confirmed `/health`
responds), then a PR back into `develop` — which is exactly what
`auth-ci.yml`'s trigger config is designed for: the PR ran the build
checks without touching Azure, and merging it triggered the real
deploy automatically via the path-filtered CD steps.

---

## 7. Gaps and future improvements (honest assessment)

These are real, current limitations — good to know for a viva since
"what would you improve" is a near-guaranteed question.

1. **No automated tests run in CI.** `dotnet build` runs, but never
   `dotnet test` — even though `Auth.Api.Tests` and `Ticket.Api.Tests`
   projects already exist with real integration tests (401/403/200
   authorization checks, etc.). This is the single highest-value fix:
   add a `dotnet test` step before the Docker build, so a broken test
   blocks the deploy.

2. **`main` never deploys anywhere.** Only `develop` has a live CD
   target. There's no defined promotion path from `develop` → `main` →
   a production environment. Either `main` needs its own deploy target,
   or the branching strategy needs to be made explicit (e.g. `develop`
   *is* the only environment for now).

3. **Heavy duplication across the six workflow files.** They're
   ~95% identical, differing only in service name/path/image. This
   could be collapsed into a single **reusable workflow**
   (`workflow_call`) parameterized by service name, cutting six files
   down to one template + six thin callers — much easier to maintain
   (a CI change currently has to be hand-copied six times).

4. **No canary/blue-green rollout.** `Single` revision mode means a bad
   deploy goes to 100% of traffic immediately. Container Apps supports
   multi-revision mode with traffic splitting — a small-percentage
   canary before full cutover would catch problems like the Kafka
   incident before they hit every user.

5. **Code duplication across services isn't just in workflows.** The
   `Roles.cs` constants, `TicketCreatedEvent` DTO, and the (now-fixed)
   `TicketCreatedConsumer` are byte-for-byte copy-pasted into
   Assignment, Notification, and SLA. A shared internal NuGet package
   would remove the risk of fixing a bug (like the crash-loop one) in
   one copy and forgetting the other two.

6. **No image vulnerability scanning** (e.g. Trivy, Grype, or ACR's own
   scanning) wired into the pipeline before push.

7. **No branch protection enforcement visible in-repo** — nothing stops
   someone pushing straight to `develop` without a PR/review, even
   though the pipeline is clearly designed around a PR-first workflow
   (PRs get checks but not deploys).

8. **Secrets are OIDC-based (good)**, but there's no secret scanning
   step (e.g. gitleaks) to catch an accidentally-committed credential
   before it ships.

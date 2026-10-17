# CI/CD Pipeline — Explained

A complete walkthrough of how this repo builds, tests, and deploys itself: the
GitHub Actions pipelines, the Dockerfiles, docker-compose, and the Azure
infrastructure they deploy to. Written for a viva — it explains *what* each
piece does and *why* it's built that way, plus the honest gaps and what
should come next.

---

## 1. The big picture

This is a **monorepo** containing five independent .NET 8 microservices
(Auth, Ticket, Assignment, Notification, SLA) and one React frontend. Each
has its own pipeline, and the repository has three more workflows for
quality and security. Everything lives in `.github/`:

```
.github/
├── workflows/
│   ├── backend-service.yml      reusable pipeline shared by the five services
│   ├── backend-integration.yml  Ticket-only: integration tests against Kafka
│   ├── auth-ci.yml              ┐
│   ├── ticket-ci.yml            │ five thin callers: they pass in the names
│   ├── assignment-ci.yml        │ that differ and nothing else
│   ├── sla-ci.yml               │
│   ├── notification-ci.yml      ┘
│   ├── frontend-ci.yml          frontend pipeline (same stages, Node instead of .NET)
│   ├── sonarcloud.yml           one SonarCloud analysis for the whole monorepo
│   ├── security.yml             CodeQL and secret scan
│   └── wso2-gateway-ci.yml      validates the API definitions, boots a WSO2 container
├── actions/deploy-container-app/action.yml
│                                deploy, wait for health, roll back on failure
└── scripts/test-summary.py      turns test and coverage output into a job summary
```

Each service workflow is **path-filtered**: it only runs when files under its
own service folder, or the shared pipeline files, change. This is the core
decision of the setup:

> **Changing the Ticket service should not rebuild, retest, or redeploy
> Auth, SLA, Notification, Assignment, or the frontend.**

That is the main benefit of per-service pipelines in a monorepo: faster
feedback (only what changed gets built) and independent deployability (one
service's bad build does not block another's release).

---

## 2. Anatomy of a service pipeline

A caller such as `auth-ci.yml` is about 40 lines. It declares the triggers and
the service's own values, then calls the shared pipeline:

```yaml
jobs:
  pipeline:
    uses: ./.github/workflows/backend-service.yml
    with:
      service: auth
      project-dir: services/auth/Auth.Api
      project-name: Auth.Api
      container-app: auth-service
      db-name: auth_db
      db-env-var: ConnectionStrings__AuthDb
    secrets:
      DB_CONNECTION_STRING: ${{ secrets.AUTH_DB_CONNECTION_STRING }}
      # plus the three Azure OIDC secrets
```

Before this, the five workflows were about 95% identical and every change had
to be copied by hand five times. The values that genuinely differ are the
inputs above; Ticket also sets `ef-context` (it has two EF contexts),
and `test-filter`.

**Triggers.** A `push` or `pull_request` to `main` or `develop`, but only if
the diff touches that service's folder or the shared pipeline files. A pull
request runs the checks and **never deploys**. A push to `develop` (which is
what merging a pull request does) runs the checks and deploys.

### The jobs

The shared pipeline has three jobs. They run in this order, each waiting for
the one before it with `needs:`:

| Job | Runs on | What it does |
|---|---|---|
| `test` | every run | Restore, build, apply the migrations to a throwaway MySQL service container, run the unit tests with coverage, write a results table to the job summary, upload the `.trx` and coverage files as an artifact |
| `image` | every run | Build the Docker image. **Gate:** Trivy (run from its own container, because `aquasecurity/trivy-action` is broken upstream) fails the run on a CRITICAL vulnerability with a fix in the libraries our code depends on. **Report only:** operating-system findings in the base image. On a push to `develop`, push the image to ACR |
| `deploy` | push to `develop` only | Log in to Azure, apply the migrations to the real database, then update the Container App, wait for the new revision to be healthy and roll back if it is not |

**Ticket also has an integration job.** `backend-integration.yml` runs the tests tagged
`Integration` against a real Kafka broker (a service container). Only `ticket-ci.yml` calls it,
so the other services carry no empty placeholder for it. It runs alongside the shared pipeline
and does not yet gate the deploy.

Splitting build, test and deploy into jobs is what makes the stages visible
in the GitHub UI, lets `deploy` be skipped on a pull request without a string
of `if:` conditions, and means a failed test job never reaches an Azure login.

### The deploy action

`.github/actions/deploy-container-app` is used by every service and the
frontend. It does four things:

1. Reads the image the app is running now.
2. Runs `az containerapp update` with the new image.
3. Polls the new revision until it reports `Healthy` and `Provisioned`, or fails
   after five minutes (or straight away on `Failed` or `Unhealthy`).
4. If step 3 fails, runs `az containerapp update` again with the image from step 1.

If the app has been scaled to zero on purpose (`scripts/stop-all`), step 3 is
skipped with a notice, because there is no replica to check and waking one
would cost money. Both paths were exercised against the live `sla-service`: redeploying
its current image passes, and deploying a missing image tag makes the new revision sit in
`Activating` until the timeout, after which the action rolls back and exits 1. A revision
that cannot pull its image never reports `Failed`, so a bad deploy takes the full timeout
(five minutes by default) to be reported.

### Concurrency

Pull-request runs are cancelled when a newer commit is pushed. Runs on
`develop` are never cancelled, and each Container App has its own deploy group
(`deploy-<app>`), so two quick merges deploy one after the other and not
simultaneously.

### Authentication to Azure

`id-token: write` lets GitHub issue a short-lived OIDC token that Azure trusts,
so the pipeline holds no long-lived Azure password. The federated credential
trusts the **branch** (`refs/heads/develop`). For that reason the deploy jobs
deliberately do **not** use a GitHub "environment": an environment changes the
token's subject and would break the login.

### Image tagging: `github.sha`, not `latest`

Every image is tagged with the **commit SHA** that built it, never `latest`:
- **Traceability.** A running container's image tag says exactly which commit is live.
- **Immutability.** The same tag always refers to the same build.
- **Rollback.** `az containerapp update --image ...` returns to any earlier tag
  with no rebuild. The deploy action does exactly this on a failed health check.

### The frontend pipeline

`frontend-ci.yml` has the same three stages. `test` runs `npm ci`, the unit
tests with coverage and the production build (with `CI=true` the build treats
lint warnings as errors, so it doubles as the lint check). `image` builds and
pushes the image, and `deploy` uses the same deploy action on the Container
App `it-helpdesk-frontend`. The *image* is called `frontend` but the *Container
App* is `it-helpdesk-frontend`, a naming inconsistency rather than a bug.

---

## 3. Tests, coverage, and SonarCloud

Every backend workflow restores and builds the service's adjacent `*.Tests`
project, runs it before building the Docker image, and fails the workflow if
any test fails. The frontend workflow runs the existing Create React App
tests in non-watch mode before its production build.
The test jobs provision fresh MySQL databases and apply the checked-in
migrations before test execution; the Ticket workflow pins `TZ=UTC` so
date-boundary tests are deterministic.

The repository-level `.github/workflows/sonarcloud.yml` runs the five backend
test projects and the frontend tests with coverage, then performs one
SonarCloud analysis for the monorepo. A single analysis avoids concurrent
updates to one SonarCloud project when several service paths change in the
same commit. Configure these repository settings before enabling the required
check:

| Setting | Type | Purpose |
|---|---|---|
| `SONAR_PROJECT_KEY` | Actions variable | SonarCloud project key |
| `SONAR_ORGANIZATION` | Actions variable | SonarCloud organization key |
| `SONAR_TOKEN` | Actions secret | SonarCloud authentication token |

The workflow does **not** wait for the quality gate (`sonar.qualitygate.wait` is
not set): the free SonarCloud plan refuses gate data for any branch except the
main one, which failed the step even though the analysis uploaded fine. The gate
result is read on sonarcloud.io and shown on the pull request. Pull requests only
receive read access to repository contents and never receive Azure or database
credentials.

## 4. Automated database migrations

Each backend service has checked-in EF Core migrations. Services do not apply
schema changes during application startup; the `deploy` job of the relevant service pipeline runs
`dotnet ef database update` after Azure OIDC login and before
updating the Container App. This explicit release step avoids
multiple application replicas racing to migrate and keeps test hosts isolated.

The migration step is gated to pushes on `develop`, exactly like image push
and deployment. A migration failure stops the workflow before the new image
is deployed. EF's migration history makes successfully applied migrations
idempotent across retries. Configure these repository secrets with the
connection string for each deployed database:

| Secret | Service/database |
|---|---|
| `AUTH_DB_CONNECTION_STRING` | Auth |
| `TICKET_DB_CONNECTION_STRING` | Ticket |
| `ASSIGNMENT_DB_CONNECTION_STRING` | Assignment |
| `SLA_DB_CONNECTION_STRING` | SLA |
| `NOTIFICATION_DB_CONNECTION_STRING` | Notification |

Connection strings are passed to the EF CLI through the step environment and
are never echoed. Pull requests do not run migration steps and cannot access
these secrets. Database backups and a tested rollback procedure remain an
operational responsibility; rolling back an image does not automatically
reverse an already-applied schema migration.

## 5. The Dockerfiles

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

## 6. `docker-compose.yml` — local development only

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

## 7. Azure infrastructure — what the pipelines actually deploy to

| Piece | Name / detail |
|---|---|
| Resource group | `it-helpdesk-rg` |
| Container registry | `ithelpdeskacr02.azurecr.io` (Azure Container Registry) |
| Compute platform | **Azure Container Apps** (serverless containers, not raw VMs/AKS) |
| Region | Central India |
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

Container Apps bill primarily for active replica time. Two scripts in
`scripts/` (`start-all` / `stop-all`, each as `.ps1` and `.sh`) move every
Container App between running and `min-replicas: 0` (idle, no compute billing),
deactivate the WSO2 gateway revision, and start/stop the MySQL server. How to
use them, and how to do the same by hand in the Azure portal, is in
`scripts/README.md`. MySQL is **not** free: it bills for every hour it is
running, so `stop-all` stops it too (it restarts by itself after 7 days).

---

## 8. A real incident — worth telling in a viva as a concrete story

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

## 9. Gaps and future improvements (honest assessment)

Checked against the final-evaluation rubric in
[CI-CD-RUBRIC-GAP-ANALYSIS.md](CI-CD-RUBRIC-GAP-ANALYSIS.md), which also says
what has already been fixed. What is still open:

1. **The integration job does not gate the deploy yet.** It runs the three Ticket
   tests against a Kafka container and has run green on a pull request. After a few
   more green runs, add it to the `needs` of the `image` job. Seven Assignment tests tagged `Integration` are still not run
   in CI: they need a running Auth service and agents with user IDs 3 and 26 in
   the database, and a later migration removed that seed data.

2. **`main` never deploys anywhere.** Only `develop` has a live deploy target.
   There is no promotion path from `develop` to `main` to a separate production
   environment. Either `main` needs its own target, or the branching strategy
   should state that `develop` is the only environment.

3. **WSO2 API definitions are validated but never published.** Changing
   `infra/wso2/apis/*.json` does not update the gateway; someone has to run
   `scripts/publish-wso2-apis.sh azure` by hand, and the gateway must be running.

4. **Vulnerable packages.** `dotnet list package --vulnerable` reports High
   advisories in transitive packages such as `System.Text.Json 8.0.0`. Nothing in
   the pipeline checks for this automatically (a report-only check was tried and
   removed because it only listed problems). Pinning patched versions would fix it.

5. **No canary or blue-green rollout.** `Single` revision mode sends a new
   revision 100% of traffic at once. The deploy action limits the damage with a
   health check and rollback, but does not test a revision before it takes traffic.

6. **Code duplication across services.** `Roles.cs`, `TicketCreatedEvent` and
   the Kafka consumers are copied between services. A shared internal NuGet
   package would remove the risk of fixing a bug in one copy and forgetting the
   others.

7. **The SonarCloud gate is not enforced by the pipeline.** The free plan
   refuses quality-gate data for any branch except the main one, so the workflow
   cannot wait for it. The result is read on sonarcloud.io and shown on the pull
   request. See `SONARQUBE_ADJUSTMENTS.md`.

8. **Branch protection is not visible in the repository.** Nothing in the code
   stops a direct push to `develop`. Requiring status checks is a repository
   setting.

9. **Only some actions are pinned to a commit SHA.** The third-party ones (`azure/login`, `gitleaks/gitleaks-action`)
   are pinned to the exact commit, with the tag in a comment. Actions owned by GitHub
   (`actions/*`, `github/codeql-action`) still use version tags.

## Ticket database: which context and the one-time baseline

The Ticket service has two EF contexts. Only `ApplicationDbContext` (the `Tickets` and `StatusHistory` tables) is the real schema, so CI runs
`dotnet ef database update --context ApplicationDbContext`. Without `--context` the command stops with "More than one DbContext was found".
`TicketDbContext` has a migration that creates a second `Tickets` table and fails on a database that already has one, so it is not applied.

The existing Ticket database was created by `EnsureCreated()` and has no migration history, so the first migration run fails with
"Table 'Tickets' already exists". Run `services/ticket/baseline-migrations.sql` once against the real ticket database. It marks the three
existing migrations as applied only when the `Tickets` table is already there, and does nothing on a brand new database. It is safe to run again.

Checked on 2026-10-13 against a throwaway MySQL 8: all five services migrate cleanly on an empty database, and the Ticket database
created the old way migrates after the baseline.

## Tests that need the full local stack are tagged `Integration`

Ten tests were written for the docker-compose setup and cannot pass in CI, which has only a MySQL container:

- Assignment (7): `AddAgent_*`, `RemoveAgent_AsAdministrator_*`, `ReassignTicket_*` and the queue test need a running Auth service on
  `localhost:5121` and agents with user IDs 3 and 26 already in the database.
- Ticket (3): `CreateTicket_ValidRequest_Returns201`, `GetStatusHistory_AfterMultipleChanges_*` and
  `UpdateStatus_Administrator_CanActOnAnyTicketRegardlessOfAssignment` publish to Kafka, so with no broker each hangs for about 5 minutes and times out.

They are marked `[Trait("Category", "Integration")]`, and the Assignment and Ticket CI test steps run with `--filter "Category!=Integration"`.
With the filter the Assignment suite runs 28 tests and the Ticket suite 22, in seconds. To run everything locally with the stack up:
`dotnet test` (no filter). To run only the integration tests: `dotnet test --filter "Category=Integration"`.

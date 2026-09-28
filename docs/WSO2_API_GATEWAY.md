# WSO2 API Gateway — Setup, Fixes & Explained

> IT Helpdesk & Ticketing Platform (SE3022 Case Study) — Sprint 3

This document explains what the WSO2 API Gateway is, why it's in this
project, what we had to do to get it actually working, and every problem we
hit along the way (with the fix). It's written so anyone picking this up
later — teammate, marker, or future you — can understand the *why*, not just
the *what*.

---

## 1. What is an API Gateway, and what is WSO2 specifically?

This platform is a microservices system: five independent .NET services
(Auth, Ticket, Assignment, SLA, Notification), each with its own address and
port. Without a gateway, a client (the frontend, or an external consumer)
would need to know all five addresses, and every service would have to
separately handle concerns like authentication enforcement, CORS, and rate
limiting.

An **API Gateway** sits in front of all of them as a single public entry
point. It routes each incoming request to the right backend service based on
the URL path, and centralizes cross-cutting concerns instead of duplicating
them five times.

**WSO2 API Manager** is a specific, full-featured, open-source product that
does this. Unlike a small hand-written gateway, it's a complete platform:

- **Gateway** — the actual traffic proxy (data plane)
- **Publisher** — where APIs are defined, configured, and published (control plane)
- **DevPortal** (Developer Portal) — where API consumers can discover and subscribe to APIs
- **Key Manager** — OAuth2 token issuance and validation

It ships as a single Docker image (`wso2/wso2am:4.3.0`) that bundles all four
of the above into one process for a "hybrid" single-node deployment, which is
what this project runs in `docker-compose.yml`.

### Why WSO2, and not just the existing custom gateway?

This repo actually had **two** gateways added in the same sprint:

1. A small custom gateway (`src/ApiGateway/`) built with .NET/YARP by the dev.
2. WSO2 API Manager (`wso2-gateway` in `docker-compose.yml`, config in
   `infra/wso2/`), set up separately by DevOps.

**The team's decision: WSO2 is the standard, authoritative gateway.** The
YARP gateway (`src/ApiGateway/`) is kept in the repo as a reference/fallback
implementation only — it is not the live routing path, should not be assumed
to receive real production traffic, and should not be extended going
forward. Both gateways were fixed and independently verified working during
this sprint (see `git log` for `fix(gateway):` commits for the YARP side),
so the fallback is a working one, not dead code — but WSO2 is what clients
should be pointed at, and what future gateway work should happen in.

This document covers the WSO2 side only. If the team later decides to drop
the fallback entirely, remove `src/ApiGateway/` and its CI/CD pipeline
(`.github/workflows/gateway-ci-cd.yml`) in one clean commit — but that's a
separate, deliberate decision, not something to do incidentally.

---

## 2. Where everything lives

```
infra/wso2/
├── apis/
│   ├── auth-api.json           # OpenAPI 3.0 definition, one per service
│   ├── ticket-api.json
│   ├── assignment-api.json
│   ├── sla-api.json
│   └── notification-api.json
├── deploy-apis.sh              # Bash: imports + publishes all 5 APIs into WSO2
└── deploy-apis.ps1             # PowerShell equivalent, for Windows

docker-compose.yml               # defines the `wso2-gateway` service
.github/workflows/wso2-gateway-ci.yml   # CI: validates the OpenAPI files + boot-tests WSO2
```

Each `apis/*.json` file is a standard OpenAPI 3.0 document with WSO2-specific
`x-wso2-*` vendor extensions describing where the real backend lives
(`x-wso2-production-endpoints`, `x-wso2-sandbox-endpoints`) and what base path
to expose it under (`x-wso2-basePath`).

---

## 3. Prerequisites

To run this locally, you need:

- **Docker Desktop** (WSO2 runs as a container — the image is large, ~700MB+,
  so the first pull takes a while)
- **MySQL** running locally (`docker compose up -d mysql`) — the backend
  services need it to start
- The relevant backend services running locally (e.g. Auth.Api on `5121`,
  Ticket.Api on `5164`, Assignment.Api on `5067` — see each service's
  `launchSettings.json` for its real port)
- **PowerShell** (Windows) to run `deploy-apis.ps1` — Windows PowerShell 5.1
  is sufficient, `pwsh` (PowerShell 7) is *not* required despite the script's
  shebang line implying it
- **curl + jq** (Linux/macOS/WSL) to run `deploy-apis.sh` instead

WSO2 exposes three ports:

| Port | Purpose |
|------|---------|
| `8280` | HTTP gateway — where actual API traffic goes |
| `8243` | HTTPS gateway |
| `9443` | Management: Publisher UI, DevPortal UI, and the REST APIs used to configure WSO2 |

---

## 4. The steps we followed

1. **Fixed the OpenAPI definitions** (`infra/wso2/apis/*.json`) — corrected
   wrong backend ports and a path mismatch (see Problem 1 below).
2. **Decided what to do about SLA and Notification** having no real REST API
   — trimmed their specs down to only advertise `/health`, since that's all
   that actually exists on those services (see Problem 2).
3. **Booted the `wso2-gateway` container** (`docker compose up -d
   wso2-gateway mysql`) and waited for it to report healthy.
4. **Ran `deploy-apis.ps1`** against the running container to import,
   configure, and publish all 5 APIs.
5. **Tested real routes through the gateway** (port `8280`) against the
   actually-running backend services, to prove requests genuinely reach the
   real code — not just that the script exits without an error.

Step 5 is the one that surfaced almost all of the problems below. Nothing in
this setup had actually been end-to-end tested before — the scripts looked
plausible and "ran successfully" (no errors), but nothing they did had ever
been verified to actually route real traffic.

---

## 5. Problems we ran into, and how we fixed them

### Problem 1 — Wrong backend ports and a path mismatch in the API definitions

The OpenAPI files pointed at made-up ports (`assignment-api:5165`,
`sla-api:5166`, `notification-api:5167`) that didn't match the services' real
ports (`5067`, `5262`, `5214` respectively, per each service's
`launchSettings.json`). Assignment's path was also wrong: the definition said
`/api/Assignment` (singular) but the real controller
(`AssignmentsController`) is routed at `/api/Assignments` (plural).

**Fix:** corrected the ports and the path in `assignment-api.json`,
`sla-api.json`, and `notification-api.json`. Validated with `swagger-cli
validate` (the same check the CI pipeline runs).

### Problem 2 — SLA and Notification don't have a real REST API

`Sla.Api` and `Notification.Api` are Kafka-consumer-only services — they have
no HTTP controllers at all, just a bare `/health` endpoint. Their OpenAPI
specs still advertised routes like `GET /` and `GET /breaches` that don't
exist in the code, so anyone calling them through WSO2 would get an
error for a feature that was never built.

**Fix:** trimmed `sla-api.json` and `notification-api.json` down to only
declare `/health`, so the published API honestly reflects what exists today.
(Checked the frontend for any usage of these endpoints first — there is
none; the SLA/Notification flow is entirely event-driven via Kafka, not
REST.)

### Problem 3 — Wrong WSO2 REST API path (`/publisher/v4/apis`)

The Docker health check, both `deploy-apis` scripts, and the CI workflow all
polled `https://localhost:9443/publisher/v4/apis` to check WSO2 was ready.
That path returned `404` — it's the **Publisher web UI** context, not the
REST API. The real Publisher REST API lives at
**`/api/am/publisher/v4/apis`**.

This meant the Docker health check could never pass, and both deploy
scripts' very first step ("wait for WSO2 to be ready") would spin until
timeout and fail.

**Fix:** replaced every occurrence of `/publisher/v4/` with
`/api/am/publisher/v4/` in `docker-compose.yml`, `deploy-apis.sh`,
`deploy-apis.ps1`, and `wso2-gateway-ci.yml`.

### Problem 4 — `deploy-apis.ps1` crashes on Windows PowerShell 5.1

The script uses an em dash (`–`) inside string literals. The file has no
byte-order mark (BOM), so Windows PowerShell 5.1 (the version actually
installed by default on Windows, as opposed to PowerShell 7/`pwsh` which the
script's shebang assumes) misreads the UTF-8 bytes, which corrupts the
string and throws a cascade of "missing terminator" / "missing closing
brace" parse errors — the script wouldn't even start.

**Fix:** replaced the em dashes with plain ASCII hyphens.

### Problem 5 — Importing an OpenAPI file doesn't set the backend endpoint

After `import-openapi` succeeds and returns an API ID, the API's
`endpointConfig` is `null`. WSO2 does **not** automatically read the
`x-wso2-production-endpoints` / `x-wso2-sandbox-endpoints` vendor extensions
from the file during import, despite that being their whole purpose.
Publishing an API with no endpoint fails with:

```json
{"code":900967,"message":"General Error","description":"Server Error Occurred"}
```

(WSO2's own log shows the real reason: `Failed to publish service to API
store. No endpoint selected`.)

**Fix:** after import, both scripts now `GET` the full API object, set
`endpointConfig` from the URLs already declared in the OpenAPI file's
`x-wso2-*-endpoints` extensions, and `PUT` it back before publishing.

### Problem 6 — Publishing also fails without a subscription policy

Once the endpoint was fixed, publishing still failed with the same generic
500 — this time because `policies` (WSO2's subscription tiers, e.g.
"Unlimited") was an empty array. WSO2 logged: `No Tiers selected`.

**Fix:** both scripts now also set `policies = ["Unlimited"]` in the same
update call as the endpoint fix.

### Problem 7 — "Published" doesn't mean "live on the gateway"

Even after publishing succeeded, calling the API through the actual gateway
port (`8280`) returned `404`. In WSO2 4.x, changing an API's lifecycle state
to `Published` only affects the Publisher/DevPortal — the API isn't deployed
to a gateway's data plane until you separately **create a revision** and
**deploy that revision** to a named environment (`Default` in this setup).
Neither script did this at all.

**Fix:** both scripts now create a revision (`POST
.../apis/{id}/revisions`) and deploy it (`POST
.../apis/{id}/deploy-revision?revisionId=...`) to the `Default` environment
right after publishing.

### Problem 8 — Doubled version in the URL (`/auth/v1/v1`)

Even after the API was genuinely live, every request still 404'd. WSO2
automatically appends the API's own version (`v1`) onto whatever `context`
you give it at import time. Both scripts passed `context: "/auth/v1"` while
also setting `version: "v1"`, so the real exposed path became
`/auth/v1/v1/...` — not what anyone would guess to call.

**Fix:** changed the context maps in both scripts to be version-less
(`/auth`, `/ticket`, `/assignment`, `/sla`, `/notification`) and let WSO2
append `/v1` itself, producing the expected `/auth/v1/...` paths.

### Problem 9 — Public endpoints (like `/login`) required a token anyway

Even with the path fixed, calling `/auth/v1/login` returned WSO2's `401
Missing Credentials` — meaning nobody could actually log in through the
gateway, since logging in would require a token you can only get by logging
in. The OpenAPI file correctly marks `/login`, `/register`, and `/health` as
`"security": []` (public, no auth required), but `import-openapi` ignores
this and defaults **every** operation's WSO2 `authType` to `"Application &
Application User"` (protected).

**Fix:** both scripts now read each operation's `security` field from the
source OpenAPI file and set `authType = "None"` on any operation where
`security` is an empty array (or absent), before publishing.

### Problem 10 — `/health` doesn't route correctly for any service

Even with everything else working, `GET /auth/v1/health`,
`/ticket/v1/health`, and `/assignment/v1/health` returned an **empty 404**
through the gateway, while each service's own `/health` endpoint worked fine
called directly. `/sla/v1/health` and `/notification/v1/health` also 404'd.

**Root cause:** WSO2 forwards every operation to `{endpoint_base}` +
`{resource_path}`.

- For **Auth, Ticket, and Assignment**, the endpoint base (`/api/Auth`,
  `/api/Ticket`, `/api/Assignments`) is correct for the real controller-routed
  operations (`/login`, `/mine`, etc.), but each service's `/health` endpoint
  is mapped at the **root** of the app (`app.MapGet("/health", ...)`), not
  nested under that controller prefix. So WSO2 ended up requesting e.g.
  `/api/Auth/health`, which doesn't exist — the empty 404 was ASP.NET Core's
  default "no route matched" response, confirming the request *did* reach the
  backend, just at the wrong path.
- For **SLA and Notification**, the problem was slightly different: their
  entire exposed surface *is* just `/health` at the service root, but the
  WSO2 endpoint config still pointed at a non-existent `/api/Sla` /
  `/api/Notification` base (left over from before we trimmed their specs in
  Problem 2), so every request 404'd for the same "wrong prefix" reason.

**Fix (two parts):**

1. **SLA and Notification** — since `/health` is their *only* real operation,
   there's no need for a shared base path at all. Changed their
   `x-wso2-production-endpoints` / `x-wso2-sandbox-endpoints` URLs in
   `sla-api.json` and `notification-api.json` to point at the bare service
   root (`http://sla-api:5262`, `http://notification-api:5214`) instead of a
   non-existent controller path.
2. **Auth, Ticket, and Assignment** — these genuinely need the
   `/api/{Controller}` base for their real operations, so that couldn't just
   be changed. Instead, added a second health route in each service's
   `Program.cs`, mapped under that same controller prefix
   (`/api/Auth/health`, `/api/Ticket/health`, `/api/Assignments/health`),
   alongside the existing root `/health` (kept as-is, since the YARP
   gateway's active health check, Docker health checks, and direct
   developer checks all rely on it).

---

## 6. Verified end-to-end (what we proved actually works)

With all 5 backend services running locally and a clean `deploy-apis.ps1`
run:

- `POST /auth/v1/login` through WSO2 (port `8280`) reaches the real
  `AuthController.Login` code with no token required — confirmed by seeing a
  genuine ASP.NET error trace from the real backend (a local MySQL
  connectivity issue in the test environment, unrelated to WSO2).
- `GET /ticket/v1/mine` through WSO2 with no token correctly returns `401
  Missing Credentials` — protected routes are still protected.
- `GET /health` through WSO2 works for **all 5 services**
  (`/auth/v1/health`, `/ticket/v1/health`, `/assignment/v1/health`,
  `/sla/v1/health`, `/notification/v1/health`), each returning the real
  `{"status":"healthy","service":"..."}` payload from the actual backend.

No previously-working route regressed after the `/health` fix.

---

## 7. Quick reference — running this yourself

```bash
# 1. Start MySQL + WSO2
docker compose up -d mysql wso2-gateway

# 2. Wait for WSO2 to report healthy
docker inspect --format='{{.State.Health.Status}}' local-wso2-gateway

# 3. Start the backend services you want to test (example: Auth.Api)
cd services/auth/Auth.Api
dotnet run

# 4. Publish all 5 API definitions into WSO2
cd ../../../infra/wso2
./deploy-apis.ps1        # Windows
# or
bash deploy-apis.sh      # Linux/macOS/WSL

# 5. Call a route through the gateway
curl -X POST http://localhost:8280/auth/v1/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","password":"secret"}'
```

Publisher UI: `https://localhost:9443/publisher`
DevPortal: `https://localhost:9443/devportal`
(Default WSO2 login: `admin` / `admin` — **must be changed before any
non-local deployment**, see the "wrong direction" note in the deployment
discussion: this is not currently safe to expose beyond localhost.)

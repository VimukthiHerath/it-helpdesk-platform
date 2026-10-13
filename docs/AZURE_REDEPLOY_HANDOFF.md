# Azure Redeploy — Handoff (state as of 2026-10-11)

Audience: the next person who looks after this deployment. You were not in the
session where this was built, so this document explains **what exists, what
works, what does not, what it costs, and what you must do next.**

It replaces nothing: `docs/DEPLOYMENT_HANDOVER.md` is still the original
step-by-step plan. This file records what *actually happened* when that plan
was run on the new subscription, and where reality differed from the plan
(section 6). Where the two disagree, trust this file.

> **Secrets are not in this file.** They live in `.local-notes/` on
> Vimukthi's machine (gitignored). See section 9 for how to get or recover them.

---

## UPDATE 2026-10-13 — read this first (it overrides anything below that disagrees)

**Current state**
- **Everything is scaled to 0** (apps and WSO2 revision). Only MySQL, the registry
  and storage keep running and costing a small amount (section 7). The "running 24/7"
  statements further down describe 2026-10-11 and are no longer true.
- **Access:** `it24101503@my.sliit.lk` has **Contributor on `it-helpdesk-rg`**
  (granted 2026-10-13). Sign in with `az login` (tenant
  `44e3cf94-19c9-4e32-96c3-14f5bf01391a`) and select subscription
  `7e76da9f-fdb8-43bd-9551-28bc90afcd2f`. Resource-group-level access shows the
  group under **Resource groups** in the portal; the subscription itself may not
  appear in the Subscriptions blade. Budgets and Cost analysis need Reader on the
  subscription, which was not granted.
- **Sprint 4 code is on the `QA-Sprint-4` branch** (capital letters; a lowercase
  `qa-sprint-4` branch existed and was deleted because Windows cannot tell them
  apart). It is **not deployed** and **not yet merged into `develop`**.
- **CI/CD (GitHub Actions) is still not set up** — see "CI/CD setup" below.

**What changed in Sprint 4 (all on `QA-Sprint-4`)**
- Every browser call now goes through the WSO2 gateway (`/auth/v1`, `/ticket/v1`,
  `/assignment/v1`); the frontend no longer calls services directly.
- Auth signs tokens with **RS256** and publishes its public key at
  `/.well-known/jwks.json`. Ticket, Assignment and SLA verify with it.
- Password reset: Auth publishes a `password-reset-requested` Kafka event and
  Notification sends the email (the old code only logged the link).
- Agent roster management, configurable SLA durations (new admin-only SLA routes),
  per-agent report, Swagger docs. The committed RSA private key was removed from git.
- Auth now has migration `AddPasswordResetFields` — **it must be applied by hand**
  (auth never migrates at startup). Assignment and SLA migrate themselves.

**Status 2026-10-13 (evening): preparation is done, nothing is running.** CI/CD works
(all workflows green on `develop`, Azure runs the CI-built images). The settings and
secrets in the table below **are now applied** to the five apps, and the auth migration
`AddPasswordResetFields` **is applied** to Azure MySQL. Still to do: bring the apps up,
re-publish the WSO2 APIs, decide the WSO2 token approach (see "Open WSO2 risk"), run the
smoke tests, then scale back to 0. Old accounts get a security stamp on their next login,
so the existing admin keeps working.

**Gateway token approach — decided and tested 2026-10-13 (supersedes "Open WSO2 risk" below)**
- Tried: a WSO2 Key Manager that trusts Auth's issuer. WSO2 rejects Auth's RS256 tokens on
  protected routes (`900901 Invalid Credentials`), and creating a custom Key Manager failed
  (`901401 Required Key Manager configuration missing`; the needed settings are not
  documented for this edition). WSO2 also wants each token tied to a registered application,
  which Auth's tokens do not have. Stopped there.
- **In use: the gateway only routes; the services check the JWT.** All 5 APIs are published
  with every operation set to `x-auth-type: None`. The services already validate Auth's RS256
  token through its JWKS URL. Tested through the gateway: valid admin token returns 200
  (`/ticket/v1/report`, `/auth/v1/me`, `/auth/v1/users`); no token and a tampered token
  return 401; login, register, forgot-password and the health routes are public.
- Auth advertises its key URL as `http://` behind Azure's ingress; the services follow the
  redirect to `https://`, so nothing needs changing.
- **Consequence:** auth, ticket and assignment still have public URLs, so someone can call a
  service without the gateway (they still need a valid token). To make "everything goes
  through the gateway" literally true, switch those three to internal ingress and update the
  gateway backend URLs, `Jwt__JwksUri`, `AuthApiUrl` and `AssignmentApi__BaseUrl` to the
  `.internal.` hostnames. Not done yet.
- **Re-publishing the APIs** (needed only if the share is wiped, or the specs change): the
  specs live in `infra/wso2/apis/`. Stage them on the file share, run a `curl`-only script
  inside the gateway with `az containerapp exec` that deletes the old APIs, then imports each
  spec with its backend URL, publishes it and deploys a revision (sections 6 item 8 and 8.7).
  `DELETE /apis/{id}` works on published APIs, so no lifecycle change is needed first.

**Sprint 4 deploy checklist (settings that did not exist in Azure before 2026-10-13)**

`<D>` = `proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io`. Store keys and
passwords as Container App secrets and reference them with `secretref:`.

| App | Add |
|---|---|
| `auth-service` | `Jwt__RsaPrivateKeyPem` (**secret**; a fresh key — never reuse the one that was committed to git), `InternalService__ApiKey` (secret), `PasswordReset__ResetBaseUrl=https://it-helpdesk-frontend.<D>/reset-password`, `Kafka__BootstrapServers=kafka-broker:9092`. Keep the existing `Jwt__Key` (legacy tokens still validate) |
| `notification-service` | `InternalService__ApiKey` (**same value as auth**) |
| `ticket-service` | `Jwt__JwksUri=https://auth-service.<D>/.well-known/jwks.json` |
| `assignment-service` | `Jwt__JwksUri` (as above) and **`AuthApiUrl=https://auth-service.<D>`** (the add-agent call to Auth uses it and it is missing from Azure today, so adding an agent to the rotation fails) |
| `sla-service` | `Jwt__JwksUri` (as above); SLA now validates JWTs |
| `it-helpdesk-frontend` | Rebuild the image. `frontend/.env.production` must have **all four** `REACT_APP_*_URL` values set to `https://wso2-gateway.<D>` |
| `wso2-gateway` | Re-publish the 5 APIs from `infra/wso2/apis/*.json` (the routes changed; see section 6, item 8). Then see "Open WSO2 risk" |

Then apply the auth migration to Azure MySQL (section 8.4), build and push all six
images (section 8.2), roll each app (section 8.3), start Kafka first, activate
exactly one WSO2 revision, and run the smoke tests (section 8.6). Scale back to 0
afterwards (section 7).

**Open WSO2 risk (not verified):** the design says the gateway verifies the JWT
using a Key Manager pointed at Auth's JWKS (`docs/WSO2_API_GATEWAY.md`, section 12).
That has never been tried here. WSO2 may also insist on an application
subscription for each token. If it does not work, the fallback is to register the
protected routes without WSO2-level auth and let the services enforce the JWT, which
they already do — but then the services must be made **internal-only**, otherwise
the gateway can be bypassed. Decide this with the team before the demo.

**CI/CD setup (what is needed, in order)**
1. **Done in Azure on 2026-10-13:** managed identity `github-actions-identity`
   (client id `f0c24a18-189f-4ff3-a442-417de5728143`, principal id
   `2f069003-3df9-4a99-b2f6-6ebe13af4f37`) with a federated credential `github-develop`
   for `repo:VimukthiHerath/it-helpdesk-platform:ref:refs/heads/develop`, plus
   **Contributor** on `it-helpdesk-rg` and **AcrPush** on `ithelpdeskacr02`.
   Only pushes to `develop` can log in as it. To allow another branch, add another
   federated credential with that branch in the subject.
   **Gotcha (found 2026-10-13):** this repository's OIDC subject includes numeric ids,
   so the name-style subject above never matches and every deploy fails at "Log in to
   Azure" with `AADSTS700213 No matching federated identity record`. The subject GitHub
   actually presents is `repo:VimukthiHerath@176001576/it-helpdesk-platform@1338640258:ref:refs/heads/develop`,
   so the credential must use that exact string. Read the real subject from the error
   in the failed run's annotations if it ever changes.
2. In GitHub (repo Settings, Secrets and variables, Actions) add three **secrets**
   (these are IDs, not passwords): `AZURE_NEW_CLIENT_ID` = `f0c24a18-189f-4ff3-a442-417de5728143`,
   `AZURE_NEW_TENANT_ID` = `44e3cf94-19c9-4e32-96c3-14f5bf01391a`, `AZURE_NEW_SUBSCRIPTION_ID` =
   `7e76da9f-fdb8-43bd-9551-28bc90afcd2f`. The names are deliberately different from the
   old `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` secrets, which hold
   values for the old, dead subscription and are no longer used by the workflows.
   **Status: being added by Vimukthi.**
3. Get the config commits into `develop`: the workflows must point at
   `ithelpdeskacr02` (they still say `ithelpdeskacr01` on `develop`).
4. Only a **push to `develop`** deploys. Each service workflow builds, pushes, then
   runs `az containerapp update --image`, which swaps the image and keeps every
   setting above. It does **not** start apps that are scaled to 0 and does not
   touch WSO2.

---

## 0. TL;DR

- The whole platform is **deployed and running** on a **new Azure account**
  (the old account's student credit ran out).
- **It is costing credit right now**, 24/7, even when nobody is using it
  (section 7). Scale it down when you are not demoing or testing.
- Deployed code = branch **`develop` as of commit `5fe1673`**, plus three
  local-only config commits. The **`QA-Sprint-4` fixes are NOT deployed**
  (section 5).
- **CI/CD (GitHub Actions → Azure) is NOT set up.** Pushing to `develop` will
  fail at the Azure login step. Deployments so far were done by hand (section 8).
- Not yet tested end to end: creating a ticket and watching it flow through
  Kafka → assignment → SLA → notification email (section 3).
- Known security weak spots: weak admin password, open role self-assignment on
  `/register`, WSO2 still on `admin/admin` (section 4).

---

## 1. What this project is, and why we redeployed

IT Helpdesk platform: React frontend + 5 .NET services (auth, ticket,
assignment, sla, notification) + MySQL + Kafka + a WSO2 API Manager gateway,
all on **Azure Container Apps**.

The previous deployment (original account) burned the whole ~$40 student credit
in a few days. Causes, from the old Azure logs: WSO2 crash-looping with 2
replicas sharing one H2 database file, a "stop" script that did not really
stop anything, and no budget alert. Details are in `DEPLOYMENT_HANDOVER.md`
section 0. **Those lessons shape everything below.**

This new deployment is a **separate Azure account**:

| Item | Value |
|---|---|
| Account that owns it | `it24101497@my.sliit.lk` |
| Subscription | `Azure for Students`, id `7e76da9f-fdb8-43bd-9551-28bc90afcd2f` |
| Tenant | `44e3cf94-19c9-4e32-96c3-14f5bf01391a` |
| Resource group | `it-helpdesk-rg` (see 1.1) |

The old subscription (`0e4a7888-…`) is disabled and out of credit. Ignore it.

### 1.1 What a resource group is

A resource group is just a **folder in Azure**. `it-helpdesk-rg` holds every
resource of this project. Two practical uses: all cost shows under one name,
and **deleting the resource group deletes everything inside it** (the way to
stop all charges for good). It was newly created on this subscription. Its own
"location" (`southeastasia`) is only metadata and does not matter; the
resources inside are in the regions in section 2.

---

## 2. What exists in Azure

Everything is in `it-helpdesk-rg`.

**Region note:** the subscription has an Azure Policy that only allows
`centralindia`, `indonesiacentral`, `eastasia`, `austriaeast`, `malaysiawest`.
`southeastasia` (used in the old plan) is **blocked**. We used `centralindia`.
MySQL could not be created in `centralindia` (the service returned an internal
server error there), so **MySQL is in `indonesiacentral`**.

| Resource | Name | Notes |
|---|---|---|
| Container Apps environment | `it-helpdesk-envstd` | Workload-profiles ("standard") type, `centralindia`. Default domain: `proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io` |
| Container Registry | `ithelpdeskacr02` (Basic) | Old name `…01` was taken globally |
| Identity | `acr-pull-identity` | Has `AcrPull` on the registry; apps use it to pull images |
| MySQL Flexible Server | `it-helpdesk-mysql02` | Burstable B1ms, 20 GB, MySQL 8.0.21, **indonesiacentral**. DBs: `auth_db`, `ticket_db`, `assignment_db`, `sla_db`, `notification_db` (+ an unused `flexibleserverdb`) |
| Storage account | `ithelpdeskwso2sb` | File share `wso2-data` holds WSO2's databases. Mounted into the environment as `wso2-data-storage` |
| Log Analytics workspace | auto-named `workspace-ithelpdeskrg…` | Created with the environment |
| Container Apps Job | `wso2-seed-job` | **Leftover** one-shot helper (section 6, item 4). Costs nothing; safe to delete |
| Budgets | `budget-5`, `budget-15` | Subscription level; email alerts at 50/90/100% to `it24101497@my.sliit.lk` |

### Container Apps (all currently scaled to 1 replica = running)

| App | Ingress | URL / host |
|---|---|---|
| `it-helpdesk-frontend` | external | https://it-helpdesk-frontend.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io |
| `wso2-gateway` | external, port 8280 only | https://wso2-gateway.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io |
| `auth-service` | external | https://auth-service.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io |
| `ticket-service` | external | https://ticket-service.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io |
| `assignment-service` | external | https://assignment-service.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io |
| `sla-service` | **internal** | `sla-service.internal.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io` |
| `notification-service` | **internal** | `notification-service.internal.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io` |
| `kafka-broker` | internal TCP 9092 | `kafka-broker:9092` (single node, no persistence) |

Note the `.internal.` in the hostname of internal apps; without it the name
does not resolve.

Images currently deployed: tag **`manual-5c2601a`** for all six
(`auth-service`, `ticket-service`, `assignment-service`, `sla-service`,
`notification-service`, `frontend`), built by hand from the local checkout.

---

## 3. What works, what doesn't, what is untested

### Verified working (tested through the public URLs on 2026-10-11)

- Frontend serves (HTTP 200).
- `auth`, `ticket`, `assignment` `/health` → 200.
- `sla` and `notification` are healthy (checked through the gateway and
  Container Apps health state).
- Login with bad credentials → 401 `Invalid email or password.` (proves
  auth ↔ MySQL works and the auth migrations are applied).
- CORS preflight from the frontend origin is allowed on auth and the gateway.
- WSO2 gateway: all 5 APIs (`/auth/v1`, `/ticket/v1`, `/assignment/v1`,
  `/sla/v1`, `/notification/v1`) are **published and deployed**.
  `/<x>/v1/health` returns 200 for auth, ticket, assignment, sla,
  notification. `GET /ticket/v1/mine` without a token → 401 "Missing
  Credentials" (correct: WSO2 is protecting it).
- Admin login through the gateway works (user in section 9).

### NOT tested yet — do this first

- **The real user journey**: log in on the frontend, create a ticket, see it get
  assigned, see the SLA timer, receive the email. Only health endpoints and
  login were tested. Kafka messaging between services is therefore unproven
  end to end.
- **Email sending.** SMTP (Gmail app password) is configured on
  `notification-service` but no email has been sent. Gmail may reject it.
- Other roles (Agent, Employee) and the admin-only screens.

### Known problems / limitations

- **Kafka**: one broker, no volume. Restarting it **loses all topics and
  messages** (auto-create is on, so topics come back, but history is gone).
  Consumers logged one burst of disconnects (~11:30 UTC) right after first
  startup, then were stable. If services seem to "lose" events, check Kafka first.
- **WSO2 `admin/admin` was NOT changed.** Port 9443 is not exposed, so it is only
  reachable via `az containerapp exec`, which needs Azure access to this
  subscription. We tried SCIM2 and SOAP to change it; both are unavailable in
  this image. Decision (Vimukthi): leave it.
- **`POST /api/Auth/register` accepts a `role` field from anyone.** Anybody who
  finds the URL can create an Administrator. This is a bug in the auth service
  code (`UserRegisterDTO.Role`), not an infra problem. Fix it in code before
  any real use.
- **Admin password is `123456`** (user's choice; the app's minimum is 6 chars).
- **Frontend URLs are baked in at build time** (`frontend/.env.production`).
  If the environment domain ever changes you must edit that file and rebuild the
  frontend image.

---

## 4. Security summary (be honest in the report)

| Issue | Severity | Status |
|---|---|---|
| Public `/register` lets anyone pick `role: Administrator` | High (code bug) | **Open** |
| Admin password `123456` | Medium | Open (owner's choice) |
| WSO2 `admin/admin` | Low-medium (not internet-reachable) | Open (owner's choice) |
| Real secrets in `.local-notes/` on one laptop | Process risk | Share securely (section 9) |
| Gmail app password was pasted into a chat session | Medium | **Revoke and regenerate it after the sprint** |
| MySQL firewall allows "Azure services" (0.0.0.0) plus one personal IP | Low | Acceptable for a student project |

---

## 5. Which code is deployed (read before the sprint ends)

- Deployed from **`develop` at `5fe1673`** (includes the WSO2 startup-probe /
  single-replica fix).
- **`QA-Sprint-4` is NOT merged and NOT deployed.** It is 2 commits ahead of
  `develop` (bug 3, bug 6) and **needs new config** that the running apps do
  not have yet:
  - `InternalService__ApiKey` on **both** `auth-service` and
    `notification-service`, **same value on both** (otherwise 401 between
    services). A value was generated and sits in
    `.local-notes/AZURE_SECRETS.env` as `INTERNAL_API_KEY`, but it has **not
    been applied** to either app.
  - A new Kafka topic `ticket-resolved` →
    `Kafka__TicketResolvedTopic=ticket-resolved` on `ticket-service`.
- **Local branch `chore/azure-redeploy`** (branched from `origin/develop`) holds
  three commits, **not pushed**:
  1. CI workflows → registry `ithelpdeskacr02`
  2. WSO2 YAML → new subscription id, `centralindia`, env `it-helpdesk-envstd`
  3. `frontend/.env.production` → new domain
  Until this is merged to `develop`, `develop` on GitHub still points at the
  **old** registry and domain. Anyone building the frontend from plain `develop`
  gets a frontend that talks to dead URLs.
- Also uncommitted in the working tree: a `.gitignore` change (user's own),
  `docs/DEPLOYMENT_HANDOVER.md` (untracked), this file, and a couple of
  unrelated PDFs.

### When the current sprint is done — checklist

This is the dev branch of the last sprint; when the sprint closes:

1. Merge `QA-Sprint-4` (and any other sprint work) into `develop` via PR.
2. Merge `chore/azure-redeploy` into `develop` too (or cherry-pick its 3
   commits) so the config in git matches Azure.
3. Add the config listed above (`InternalService__ApiKey` on auth +
   notification, `Kafka__TicketResolvedTopic` on ticket). Example:
   ```bash
   az containerapp secret set -g it-helpdesk-rg -n auth-service --secrets internal-key='<value>'
   az containerapp update    -g it-helpdesk-rg -n auth-service --set-env-vars InternalService__ApiKey=secretref:internal-key
   # same two commands for notification-service, SAME value
   az containerapp update -g it-helpdesk-rg -n ticket-service --set-env-vars Kafka__TicketResolvedTopic=ticket-resolved
   ```
4. Rebuild and redeploy all images (section 8).
5. If the sprint changed DB schema: `assignment`, `sla`, `notification` apply
   EF migrations at startup; `ticket` uses `EnsureCreated`. **`auth` does NOT
   migrate at startup** — run `dotnet ef database update` yourself (section 8.4).
6. Re-run the smoke tests (section 8.6).
7. **Scale everything down** (section 7) and, when the course no longer needs
   it, delete the resource group.

---

## 6. Where reality differed from `DEPLOYMENT_HANDOVER.md`

Read this before following the old doc, or you will repeat our mistakes.

1. **Region**: `southeastasia` is policy-blocked → `centralindia` (MySQL in
   `indonesiacentral`). The `location:` line in `infra/wso2/azure/*.yaml` is now
   `centralindia`.
2. **Express environment trap**: the installed `az containerapp` extension
   (1.3.0b5) creates an **"express" environment by default**, which does **not
   support Azure Files mounts** (WSO2 needs one). Create the environment with
   `--environment-mode WorkloadProfiles`. We made an express one first and
   deleted it.
3. **WSO2 does not create its own databases on first boot.** The doc says WSO2
   creates `database/` and `synapse-configs/` itself. Wrong: the H2 database
   files **ship inside the image**, and mounting an empty share over
   `repository/database` hides them → boot fails with
   `Table "UM_DOMAIN" not found (this database is empty)`. We fixed it by
   copying the image's real `database/` and `synapse-configs/` onto the share
   with a one-shot Container Apps Job (`wso2-seed-job`). The share is now seeded;
   you do not need to redo this unless the share is wiped. If you must, the job
   is the pattern to copy.
4. **ACR cloud builds are blocked** on this subscription (`TasksOperationsNotAllowed`).
   `az acr build` fails. You must **build images locally with Docker Desktop** and
   push. (Docker must be running.)
5. **`az containerapp update` is dangerous for `wso2-gateway`.** Every
   update creates a *new revision*; WSO2 takes minutes to boot, so the old
   revision keeps running while the new one starts → **two replicas fight over
   the same H2 file → `The file is locked`**. This is exactly what hurt the old
   deployment. For WSO2 use **`revision activate` / `revision deactivate`**
   (section 7), and keep exactly one revision active.
6. **`az containerapp replica list` and `logs show` default to the *latest*
   revision**, which may be an inactive one. They can show "0 replicas" while an
   older revision is happily running and costing money. Always pass `--revision`
   or use `az containerapp revision list` to see the real state.
7. **WSO2 boot time** was ~1–2 minutes on this run, not the 5–11 minutes in the
   old doc (the startup probe still allows up to ~11).
8. **Publishing the APIs**: `deploy-apis.sh` needs `jq`, which the container
   lacks. We staged the specs + a `curl`-only script on the Azure Files share and
   ran it with `az containerapp exec`. The API definitions were adjusted for
   Azure: backend URLs point to the real service hosts, public operations get
   `x-auth-type: None`, policy `Unlimited`. This is already done and persisted in
   the share's H2 database; you only redo it if the share is wiped.
9. **SLA and notification backends use the `.internal.` hostname** in WSO2.
10. **Git Bash on Windows rewrites `/subscriptions/...` paths** and breaks `az`
    commands. Prefix with `export MSYS_NO_PATHCONV=1` or use PowerShell.
11. **Your public IP changes**, and the MySQL firewall rule `me` holds only one
    IP. If a DB connection times out from your laptop, update the rule (8.4).

---

## 7. COST — this is costing money right now

**Everything is scaled to 1 replica and running 24/7.** Container Apps bill for
the vCPU/memory of every running replica, MySQL bills for every hour it is on,
and the registry/storage/logs bill all month regardless. The previous account
lost ~$40 in a few days this way. **Treat "left running" as an emergency.**

Rough picture (estimates only, not read from the Azure bill — check Cost
Management for real numbers):

| Item | Cost behaviour |
|---|---|
| 8 running Container Apps (WSO2 is the biggest: 1 vCPU / 2 GiB) | Billed per second while a replica exists. **This is the main burn.** Scale to 0 when idle. |
| MySQL B1ms | Billed per hour while running (~$12–15/month if left on). Can be **stopped**; **auto-restarts after 7 days** |
| Container Registry (Basic) | ~$5/month, always |
| Storage account + Log Analytics | Small, always |

Budget alerts exist ($5 and $15, emails to `it24101497@my.sliit.lk`) but they
only **warn**; they do not stop anything.

> **Use the scripts instead of the manual commands below:** `scripts/stop-all` and `scripts/start-all`
> (PowerShell `.ps1` and bash `.sh`). They follow all the rules in this section, check the result, and
> `scripts/README.md` also explains how to do it by hand in the Azure portal. The commands below are kept
> as the reference for what the scripts do.

### Stop everything (do this after every session)

```bash
export MSYS_NO_PATHCONV=1   # Git Bash only
RG=it-helpdesk-rg

# 1. normal apps: scale to zero
for a in it-helpdesk-frontend notification-service sla-service assignment-service \
         ticket-service auth-service kafka-broker; do
  az containerapp update -g $RG -n $a --min-replicas 0
done

# 2. WSO2: DEACTIVATE the active revision (do NOT use 'update' for it)
az containerapp revision list -g $RG -n wso2-gateway --query "[?properties.active].name" -o tsv
az containerapp revision deactivate -g $RG -n wso2-gateway --revision <name-from-above>
# currently active: wso2-gateway--0000003

# 3. MySQL
az mysql flexible-server stop -g $RG -n it-helpdesk-mysql02
```

### Verify it actually stopped (mandatory — "success" messages lie)

```bash
for a in $(az containerapp list -g $RG --query "[].name" -o tsv); do
  echo "== $a"
  az containerapp revision list -g $RG -n $a --query "[?properties.active].{rev:name,replicas:properties.replicas}" -o table
done
# every active revision must show replicas 0 (or no active revision for wso2-gateway)
az mysql flexible-server show -g $RG -n it-helpdesk-mysql02 --query state -o tsv   # Stopped
```
Then open Portal → Cost Management → Cost analysis and check the next day.

### Start again for a demo

```bash
az mysql flexible-server start -g $RG -n it-helpdesk-mysql02
az containerapp update -g $RG -n kafka-broker --min-replicas 1      # Kafka first
for a in auth-service ticket-service assignment-service sla-service notification-service it-helpdesk-frontend; do
  az containerapp update -g $RG -n $a --min-replicas 1
done
# WSO2: activate exactly ONE revision. wait ~1 min after a deactivate first (file lock).
az containerapp revision activate -g $RG -n wso2-gateway --revision wso2-gateway--0000003
```
Revision `0000003` is the one configured min 1 / max 1. `0000001`…`0000004` and
`vreh0ud` are old revisions, all inactive; **never have two active at once.**
If you see `The file is locked` in WSO2 logs: deactivate it, wait a couple of
minutes, activate one revision again.

### Permanent shutdown
`az group delete -n it-helpdesk-rg` removes everything inside. (Budgets live at
subscription level and remain.)

---

## 8. How to operate it

### 8.1 Log in / tools
`az login` as `it24101497@my.sliit.lk`, or as a user who has **Contributor on
`it-helpdesk-rg`** (access for `it24101503@my.sliit.lk` was being granted by the
subscription owner — if you cannot see the resource group, ask them). You need
Azure CLI with the `containerapp` extension, Docker Desktop **running**, `git`,
and the .NET 8 SDK + `dotnet-ef` for migrations.

### 8.2 Build and push images (manual deploy)
```bash
az acr login -n ithelpdeskacr02
TAG=manual-$(git rev-parse --short HEAD)
for s in auth:Auth.Api:auth-service ticket:Ticket.Api:ticket-service \
         assignment:Assignment.Api:assignment-service \
         sla:Sla.Api:sla-service notification:Notification.Api:notification-service; do
  IFS=: read dir proj img <<< "$s"
  docker build -t ithelpdeskacr02.azurecr.io/$img:$TAG services/$dir/$proj
  docker push  ithelpdeskacr02.azurecr.io/$img:$TAG
done
docker build -t ithelpdeskacr02.azurecr.io/frontend:$TAG frontend
docker push  ithelpdeskacr02.azurecr.io/frontend:$TAG
```
Large uploads are slow on a home connection; run in the background.

### 8.3 Roll an app to a new image
```bash
az containerapp update -g it-helpdesk-rg -n auth-service \
  --image ithelpdeskacr02.azurecr.io/auth-service:$TAG
```
Fine for every app **except `wso2-gateway`** (public image, never needs this).

### 8.4 Database
- Host `it-helpdesk-mysql02.mysql.database.azure.com`, port 3306, user
  `helpdeskadmin`, TLS required (`SslMode=Required`). Password: section 9.
- Allow your IP:
  ```bash
  az mysql flexible-server firewall-rule update -g it-helpdesk-rg -n it-helpdesk-mysql02 \
    --rule-name me --start-ip-address <your-ip> --end-ip-address <your-ip>
  # or create a rule with another name for yourself
  ```
- Auth migrations (needed after any auth schema change). The auth app builds its
  DB connection at startup, so supply the real connection string via env var:
  ```bash
  cd services/auth/Auth.Api
  export ConnectionStrings__AuthDb="Server=it-helpdesk-mysql02.mysql.database.azure.com;Port=3306;Database=auth_db;Uid=helpdeskadmin;Pwd=<password>;SslMode=Required;"
  export AssignmentApi__BaseUrl=https://x.invalid cors__FrontendUrl=https://x.invalid
  export Jwt__Key=<jwt key> Jwt__Issuer=AuthApi Jwt__Audience=ItHelpdeskClient
  dotnet ef database update
  ```
  Currently applied: `InitialCreate`, `AddCreatedByToUsers`.

### 8.5 Config reference
Per-app environment variables and secrets are exactly the matrix in
`DEPLOYMENT_HANDOVER.md` section 2.4, with these actual values:
- Domain: `proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io`
- Passwords/keys are stored as Container App **secrets** (`secretref:`), not
  plain env vars.
- Read an app's current config: `az containerapp show -g it-helpdesk-rg -n <app> --query properties.template.containers[0].env`
- Read a secret's value (needs access): `az containerapp secret list -g it-helpdesk-rg -n <app> --show-values`

### 8.6 Smoke tests
```bash
D=proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io
for a in auth ticket assignment; do curl -s -o /dev/null -w "$a %{http_code}\n" https://$a-service.$D/health; done
G=https://wso2-gateway.$D
curl -s $G/sla/v1/health; curl -s $G/notification/v1/health        # 200 healthy
curl -s -X POST $G/auth/v1/login -H 'Content-Type: application/json' \
     -d '{"email":"x@y.z","password":"bad"}'                          # 401
curl -s $G/ticket/v1/mine                                             # 401 Missing Credentials
```
WSO2 may answer 404 for a few seconds on a route right after (re)deploy; retry.

### 8.7 Re-publishing WSO2 APIs (only if the share is wiped)
Not needed normally. See section 6 item 8 for the method.
Source specs: `infra/wso2/apis/*.json`.

---

## 9. Credentials and where they live

| What | Where |
|---|---|
| MySQL password, JWT key, internal API key, env name/domain | `.local-notes/AZURE_SECRETS.env` on Vimukthi's laptop (gitignored) |
| SMTP (Gmail) settings | `.local-notes/SMTP_CREDENTIALS.md` on the same laptop (gitignored) |
| First admin user | email `vimukthiherath123@gmail.com`; password given verbally by Vimukthi (not written here on purpose) |
| WSO2 admin | still `admin` / `admin` |

**Do not copy any of these into a tracked file.** `.local-notes/` is gitignored.
Ask Vimukthi to share them over a private channel.

If the local files are lost: the MySQL password can be reset with
`az mysql flexible-server update -g it-helpdesk-rg -n it-helpdesk-mysql02 -p '<new>'`
(then update the `db` secret in each service); the JWT key and SMTP values can
be read back from the Container App secrets (`--show-values`), but the JWT key
must stay **identical on auth, ticket and assignment** or tokens stop working.

---

## 10. Next steps, in priority order

1. **Test the real flow** on the frontend (login → ticket → assignment → SLA →
   email). Write down anything that fails. (Section 3.)
2. **Decide about CI/CD.** Not set up. To enable it, someone with rights on the
   subscription must:
   - create the identity `github-actions-identity` + a federated credential for
     repo `VimukthiHerath/it-helpdesk-platform`, branch `develop`;
   - give it `Contributor` on `it-helpdesk-rg` and `AcrPush` on the registry;
   - add GitHub repo secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
     `AZURE_SUBSCRIPTION_ID`;
   - merge `chore/azure-redeploy` into `develop` first, otherwise the workflows
     still target the old registry.
   Exact commands are in `DEPLOYMENT_HANDOVER.md` section 3.10 (use the new
   registry name and subscription id). Until then, deploy by hand (8.2–8.3).
3. **Fix the `/register` role bug** in the auth service, and consider a stronger
   admin password.
4. **Merge the sprint work** and apply the section 5 checklist.
5. **Scale down** after each session (section 7) and verify.
6. Optional tidy-up: delete the `wso2-seed-job` Container Apps Job; correct
   `DEPLOYMENT_HANDOVER.md` with the section 6 findings; rotate the Gmail app
   password.

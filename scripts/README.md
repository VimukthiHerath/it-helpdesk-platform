# Start and stop the platform on Azure

The platform runs on Azure Container Apps and a MySQL server. **Everything that is running
costs money**, so the rule is simple: start it when you need it, stop it when you are done.
These scripts do both safely, and prove the result.

| File | Use it from | What it does |
|---|---|---|
| `start-all.ps1` | Windows PowerShell | Starts MySQL, Kafka, the services, the frontend and the WSO2 gateway, then waits until everything answers |
| `stop-all.ps1` | Windows PowerShell | Scales everything to zero, switches the gateway off, **checks** that nothing is left running, stops MySQL |
| `start-all.sh` | Git Bash, Linux, macOS, **Azure Cloud Shell** | Same as `start-all.ps1` |
| `stop-all.sh` | Git Bash, Linux, macOS, **Azure Cloud Shell** | Same as `stop-all.ps1` |
| `publish-wso2-apis.ps1` / `.sh` | Windows / bash | Publishes the five APIs into the WSO2 gateway, locally or on Azure (section 8) |
| `lib/wso2-publish-core.sh` | (used by the two above) | The publishing steps themselves; runs *inside* the WSO2 container |

The `.ps1` and `.sh` versions do exactly the same thing; use whichever fits where you are.

---

## 1. Before you start

1. **Azure CLI** installed (<https://aka.ms/installazurecli>). The scripts install the
   `containerapp` extension themselves if it is missing.
2. **Logged in:** `az login`, with an account that has access to the resource group
   `it-helpdesk-rg` (Contributor or higher) in the subscription `Azure for Students`
   (`7e76da9f-fdb8-43bd-9551-28bc90afcd2f`). The scripts select that subscription for you.
3. **Azure Cloud Shell** (portal, the `>_` icon, choose *Bash*) already has the CLI and is already
   logged in. Upload or `git clone` the repository there and run the `.sh` scripts.
4. Run the scripts **from the repository root**.

---

## 2. Quick start

**Windows PowerShell**

```powershell
.\scripts\start-all.ps1          # bring everything up (about 6-8 minutes)
# ... use the system ...
.\scripts\stop-all.ps1           # shut everything down and verify (about 8-12 minutes)
```

If PowerShell refuses to run scripts ("running scripts is disabled on this system"), run them like this instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-all.ps1
```

**Git Bash / Linux / macOS / Cloud Shell**

```bash
./scripts/start-all.sh
./scripts/stop-all.sh
```

(If you get "permission denied": `bash scripts/start-all.sh`, or `chmod +x scripts/*.sh` once.)

When `start-all` finishes it prints the **frontend** and **gateway** addresses and a table showing every
app's replica count and state. When `stop-all` finishes with *Done*, it has seen **zero replicas**
in every app.

### Options

| PowerShell | bash | Meaning |
|---|---|---|
| `-DryRun` | `--dry-run` | Show what would be done and change nothing. Good for a first look. |
| `-NoWait` | `--no-wait` | Send the commands and return immediately, without waiting or checking |
| `-KeepMySql` (stop only) | `--keep-mysql` | Leave the MySQL server running (it keeps billing about $12-15 a month) |
| `-TimeoutMinutes 20` | `HELPDESK_TIMEOUT_MIN=20` | How long to wait for health / for replicas to disappear (default 15) |
| `-NudgeMinutes 4` (stop only) | `HELPDESK_NUDGE_MIN=4` | After this long, an app that will not scale down by itself is deactivated (default 4) |
| `-ResourceGroup`, `-Subscription`, `-MySql`, `-Environment` | `HELPDESK_RG`, `HELPDESK_SUBSCRIPTION_ID`, `HELPDESK_MYSQL`, `HELPDESK_ENV` | Use other names if the deployment is ever recreated (the defaults are this project's) |

**Exit codes:** `0` success, `1` something did not come up / is still running (the message says what),
`2` the script could not start (not logged in, wrong subscription, resource group not found).

---

## 3. What the scripts do (and why)

### start-all

1. **Checks your setup:** Azure CLI present, you are logged in, the subscription can be selected, the
   resource group exists, and finds the environment's address itself (nothing is hardcoded).
2. **MySQL:** starts the server if it is stopped and waits until it is `Ready`. Everything else needs it.
3. **Kafka**, then the **five services** (auth, assignment, ticket, sla, notification) and the
   **frontend:** sets each to "minimum replicas 1". An app already set that way is left alone.
4. **WSO2 gateway:** activates **exactly one** revision (see the safety rules below).
5. **Waits and checks** (up to 15 minutes) until these all return HTTP 200: the gateway itself, the
   health route of every service **through the gateway** (so the gateway's routes are tested too), and
   the frontend.

### stop-all

1. **Checks your setup** (as above).
2. Sets every container app to "minimum replicas 0".
3. **Deactivates** the WSO2 gateway's revision.
4. **Checks** every few seconds until no app has a running replica. Azure removes replicas some minutes
   after a scale-to-zero request (Kafka is the slowest, up to about 5 minutes after the services
   disconnect). If an app has not gone down after 4 minutes, the script deactivates its
   revision, and it only reports success after it has *seen* zero replicas.
5. **MySQL:** the stop is requested at the very start (Azure needs a few minutes for it, so it runs while the
   apps drain) and confirmed `Stopped` at the end.

**Measured timings:** start about 6 minutes (real run, everything answering 200 including the gateway's routes); stop about
8-12 minutes, almost all of it Azure removing replicas and stopping MySQL. The scripts poll, so they are not frozen.

Both scripts are **safe to run twice** and safe to interrupt (Ctrl+C): run them again to finish the job.

---

## 4. Safety rules (these caused the old deployment's problems)

1. **Never change the `wso2-gateway` app with `az containerapp update`, "Edit and deploy", or
   "Create new revision" in the portal.** Each of those creates a *new revision*. The new one starts
   (it takes a minute or two) while the old one is still running, so two copies run at the same time and
   both try to open the gateway's single database file. That produces `The file is locked` errors and a
   crash loop. The scripts only ever **activate** or **deactivate** a revision.
2. **Only one gateway revision may be active.** `start-all` fixes this itself if it finds two.
3. After the gateway has been switched off, wait about **two minutes** before switching it on again so
   its file lock is released. `start-all` does this wait for you.
4. **Do not trust "success" messages from the Azure CLI or portal**; check the replica count. That is what
   `stop-all` does.
5. **MySQL restarts by itself after 7 days** when stopped. If you stop it for a long period, run
   `stop-all` again after a week.

---

## 5. Troubleshooting

| What you see | Why | What to do |
|---|---|---|
| `ERROR: you are not logged in to Azure` | No `az login` yet, or the login expired | `az login`, then run the script again |
| `ERROR: cannot select subscription ...` | The login has no access to the project's subscription | Log in with the right account, or ask the owner (it24101497@my.sliit.lk) for Contributor on `it-helpdesk-rg` |
| `running scripts is disabled on this system` (PowerShell) | Execution policy | `powershell -ExecutionPolicy Bypass -File .\scripts\start-all.ps1` |
| `ERROR: resource group 'it-helpdesk-rg' not found` | Wrong subscription, or the project was deleted/recreated under another name | Check `az account show`; use `HELPDESK_RG` / `-ResourceGroup` |
| `no revision of wso2-gateway is configured min=1 / max=1` | The gateway's revisions were changed or the app was recreated | In the portal open `wso2-gateway`, Revisions, and make sure one revision has scale min 1, max 1 (section 6) |
| Start is stuck on `[gateway=404]` for a minute or two | WSO2 takes 1-2 minutes to boot and load its API definitions | Wait; the script keeps checking |
| Start ends with `[auth (via gateway)=404]` or similar and never recovers | The gateway is up but the API definitions are missing (the gateway's database was reset) | The APIs must be published again; see `docs/WSO2_API_GATEWAY.md` section 7 and `docs/AZURE_REDEPLOY_HANDOFF.md` |
| Gateway log says `The file is locked` | Two gateway revisions ran together, or it was restarted too quickly | `stop-all`, wait 2 minutes, `start-all` |
| `still running after 15 minutes: kafka-broker=1` | Something is still connected to Kafka | The script has already tried deactivating it; run `stop-all` again, or deactivate the revision in the portal (section 6) |
| Replica counts look wrong in the portal's app list | The list shows the *latest* revision, which may be an inactive one | Open the app, Revisions and replicas, and look at the revision marked *Active* |
| An app shows `Failed` / `Degraded` | Usually missing configuration or a bad image after a deploy | Open the app's *Log stream*, and see `docs/AZURE_REDEPLOY_HANDOFF.md` |
| Cost looks higher than expected | Something is still running, or MySQL was left on | Run `stop-all`; check *Cost Management > Cost analysis* the next day |

---

## 6. Doing the same by hand in the Azure portal

Use this if you do not have the CLI, or a script cannot do something. The result is the same.

Sign in at <https://portal.azure.com> with an account that has access, make sure the directory/subscription
**Azure for Students** is selected (top right, *Directories + subscriptions*), search for **Resource
groups** and open **`it-helpdesk-rg`**. Everything below is inside it. Menu names change slightly between
portal versions; the idea is the same.

### 6.1 Start everything (in this order)

1. **MySQL.** Open **`it-helpdesk-mysql02`** (Azure Database for MySQL flexible server) -> **Overview** ->
   **Start**. Wait until the status is **Available / Ready** (2-4 minutes).
2. **Kafka.** Open **`kafka-broker`** (Container App) -> **Application** -> **Revisions and replicas**
   (or *Scale and replicas*) -> **Create new revision** (or *Edit and deploy*) -> **Scale** tab -> set
   **Min replicas = 1** -> **Create**. Wait until the revision shows replicas **1** and *Running*.
3. **Services, one after the other, then the frontend:** `auth-service`, `assignment-service`,
   `ticket-service`, `sla-service`, `notification-service`, `it-helpdesk-frontend`. For each one do the same as
   for Kafka: **Min replicas = 1**.
4. **WSO2 gateway.** Open **`wso2-gateway`** -> **Revisions and replicas**.
   * **Do not** use *Create new revision* or *Edit and deploy* here (safety rule 1).
   * Find the revision that is scaled **min 1 / max 1** (currently **`wso2-gateway--0000003`**; click a
     revision name to see its scale settings). Make sure **only that revision** is active.
   * If it is inactive, tick it and press **Activate**. If an *older* revision is active, **Deactivate** that one
     first, wait two minutes, then activate the right one.
5. **Check it works.** Open `https://wso2-gateway.<environment domain>/auth/v1/health` in a browser (the
   domain is on any app's **Overview** page, *Application Url*). It should show `{"status":"healthy",...}`.
   Then open the frontend's **Application Url**.

### 6.2 Stop everything (in this order)

1. **Frontend and each service** (`it-helpdesk-frontend`, `notification-service`, `sla-service`,
   `ticket-service`, `assignment-service`, `auth-service`): *Revisions and replicas* -> create a new
   revision with **Min replicas = 0**. (The services use up to 1 replica, so zero is allowed.)
2. **`kafka-broker`:** the same, **Min replicas = 0**.
3. **WSO2 gateway:** *Revisions and replicas* -> tick the **active** revision -> **Deactivate**.
4. **Wait about 5 minutes, then check.** For every app: *Revisions and replicas* -> the active revision must
   show **0** replicas. (The portal's app list can lag; this page is the truth.) If an app still has
   replicas after about 10 minutes, **Deactivate** its active revision here as well. Kafka most often needs this.
5. **MySQL:** **`it-helpdesk-mysql02`** -> **Overview** -> **Stop**. Azure turns it back on after 7 days, so
   remember to stop it again if it is still not needed.
6. The next day, look at **Cost Management -> Cost analysis** to confirm costs have stopped growing.

### 6.3 One-line CLI equivalents (Azure Cloud Shell)

```bash
RG=it-helpdesk-rg
az containerapp update -g $RG -n auth-service --min-replicas 1        # start one app (repeat per app)
az containerapp update -g $RG -n auth-service --min-replicas 0        # stop one app
az containerapp revision list -g $RG -n wso2-gateway -o table          # see the gateway's revisions
az containerapp revision activate   -g $RG -n wso2-gateway --revision wso2-gateway--0000003
az containerapp revision deactivate -g $RG -n wso2-gateway --revision wso2-gateway--0000003
az mysql flexible-server start -g $RG -n it-helpdesk-mysql02
az mysql flexible-server stop  -g $RG -n it-helpdesk-mysql02
```

(Never run `az containerapp update` on `wso2-gateway`.)

---

## 7. What costs money

| Item | While running | While "stopped" |
|---|---|---|
| Container apps and the WSO2 gateway (the big one) | billed per second per replica | **nothing** at zero replicas |
| MySQL server | about $12-15 a month, billed per hour | nothing while stopped |
| Container Registry (Basic) | about $5 a month | same (always on) |
| Storage account, Log Analytics | a few cents | a few cents |

Budget alerts exist ($5 and $15, emailed to the account owner) but they only warn; they do not stop anything.

---

## 8. Publishing the APIs into the WSO2 gateway

The gateway only routes a path once its API has been **published and deployed** in WSO2. A fresh local
gateway has none, and an Azure gateway loses them if its database is reset. `publish-wso2-apis` does it
for all five APIs (`infra/wso2/apis/*.json`):

```powershell
# Local: after  docker compose up -d
.\scripts\publish-wso2-apis.ps1                # same as:  .\scripts\publish-wso2-apis.ps1 local
./scripts/publish-wso2-apis.sh                  # bash

# Azure: the gateway must be running (start-all first)
.\scripts\publish-wso2-apis.ps1 azure
./scripts/publish-wso2-apis.sh azure
```

| Option | Meaning |
|---|---|
| `local` (default) | The WSO2 container from docker-compose (`local-wso2-gateway`). The APIs point at the services on your machine (auth 5121, ticket 5164, assignment 5067, sla 5262, notification 5214) |
| `azure` | The deployed gateway. Its management port is never exposed, so the script stages the files on the gateway's Azure Files share and runs the publishing steps **inside** the container with `az containerapp exec`. The APIs point at the Azure services |
| `-DryRun` / `--dry-run` | Show the plan, change nothing |
| `-CheckBackends` / `--check-backends` | Also call each service's `/health` through the gateway. **On Azure this wakes every service that is scaled to zero** (and needs MySQL running), so it is off by default |

What it does: waits for WSO2, gets a token, **deletes any existing copy of these five APIs**, then for each API
imports it, publishes it, creates a revision and deploys it. Finally it asks the gateway whether each route
exists (a CORS preflight, which the gateway answers itself without touching any service).

Good to know:

* It is **safe to run again**; the old copies are replaced. Run it after editing any `infra/wso2/apis/*.json`.
* Every operation is published with `x-auth-type: None`: the **gateway only routes** and each service checks the
  JWT itself (`docs/ARCHITECTURE_AND_END_TO_END_FLOW.md`, section 3.4). That marker lives in the API files.
* The admin login is `admin` / `admin` unless you set `WSO2_ADMIN_USER` / `WSO2_ADMIN_PASSWORD`.
* **Local needs Docker; Azure needs the Azure CLI and Contributor access** (it reads the storage key to stage the files).
* If the route check shows 404 for a route straight after publishing, wait a minute and run the check again;
  WSO2 loads deployed APIs a few seconds after the deployment call returns.

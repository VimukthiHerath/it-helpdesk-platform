# Monitoring (Azure Application Insights)

The five .NET services send traces, requests, dependency calls, exceptions and logs to one
Application Insights resource through OpenTelemetry (`Azure.Monitor.OpenTelemetry.AspNetCore`).

## In plain words

The helpdesk is five small services that talk to each other, some of them through Kafka messages. When something goes wrong,
the problem can be several steps away from what the user sees. Monitoring answers three questions without reading logs by hand:

1. **What is happening now?** The dashboard and Application map.
2. **What happened to this one ticket?** A single trace that follows it through all the services.
3. **Tell me when it breaks.** Email alerts.

The tool is **Azure Application Insights**. Each service and the website send small reports to it. We never run a monitoring server.

## How we got here, step by step

1. **Created the place to store the data.** One Application Insights resource, `ithelpdesk-appinsights`, attached to the log workspace we already had. A daily limit of 0.5 GB stops it ever costing much.
2. **Taught each service to report.** We added one library (OpenTelemetry for Azure) to the five services. It records every request, every call to another service or the database, errors and log messages. It ignores the `/health` checks, and it stays off on a developer laptop.
3. **Connected the services to the resource.** Each running app got the resource's connection string as a secret, plus its own name (for example `ticket-service`) so it shows up by name.
4. **Made one ticket show up as one story.** Messages through Kafka lose the trail by default. We attach the trail to every Kafka message, and the receiving service continues it. Now creating a ticket shows as one connected trace across Ticket, SLA, Assignment and Notification.
5. **Proved it with a real flow.** We registered users, created a ticket and watched it appear in Application Insights from all five services.
6. **Built the dashboard.** A workbook called "IT Helpdesk health": requests, errors and speed per service, the slowest endpoints, Kafka messages handled and the most common errors.
7. **Added email alerts.** Four rules email the team: server errors, a burst of errors in any service, any failure in the notification service, and slow responses. Each checks every 15 minutes. There is deliberately no "service is down" alert, because the apps scale to zero when idle.
8. **Added browser monitoring.** The website reports page views and JavaScript errors too, with the privacy rules below. It is switched on by a GitHub secret at build time.

Everything is repeatable from the repo: `scripts/deploy-monitoring.sh` recreates the dashboard and alerts.

## How to demo it (about 5 minutes)

Before you start: run `./scripts/start-all.sh`, wait for "Everything is now running", and sign in to the Azure portal.

1. **Say what it is (30 seconds).** "Five services plus Kafka. Application Insights shows what each one does and how a ticket moves between them."
2. **Make some traffic.** Open the live site, log in as an employee, create a ticket, then log in as an admin and open All tickets. Wait 2 to 3 minutes for the data to arrive.
3. **Application map.** Portal > `ithelpdesk-appinsights` > **Application map**. Point out the five services and the arrows between them, with call counts and average times.
4. **One ticket end to end.** **Transaction search** > search "POST" > open the ticket creation request. The end-to-end view shows the web request, then the Kafka messages handled by SLA, Assignment and Notification, then the assignment coming back. This is the main point to show.
5. **The dashboard.** **Workbooks** > **IT Helpdesk health**. Show the service summary table, the requests chart and the Kafka messages table.
6. **Ask it a question.** **Logs** > run `requests | summarize count() by cloud_RoleName`, then `exceptions | take 10`.
7. **The website side.** **Usage** > **Page views** (or Logs: `pageViews | summarize count() by name`). This shows which pages were opened, and `exceptions | where client_Type == "Browser"` shows browser errors.
8. **Alerts.** **Alerts** > **Alert rules**. Show the four rules and the email group. Say the rules email four team addresses, and nothing fires while the system is healthy.

When you are done: `./scripts/stop-all.sh` so nothing keeps costing money.

## How it is wired

- Resource: `ithelpdesk-appinsights` (centralindia, resource group `it-helpdesk-rg`), workspace-based,
  stored in the existing Log Analytics workspace `workspace-ithelpdeskrgMgIe`.
- Daily ingestion cap: 0.5 GB (the free allowance is 5 GB per month).
- Each service reads `APPLICATIONINSIGHTS_CONNECTION_STRING`. If it is not set, nothing is sent,
  so local runs and tests are unaffected. `OTEL_SERVICE_NAME` (for example `ticket-service`) names the service in the app map.
- `/health` requests are filtered out so probes do not cost ingestion.
- `APPINSIGHTS_SAMPLING_RATIO` (default 1.0) lowers the share of requests kept if volume grows.

## Setting it on a Container App

```
CS=$(az monitor app-insights component show -a ithelpdesk-appinsights -g it-helpdesk-rg --query connectionString -o tsv)
az containerapp secret set -n ticket-service -g it-helpdesk-rg --secrets "appinsights-cs=$CS"
az containerapp update -n ticket-service -g it-helpdesk-rg --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appinsights-cs" "OTEL_SERVICE_NAME=ticket-service"
```

Repeat for auth, assignment, sla and notification. The CI deploy only changes the image, so these settings stay.
Never put the connection string in git.

## Following one ticket across services (Kafka)

Producers add a `traceparent` header to each Kafka message and consumers continue that trace
(`Observability/KafkaTracing.cs` in each service, registered in `Program.cs`). One ticket creation therefore shows as a
single trace: the HTTP request, then `ticket-created process` in SLA, Assignment and Notification, then
`ticket-assigned process` in Ticket and Notification. Checked on Azure on 2026-10-13.

## Where to look in the portal

Azure portal > Resource groups > `it-helpdesk-rg` > `ithelpdesk-appinsights`:

- **Application map**: the services and the calls between them.
- **Transaction search**: individual requests and exceptions. Open one for the end-to-end view.
- **Failures** and **Performance**: errors and slow endpoints.
- **Logs**: KQL queries, for example `requests | where toint(resultCode) >= 500`.
- **Live Metrics**: real time while the apps are running.

Nothing is sent while the apps are scaled to zero.

## Dashboard and alerts

Created by `./scripts/deploy-monitoring.sh` (add `--dry-run` to only validate). It deploys
`infra/monitoring/monitoring.json` and `infra/monitoring/workbook.json`, and can be run again safely.

- **Dashboard:** Azure portal > `ithelpdesk-appinsights` > Workbooks > **IT Helpdesk health**. It shows a per-service summary
  (requests, 5xx, 4xx, average and 95th percentile time), requests and errors over time, slowest endpoints,
  Kafka messages handled per topic, the most frequent exceptions and recent error logs.
- **Alerts** go by email to the action group `ithelpdesk-alerts` (four team addresses). Each rule is checked every 15 minutes:

| Rule | Fires when | Severity |
|---|---|---|
| Server errors (HTTP 5xx) | 3 or more 5xx responses in the window | 2 |
| Errors and exceptions in any service | 5 or more error logs or exceptions | 2 |
| Notification service failed to process a message | any error logged by notification-service | 1 |
| Slow responses | 95th percentile above 5 s with at least 10 requests | 3 |

- The harmless `Unknown topic or partition` Kafka message that appears while the stack starts is ignored by the rules and the dashboard.
- There is deliberately no "service down" alert: the apps scale to zero, so being off is normal.
- Log alert rules are billed per rule per month, and a shorter check interval costs more. To change the interval or addresses, edit
  `alertFrequency` in the template or pass `--emails "a@x.lk,b@x.lk"`.
- To remove the alerts: delete the four rules and the action group in the portal (Monitor > Alerts), or delete the deployment's resources.

## Browser monitoring (frontend)

The React app uses the Application Insights JavaScript SDK (`frontend/src/shared/telemetry.js`). It records page views
(route changes included), JavaScript errors and the outcome of calls to the gateway.

- **Switched on at build time** by `REACT_APP_APPINSIGHTS_CONNECTION_STRING`. The CI build passes it from the GitHub secret
  `APPINSIGHTS_CONNECTION_STRING` through a Docker build argument (`frontend/Dockerfile`). With no secret, the SDK does nothing.
  A browser connection string can only send data, not read it, but it is still kept out of git.
  **One-time step:** GitHub > Settings > Secrets and variables > Actions > New repository secret, name `APPINSIGHTS_CONNECTION_STRING`,
  value from `az monitor app-insights component show -a ithelpdesk-appinsights -g it-helpdesk-rg --query connectionString -o tsv`.
- **Privacy:** page views keep only the path (no query string or fragment); anything that looks like `token=...` is blanked
  (password reset links); no cookies are used; the only extra detail attached is the role (for example `Agent`), never a name or email.
- **No browser-to-backend trace link, on purpose.** The gateway's CORS list (`Authorization, Content-Type, SOAPAction, apikey, Internal-Key`)
  does not allow the `traceparent` header, so a browser sending it would fail every call at the preflight check.
  The SDK therefore runs with `enableCorsCorrelation: false`. To link the two later, the gateway's allowed headers must be extended first.
- Where to look: `ithelpdesk-appinsights` > Usage > Page views, Failures > Browser, and Logs (`pageViews`, `browserTimings`, `exceptions`).
- Cost: the SDK adds roughly 60 kB (gzipped) to the app bundle.

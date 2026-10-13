# Monitoring (Azure Application Insights)

The five .NET services send traces, requests, dependency calls, exceptions and logs to one
Application Insights resource through OpenTelemetry (`Azure.Monitor.OpenTelemetry.AspNetCore`).

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

## Not done yet

- Frontend JavaScript SDK. It needs the gateway to allow the `traceparent` header in CORS before browser-to-backend linking is switched on.

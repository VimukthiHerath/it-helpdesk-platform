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

## Not done yet

- (done in code) Kafka trace propagation: producers add a `traceparent` header and consumers continue the trace (`Observability/KafkaTracing.cs` in each service).
- Frontend JavaScript SDK.
- Dashboard and alerts.

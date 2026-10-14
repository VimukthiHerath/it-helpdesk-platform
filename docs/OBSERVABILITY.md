# Prometheus and Grafana observability

The API Gateway and all five backend services expose Prometheus-compatible
metrics at `/metrics`. The `prometheus-net.AspNetCore` middleware records
request count, response code, and request duration without changing the
authentication or application endpoints.

## Local development

1. Start the infrastructure and observability stack:

   ```powershell
   docker compose up -d prometheus grafana
   ```

2. Start the gateway and backend APIs using the normal development commands.
   Prometheus scrapes their existing HTTP ports through
   `host.docker.internal`.

3. Open:

   - Prometheus: <http://localhost:9090/targets>
   - Grafana: <http://localhost:3001>
   - Grafana credentials: `admin` / `admin` (change these for shared
     environments)

   The **IT Helpdesk / IT Helpdesk Overview** dashboard is provisioned
   automatically. It includes service filtering, target health, request
   throughput, success rate, p50/p95/p99 latency, status-code distribution,
   5xx percentage, target availability, and process memory. The Prometheus
   data source and dashboard are defined under `infra/observability/grafana`.

If `admin` / `admin` is rejected after the stack has already been started,
the password in the persistent `grafana_data` volume is taking precedence
over the Compose environment variable. Reset it and clear a temporary
login lockout with:

```powershell
docker compose exec grafana grafana cli admin reset-admin-password admin
docker compose restart grafana
```

The reset command must be run against the running Compose service; changing
`GF_SECURITY_ADMIN_PASSWORD` alone does not overwrite an existing Grafana
database.

To verify an individual target:

```powershell
curl http://localhost:5121/metrics
```

Replace `5121` with the service port from
`infra/observability/prometheus/prometheus.yml`.

## Azure Container Apps

For Azure hosting, use Azure Monitor managed service for Prometheus as the
Prometheus storage and Azure Managed Grafana as the visualization layer. This
avoids operating stateful Prometheus and Grafana containers in the
production Container Apps environment.

1. Create an Azure Monitor workspace and Azure Managed Grafana instance in
   the same region as the Container Apps environment.
2. Enable managed Prometheus collection for the Container Apps environment
   and associate it with the Azure Monitor workspace.
3. Configure the environment's Prometheus scrape settings to discover each
   API Container App on `/metrics` over its internal ingress address. Keep
   the metrics endpoints internal; do not expose them through WSO2 or the
   public frontend.
4. Grant the managed Grafana identity the **Monitoring Data Reader** role on
   the Azure Monitor workspace, then add the workspace as Grafana's
   Prometheus data source.
5. Import the dashboard from
   `infra/observability/grafana/dashboards/it-helpdesk-overview.json`.

The Container App revisions must listen on the same HTTP target port already
used by each service. No application secret is required for scraping.

Useful Microsoft documentation:

- [Azure Monitor managed service for Prometheus](https://learn.microsoft.com/azure/azure-monitor/essentials/prometheus-metrics-overview)
- [Azure Managed Grafana](https://learn.microsoft.com/azure/managed-grafana/overview)
- [Configure managed Prometheus for Azure Container Apps](https://learn.microsoft.com/azure/container-apps/observability)

## Production considerations

- Keep `/metrics` on an internal Container Apps ingress or private network.
- Change the default local Grafana password and use Entra ID for Azure
  Managed Grafana.
- Alert on sustained 5xx rate, scrape failures, and request latency rather
  than on individual transient errors.
- Review metric retention and Azure Monitor workspace cost before production
  rollout.

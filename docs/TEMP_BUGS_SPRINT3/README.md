# Sprint 3 — Manual Testing Bug Log (temporary)

Working notes from manual QA of the `QA-Sprint-3` branch, staged here before
being folded into the final sprint report. One file per bug — delete this
folder once everything's been copied into the real report.

| ID | Title | Severity | Status |
|----|-------|----------|--------|
| [BUG-01](BUG-01-agent-rotation-removal-missing.md) | No way to remove an agent from the ticket rotation | High | Open |
| [BUG-02](BUG-02-accounts-table-id-column-cutoff.md) | "All accounts" table: ID column looked cut off | Low (cosmetic) | Fixed |
| [BUG-03](BUG-03-unauthenticated-internal-endpoints.md) | Unauthenticated internal endpoints leak the entire user directory | Critical | Open |
| [BUG-04](BUG-04-notification-crash-on-smtp-failure.md) | Notification.Api crashes entirely (and crash-loops) on any SMTP failure | Critical | Open |
| [BUG-05](BUG-05-notifications-silently-lost-on-crash.md) | A crash mid-processing silently loses the notification forever | Critical | Open |
| [BUG-06](BUG-06-sla-false-breach-alerts.md) | SLA breach alerts fire for tickets that are already resolved | High | Open |
| [BUG-07](BUG-07-ticket-report-timezone-bug.md) | Ticket report date filter shifts with the server's local timezone | Medium | Open |
| [BUG-08](BUG-08-sla-breach-duplicate-publish-on-save-failure.md) | A transient DB failure after a Kafka publish causes the same SLA breach to be republished | High | Open |

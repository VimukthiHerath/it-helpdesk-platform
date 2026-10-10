# ADR 0002: Event-Driven Notifications (Kafka)

- **Status:** Accepted
- **Date:** 2026-10-10

## Context
Originally mapped as REST HTTP synchronous transactions, actions directly triggering Notifications (e.g., ticket state changes) heavily bottlenecked core user operations when the downstream `Notification.Api` lagged or dropped connection.

## Decision
Use **Apache Kafka** for broadcasting cross-service events (notably `TicketCreated`, `TicketResolved`) to decouple service interdependency globally rather than relying on synchronous HTTP cascades.

## Consequences
- **Positive:** Guarantees absolute notification delivery pipelines even if downstream APIs are temporarily offline. Eradicates synchronous waiting for users when saving elements. 
- **Negative:** Directly increases baseline infrastructural operating complexity resulting in required container monitoring, broker offset commits, partition tuning, and message schema management.

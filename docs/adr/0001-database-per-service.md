# ADR 0001: Database-per-service

- **Status:** Accepted
- **Date:** 2026-10-10

## Context
During the platform transition towards microservices (Sprint 2), maintaining a monolithic database structure created a tight coupling where scaling an individual service or updating data models forced deployment collisions and lock contentions across multiple unbounded contexts.

## Decision
We elected to implement the **Database-per-service** model in MySQL 8. Each domain API (`Auth`, `Ticket`, `Assignment`, `Sla`, `Notification`) has been assigned its own internally bound data schema (e.g. `auth_db`, `ticket_db`) guaranteeing data encapsulation.

## Consequences
- **Positive:** Eliminates data-layer tight coupling. Databases can be tuned, scaled, and managed strictly aligning with the individual requirements of the service they support.
- **Negative:** Forces all cross-service state interactions to traverse physical network boundaries leveraging APIs or message events. Also heavily complicates any scenarios requiring distributed transactions (necessitating Saga or 2PC strategies).

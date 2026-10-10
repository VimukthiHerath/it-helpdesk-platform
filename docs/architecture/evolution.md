# Architectural Evolution

This document tracks the evolution of the IT Helpdesk Platform architecture from Sprint 1 baseline to the final hardened production state.

## Sprint 1 (Baseline)
- **Structure:** Initial modular setup focusing on core domains.
- **Execution:** Local Docker execution for fast developer onboarding.
- **Data:** Monolithic database structures backing early prototyping.
- **Integration:** Synchronous HTTP calls bridging domain components.
- **Security:** Basic JWT authentication implemented for foundational access control.

## Sprint 2 (Service Decoupling)
- **Structure:** Breakdown into true independent microservices: `Auth.Api`, `Ticket.Api`, and `Assignment.Api`.
- **Data:** Migration to a Database-per-service pattern executed inside MySQL.
- **Security:** Enhanced Role-Based Access Control (RBAC) mechanisms added for strict authorization.

## Sprint 3 (Event-Driven Integration)
- **Structure:** Apache Kafka introduced to facilitate robust asynchronous workflows.
- **Integration:** Event-driven bridges constructed for Notifications and SLA triggers (`TicketCreated`, `TicketResolved`) to decouple services and radically improve resilience during domain failures.

## Sprint 4 (Hardened Production)
- **Networking:** Decommissioning of the custom .NET YARP gateway in favor of the enterprise WSO2 API Gateway for North-South ingress handling.
- **Integration:** Kafka manual offset commits added guaranteeing at-least-once delivery thresholds.
- **Deployment:** Azure cloud deployment finalized leveraging Azure Container Apps and Azure DB for MySQL.

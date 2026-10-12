# ADR 0003: Replace YARP with WSO2 Gateway

- **Status:** Accepted (Replaces previous API Gateway decision).
- **Date:** 2026-10-10

## Context
During the platform scaling pipeline leading into Sprint 4 production footprints, our reverse-engineered .NET YARP gateway started displaying maintenance liabilities regarding automated Swagger API cataloging, rate-limiting, and deep analytics.

## Decision
Deprecate the custom in-house .NET YARP gateway immediately and adopt **WSO2 API Gateway** for all centralized North-South application ingress traffic management.

## Consequences
- **Positive:** Rapidly delivers robust, enterprise-grade capabilities right out-of-the-box including rate limiting scaling controls, dynamic analytics, JWT pass-through handling, and automated Swagger aggregation logic.
- **Negative:** Necessitates integrating external proprietary tooling layers directly over local infrastructures resulting in expanded WSO2 control-plane management curves.

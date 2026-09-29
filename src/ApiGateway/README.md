# API Gateway (YARP) — Reference / Fallback

**Status: fallback, not the standard gateway.** WSO2 API Manager
(`infra/wso2/`) is the team's authoritative gateway going forward. See
[`docs/WSO2_API_GATEWAY.md`](../../docs/WSO2_API_GATEWAY.md) for the full
picture, including why both exist.

This YARP-based gateway is kept as a working reference implementation, not
dead code — it was fixed and verified during Sprint 3 (see `git log` for
`fix(gateway):` commits) — but it should not be assumed to carry real
production traffic, and new gateway work should happen in WSO2 instead.

If the team decides to drop this fallback entirely, remove this directory
and its CI/CD pipeline (`.github/workflows/gateway-ci-cd.yml`) together in
one commit.

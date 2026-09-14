# Role-based authorization across services (AUTH-2)

## Summary

Every service validates JWTs and enforces role checks **locally**, from the
token itself. No service calls Auth over the network to answer "is this
caller allowed to do this." Auth's job ends at issuing the token; every
other service is responsible for reading and trusting it.

## How a request gets authorized

1. Auth signs a JWT at login with three claims: `sub` (user id), `email`,
   and `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`)
   set to the user's `UserRole` enum value (`Employee`, `Agent`, or
   `Administrator`).
2. Every other service configures `AddAuthentication().AddJwtBearer(...)`
   with the **same signing key, issuer, and audience** Auth signs with
   (`services/*/appsettings.json` → `Jwt` section). This lets each service
   validate a token's signature and claims without contacting Auth.
3. Controllers declare the roles they require with
   `[Authorize(Roles = Roles.Employee)]` (or a comma-joined string for
   multiple roles, e.g. `$"{Roles.Agent},{Roles.Administrator}"`).
4. ASP.NET Core's authorization middleware handles the three outcomes on
   its own: no/invalid token → `401`, valid token but missing the required
   role → `403`, valid token with the required role → the request reaches
   the action method. This is what satisfies AC2 (403, not a silent
   success or a generic 401) without any code in the controller.

## `Roles` constants

Each service has its own `Authorization/Roles.cs` with the same three
constants (`Employee`, `Agent`, `Administrator`). There's no shared class
library between services in this repo, so this is duplicated **on
purpose** rather than left as a TODO — pulling it into a shared package is
a separate, larger decision (versioning, publishing, coupling deploys)
that wasn't part of this ticket.

## Per-service status

| Service | Protected endpoints | Role(s) enforced |
|---|---|---|
| Auth | `GET /api/auth/me` | `[Authorize]` only — any authenticated user needs to be able to look up their own identity, so it's intentionally not role-restricted. `register`/`login` stay public by design. |
| Auth | `GET /api/auth/users`, `PUT /api/auth/users/{id}`, `PATCH /api/auth/users/{id}/deactivate` | `Administrator` — added in `[[admin-manage-users]]`. The edit/deactivate pair make a real cross-service call to Assignment.Api; see that doc and the note below. |
| Ticket | `POST /api/ticket` (submit), `GET /api/ticket/mine` (view own) | `Employee` |
| Ticket | `GET /api/ticket` (list all, unfiltered) | `Agent`, `Administrator` — this endpoint predates AUTH-2 and had no role check at all, which meant any employee could enumerate every other employee's tickets. Restricted to staff as part of this work since AC1 requires every non-public endpoint to check a role, and there's no dedicated queue/resolve endpoint (TICKET-4) yet to carry that responsibility instead. |
| Assignment | `GET /api/assignments/queue` | `Agent` — added in `[[agent-assignment-queue]]` (ASSIGN-4), which also gave Assignment.Api its first JWT/controller wiring (it previously had none). |
| Assignment | `GET /api/assignments/agents` (view the rotation) | `Agent`, `Administrator` — added Administrator-only in `[[agent-rotation-management]]`, relaxed to include Agent in `[[manual-ticket-reassignment]]` since agents need it to pick a reassignment target. |
| Assignment | `POST /api/assignments/agents` (manage the rotation) | `Administrator` — unchanged, still management-only. |
| Assignment | `GET /api/assignments` (list all current assignments), `PATCH /api/assignments/{ticketId}/reassign` | `Agent`, `Administrator` — added in `[[manual-ticket-reassignment]]` (ASSIGN-5 / SCRUM-20). |
| Notification | none beyond `/health` | n/a — no protected endpoints exist yet. |
| SLA | none beyond `/health` | n/a — no protected endpoints exist yet. |

`/health` is intentionally public on every service (used for container
health checks) and is never role-restricted.

## One deliberate exception to "no service calls another"

The Summary above says no service calls another over the network to answer
an authorization question, and that held until `[[admin-manage-users]]`:
`AssignmentRotationClient` in Auth.Api calls `GET /api/assignments/agents`
on Assignment.Api before letting an admin change an agent's role or
deactivate them, to make sure that agent isn't still in the round-robin
rotation. This is the first and only cross-service HTTP call anywhere in
this codebase — everywhere else, decoupling is via Kafka events or by
trusting the JWT. It's called out explicitly here so it doesn't get read as
the new normal: it exists because this one check genuinely can't be
answered from Auth's own database, and building event-driven sync for it
would have been overkill for what this app needs right now. See
`[[admin-manage-users]]` for the full reasoning and the fail-closed
behavior if Assignment.Api is unreachable.

## Eliminating the Auth network round-trip (AC3)

Before this work, Ticket didn't validate JWTs itself — `TicketController`
forwarded the caller's bearer token to Auth's `GET /api/auth/me` on every
request just to find out who was calling and whether the token was valid.
That's gone. Ticket now has its own `AddJwtBearer` configuration
(`services/ticket/Ticket.Api/Program.cs`), and the user id is read straight
off the validated token's claims (`JwtRegisteredClaimNames.Sub` /
`ClaimTypes.NameIdentifier`) — see `GetAuthenticatedUserId()` in
`TicketController.cs`. The `AuthApi` `HttpClient` registration and
`AuthApi:BaseUrl` config were removed since nothing uses them anymore.

## Testing pattern

`services/ticket/Ticket.Api.Tests/TicketAuthorizationTests.cs` is the
template for the other services once they have protected endpoints worth
testing. It's a `WebApplicationFactory<Program>` integration test that:

- mints its own JWTs in-process (same signing key/issuer/audience as the
  service under test, so it never calls Auth), and
- hits one protected endpoint three times: no token → expect `401`, valid
  token with the wrong role → expect `403`, valid token with the required
  role → expect `200`.

It needs the local MySQL instance from `docker-compose.yml` running, same
as running the service itself — there's no separate test-database setup
in this repo yet.

## What's intentionally not done here

- Notification/SLA got no code changes because they have no protected
  endpoints to attach roles to on this branch. When TICKET-4 (resolve)
  lands, apply `[Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]`,
  following the same pattern as Ticket's other endpoints. Assignment's own
  ASSIGN-4 gap was closed in `[[agent-assignment-queue]]`.
- No shared `Roles`/JWT-validation library was introduced — see "`Roles`
  constants" above for why.

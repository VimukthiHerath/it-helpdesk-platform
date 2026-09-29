# Jira Task — Route protected frontend calls through the WSO2 Gateway

Ready to paste into Jira as-is (title, description, acceptance criteria,
technical notes).

---

## Title
Make WSO2 API Gateway trust Auth.Api's JWTs, so protected frontend calls can route through it

## Type
Task (Technical Debt / Infrastructure)

## Priority
Medium

## Labels
`api-gateway`, `wso2`, `auth`, `tech-debt`

## Epic / Story link
EPIC-SCRUM-45-Platform-Infrastructure (or wherever the WSO2 gateway work is tracked)

---

## Description

The WSO2 API Gateway was deployed and configured this sprint, and is
confirmed working end-to-end for **public** routes (login, register, health
checks — anything WSO2 has marked `authType: None`). The frontend now
calls `/auth/v1/login` through the gateway for exactly this reason.

However, every **protected** route (ticket creation, "my tickets", the
agent queue, the admin report, the rotation panel, etc.) still calls its
service directly, bypassing the gateway entirely. This is not an oversight
to "just fix by changing a URL" — there's a real token-compatibility gap
underneath that has to be solved first.

### Why this isn't a simple URL swap

Right now, Auth.Api issues its own JWT at login (HMAC-signed with a shared
secret, i.e. `SecurityAlgorithms.HmacSha256` / HS256), and every other
service validates that same JWT directly using that same shared secret via
standard ASP.NET Core `[Authorize]` middleware. This works because every
service already trusts Auth.Api's signing key directly.

WSO2, by default, does **not** know anything about that shared secret. Its
own access-token validation expects a token issued by **its own** Key
Manager (a standard OAuth2 flow: register a client via DCR, get a token
from `/oauth2/token`). If the frontend sends Auth.Api's own JWT to a
WSO2-protected route, WSO2's own gatekeeping will reject it before the
request ever reaches the real backend — not because anything is broken, but
because two independent identity systems don't trust each other's tokens by
default.

### The actual fix

Configure WSO2 to validate Auth.Api's own JWTs directly, instead of issuing
its own. The standard way to do this:

1. **Switch Auth.Api's JWT signing from HS256 (shared secret) to RS256
   (public/private key pair).** HS256 can't be safely validated by a third
   party without handing them the same secret used to *sign* tokens (which
   would let WSO2 also *forge* valid tokens) — RS256 lets Auth.Api keep its
   private key to itself and only publish the public key.
2. **Expose a JWKS endpoint** on Auth.Api (a standard, well-known JSON
   endpoint, e.g. `/.well-known/jwks.json`) that publishes the public key in
   the standard JWKS format, so anything can verify a token's signature
   without ever seeing the private key.
3. **Configure a Key Manager in WSO2** pointing at that JWKS URL, so WSO2's
   own token validation accepts Auth.Api's JWTs as valid access tokens.
4. **Re-publish the 4 protected APIs** (Ticket, Assignment, and the
   protected operations of Auth) against this new Key Manager instead of
   WSO2's default resident one.
5. **Repoint the frontend's remaining API calls** (everything currently
   using `REACT_APP_TICKET_API_URL` / `REACT_APP_ASSIGNMENT_API_URL` /
   the protected parts of `REACT_APP_AUTH_API_URL`) at the gateway, the
   same way `LoginForm.jsx` already does for login.

## Acceptance Criteria

- **AC1:** Auth.Api issues RS256-signed JWTs instead of HS256, with no
  change to the token's claims or expiry behaviour — existing role-based
  `[Authorize]` checks continue to work unchanged.
- **AC2:** Auth.Api exposes a JWKS endpoint publishing its current public
  key, reachable without authentication.
- **AC3:** WSO2 has a Key Manager configured against that JWKS endpoint,
  and a real login-issued JWT successfully authorizes a call to a
  protected route (e.g. `GET /ticket/v1/mine`) through the gateway.
- **AC4:** An expired or tampered JWT is still correctly rejected by WSO2
  before reaching the backend service (i.e. WSO2 is doing real validation,
  not just passing everything through).
- **AC5:** All frontend calls that currently use
  `REACT_APP_TICKET_API_URL` / `REACT_APP_ASSIGNMENT_API_URL` are switched
  to `REACT_APP_API_GATEWAY_URL` with the correct `/ticket/v1/...` /
  `/assignment/v1/...` paths, and the app is manually verified to still
  work end-to-end (login, create ticket, view queue, assign, admin report).
- **AC6:** `docs/WSO2_API_GATEWAY.md` is updated to reflect that protected
  routes are now genuinely gatewayed, removing the "known gap" note this
  sprint added about direct-call bypass.

## Out of scope
- Changing anything about Sla.Api / Notification.Api's gateway exposure —
  they only expose `/health` and have no protected operations to migrate.
- Rate limiting / subscription tiers / API monetization — not needed for
  this case study, `Unlimited` policy stays as-is.

## Technical notes / risk
- This touches Auth.Api's token signing, which every other service
  depends on — do this on a dedicated branch, test locally against all 5
  services before touching the Azure deployment, and keep the HS256 code
  path easy to revert to if RS256 migration surfaces an unexpected issue.
- WSO2 API Manager 4.3.0 supports custom Key Managers out of the box (no
  version upgrade needed) — this is configuration, not a WSO2 upgrade.

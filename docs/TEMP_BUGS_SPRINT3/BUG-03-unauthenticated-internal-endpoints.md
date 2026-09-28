# BUG-03 — Unauthenticated internal endpoints leak the entire user directory

**Severity:** Critical — no authentication required, real PII exposed, full user enumeration possible.
**Status:** Open (not fixed).
**Found:** Manual testing + code review, Sprint 3 QA pass.
**Area:** Auth.Api — internal service-to-service endpoints.
**OWASP category:** Broken Access Control (A01:2021).

## Summary

Two endpoints in `AuthController.cs` are marked `[AllowAnonymous]` with no
other access restriction (no shared secret, no network/IP check, no service
identity check). They were clearly intended as internal, service-to-service
endpoints (per their own code comments) for Notification.Api's Kafka
consumers to resolve email addresses without a user's JWT — but as shipped,
they are exactly as reachable from the public internet as from another
microservice.

- `GET /api/auth/internal/users/{id}/email` — returns any user's name + email by guessing an integer ID.
- `GET /api/auth/internal/admins/emails` — returns every active administrator's email in one call.

## Steps to reproduce

No login, no token, no headers beyond a plain GET:

```bash
curl -s http://localhost:5121/api/auth/internal/admins/emails
# {"emails":["admin@example.com"]}

curl -s http://localhost:5121/api/auth/internal/users/1/email
# {"email":"admin@example.com","name":"QA Admin"}
```

Or just open either URL directly in a browser.

### Contrast with a correctly-protected endpoint

Proves this app *does* know how to enforce auth — these two endpoints are an
oversight, not the norm:

```
GET /api/auth/internal/admins/emails   -> HTTP/1.1 200 OK   (no Authorization header sent)
GET /api/auth/users                    -> HTTP/1.1 401 Unauthorized, WWW-Authenticate: Bearer
```

### Full directory enumeration

Walking sequential IDs against `/internal/users/{id}/email` dumps the entire
user table — every employee, agent, and admin, with real names and emails —
stopping cleanly at 404 once IDs run out:

```
user 1: {"email":"admin@example.com","name":"QA Admin"}                         [200]
user 2: {"email":"vimukthiherath123@gmail.com","name":"Vimukthi Herath"}        [200]
user 3: {"email":"it24101500@my.sliit.lk","name":"vimukthi herath"}             [200]
user 4: {"email":"rashaadrazeen@gmail.com","name":"Rashad Razeen"}              [200]
user 5: {"message":"User not found."}                                           [404]
```

This is trivially scriptable (`for i in 1..N`) into a full org directory dump.

## Root cause

`services/auth/Auth.Api/Controller/AuthController.cs`:
- Line 389 — `[AllowAnonymous]` on `GetUserEmail` (line 390-391).
- Line 412 — `[AllowAnonymous]` on `GetAdminEmails` (line 413-414).

Both are documented in their own preceding comments as internal,
service-to-service endpoints meant for Notification.Api's Kafka consumers
(which have no user JWT to present) — but `[AllowAnonymous]` just disables
auth entirely rather than restricting the caller to internal traffic. No
shared secret, mTLS, network policy, or API-gateway-level IP restriction
enforces the "internal" part in practice.

## Risk (why this matters beyond "no passwords leaked")

1. **Targeted credential stuffing** — the admin-emails list hands an
   attacker a curated set of high-value accounts to test against leaked
   `email:password` databases from unrelated breaches (people reuse
   passwords).
2. **Targeted phishing** — real name + real email + confirmed admin status
   makes a fake "IT Helpdesk password reset" email far more convincing than
   a random blast.
3. **Full personal-data exposure** — the enumeration endpoint alone leaks
   the whole organization's staff directory (names + emails) with zero
   authentication, which is a data-protection issue independent of any
   follow-on attack.
4. **Proves the access-control model has a gap, not that it's absent** — the
   same app correctly returns 401 on `/api/auth/users`; these two routes
   were evidently carved out deliberately and just never locked down to
   internal callers only.

## Suggested fix

Restrict these two routes to genuine internal/service traffic instead of
`[AllowAnonymous]` — e.g. a shared internal API key validated in
middleware, mTLS between services, or at minimum a network-level rule at
the gateway/ingress that blocks external traffic from ever reaching
`/internal/*` paths on Auth.Api.

## Screenshot

_(Attach: browser tab on `/internal/admins/emails`, and the curl 200-vs-401 contrast.)_

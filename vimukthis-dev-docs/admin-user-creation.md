# Admin user creation (AUTH-3)

## Endpoint

`POST /api/auth/users` — restricted to `Administrator` via
`[Authorize(Roles = Roles.Administrator)]`, the same local-JWT role
enforcement [[role-based-authorization]] set up in AUTH-2. No new
authentication mechanism was needed; this endpoint just adds a role
requirement stricter than `/api/auth/register`'s (public).

Request body is the existing `UserRegisterDTO` (`name`, `email`,
`password`, `role`) — reused as-is rather than duplicated, since the
fields and validation are identical to registration and nothing
currently distinguishes the two beyond who's allowed to call them.
Response is a new `AdminCreateUserResponseDTO` that includes the created
user's `Id` (registration's own response doesn't, and admin callers need
it to reference the account afterward).

## Shared logic with Register

`AuthController.CreateUserAsync(...)` is now the single place that
checks for a duplicate email, hashes the password with BCrypt, and
persists the `User` row. Both `POST /api/auth/register` and
`POST /api/auth/users` call it — the only difference between the two
endpoints is the `[Authorize]` requirement and which response DTO gets
built from the result. This was a refactor of existing `Register` code,
not new duplicated logic.

## Acceptance criteria → implementation

| AC | How it's satisfied |
|---|---|
| AC1: accepts name/email/password/role | `UserRegisterDTO` request body |
| AC2: admin-only | `[Authorize(Roles = Roles.Administrator)]` — unauthenticated → 401, wrong role → 403 (same ASP.NET Core authorization middleware behavior as AUTH-2, not custom code) |
| AC3: password salted/hashed | `BCrypt.Net.BCrypt.HashPassword(...)` in the shared helper — same as Register already did |
| AC4: duplicate email → clear conflict | `409 Conflict` with `{ message: "Email already registered." }` from the shared helper |
| AC5: created account can log in immediately | No separate activation step — the row is `IsActive = true` and password-hashed the same way Register's is, so `/api/auth/login` works against it right away |

## Test coverage

`services/auth/Auth.Api.Tests/AdminCreateUserTests.cs` (same
`WebApplicationFactory` + locally-minted-JWT pattern as
`Ticket.Api.Tests`):

- no token → 401
- authenticated as `Employee` → 403
- authenticated as `Administrator` → 201 with the created user's id/name/email/role
- duplicate email → 409
- admin-created user can call `/api/auth/login` and get a token back (AC5)
- the stored password is not the plaintext value and verifies against
  the plaintext via `BCrypt.Verify` (AC3) — read directly off
  `ApplicationDbContext` via the factory's service provider, not just
  inferred from the API response

Requires the local MySQL instance from `docker-compose.yml`, same as
`Ticket.Api.Tests`.

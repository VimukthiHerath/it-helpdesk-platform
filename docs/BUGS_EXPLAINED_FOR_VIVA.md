# Bugs Explained Simply — Viva Prep

For when an instructor points at a bug and says "explain this to me." Each one
below has: a plain-English explanation (say this out loud, don't read the
technical bug doc verbatim), why it actually matters, and the fastest way to
prove it live. Full technical detail/root cause/suggested fix for each is in
`docs/TEMP_BUGS_SPRINT3/BUG-0X-*.md` if you get a follow-up question deeper
than this.

With the app now genuinely deployed to Azure (not just local docker-compose),
some of these are more convincing to show against the **real live system**
instead of localhost — noted per bug below.

---

## BUG-01 — Can't remove an agent from the ticket rotation (High)

**Say this:** "An admin can add an agent to the rotation, but there's
genuinely no way to take one back out — not a button, not an API endpoint,
nothing. If you ever add someone to the rotation by mistake, or an agent
leaves the team, they're stuck in the system forever. You can't even
deactivate their account or change their role afterward, because the app's
own safety check blocks that until they're removed from the rotation first —
which you can't do."

**Why it matters:** it's a real operational dead-end, not a cosmetic gap. An
admin can paint themselves into a corner with no way out through the app
itself.

**How to show it:**
1. Add an agent to the rotation (Agent Rotation page).
2. Go to All Accounts, click Deactivate on that same agent.
3. Show the blocked message: *"This agent is still in the ticket rotation.
   Remove them from the rotation before..."*
4. Point out there's no Remove button anywhere on the Agent Rotation page.

---

## BUG-03 — Two endpoints leak the entire staff directory, no login needed (Critical)

**Say this:** "There are two endpoints in the Auth service that were clearly
meant for internal use only — one other backend service asking 'what's this
user's email' — but they were marked as public by mistake. So right now,
anyone on the internet, with no login at all, can call them and get back
every employee's real name and email, including who the admins are."

**Why it matters:** this is a genuine, currently-exploitable data leak. It's
not "a bug in the demo app" — if this shipped for real, it's a data breach
and a phishing setup handed to an attacker on a plate.

**How to show it (do this one against the real Azure deployment — much more
convincing than localhost):**
```bash
curl https://auth-service.gentlebeach-1f28fec1.southeastasia.azurecontainerapps.io/api/auth/internal/admins/emails
```
No token, no header, nothing — and it returns a real admin's email straight
from the live production-ish database. Then show a *correctly* protected
route failing the same way with no token (e.g. `/api/auth/users` → 401), to
prove the app clearly knows how to enforce login — these two routes are a
specific mistake, not a missing feature.

---

## BUG-04 — One missing email setting crashes the entire notification service (Critical)

**Say this:** "If sending an email ever fails for any reason — wrong
password, mail server down, anything — the code that sends it doesn't catch
that error. So instead of just that one email failing, it crashes the
*entire* notification service. Not just email — SLA breach alerts too,
because they live in the same process. And it doesn't recover: it crashes
again immediately every time it restarts, on the same message."

**Why it matters:** a tiny, everyday failure (a mail server hiccup) takes
down a whole service instead of just logging an error and moving on. We
actually watched this happen for real in Azure before the SMTP credentials
were configured — confirmed, not theoretical.

**How to show it:** safest is to break it on purpose and put it back after:
```bash
cd services/notification/Notification.Api
dotnet user-secrets remove "Smtp:Port"
dotnet run
# create a ticket -> watch it crash with an unhandled exception
dotnet user-secrets set "Smtp:Port" "587"   # restore afterward
```

---

## BUG-05 — A crash mid-send loses that notification forever, silently (Critical)

**Say this:** "This is the direct follow-on from BUG-04. When it crashes
partway through sending an email, Kafka has already marked that message as
'delivered' — so when the service restarts, it doesn't try that message
again. It's just gone. No error anywhere saying a notification went
missing — the only sign is that it never arrives."

**Why it matters:** silent data loss is worse than a visible crash. Nobody
gets paged, nothing shows red, an employee just never gets their "ticket
received" confirmation and nobody knows why.

**How to show it:** continue right on from the BUG-04 demo — query
`notification_db.ProcessedEvents` for that ticket and show the row was
never written, then create a *new* ticket and show its email *does* arrive,
proving it's specifically the in-flight message that vanished.

---

## BUG-06 — SLA breach alerts fire on tickets that are already resolved (High)

**Say this:** "The SLA service has no way of finding out a ticket was
resolved. So if you close a ticket two seconds after opening it, and its
original 1-hour deadline eventually passes anyway, the system still sends
an urgent breach alert to every admin — for a ticket that's been closed for
an hour."

**Why it matters:** false alarms train people to ignore real ones. If
admins learn breach alerts are often noise, they'll eventually miss a real
one.

**How to show it:** create a 1-hour-urgency ticket, resolve it immediately
as admin, then push its deadline into the past directly in the database and
watch the breach monitor fire on it anyway within a minute. Works
identically against the real Azure deployment now.

---

## BUG-07 — The ticket report gives different answers depending on the server's clock (Medium)

**Say this:** "When you filter the admin report by date, the app is
supposed to treat that date as a calendar day. Instead it silently assumes
the date is in whatever timezone the server machine happens to be set to,
not UTC. So the exact same filter, run on two servers in different
timezones, returns different tickets. It's not a crash — it's quietly
wrong, which is worse."

**Why it matters:** it makes the report deployment-dependent and untrustworthy without anyone realizing — an admin auditing "last Monday's tickets" would get a
silently different answer depending on where the app happens to be hosted.

**How to show it:** **use the automated test for this one, not a live
demo** — Azure's servers default to UTC, so the bug doesn't visibly trigger
there the way it did on a UTC+5:30 dev machine (the defect is still there,
it just isn't visibly wrong on a UTC host). Run:
```bash
cd services/ticket/Ticket.Api.Tests
dotnet test --filter TicketReportDateFilterTests
```
Both tests fail, printing the exact tick-level time shift as proof.

---

## BUG-08 — A brief database hiccup can send the same breach alert twice (High)

**Say this:** "When the breach monitor finds a breached ticket, it sends
the Kafka alert first, then saves 'already alerted' to the database
afterward. If that save fails — even for a totally unrelated, temporary
reason — the alert was already sent, but the database never recorded it. So
the very next check, a minute later, sees the ticket as still un-alerted
and sends the exact same urgent email again."

**Why it matters:** duplicate urgent alerts erode trust the same way false
ones do (see BUG-06) — and this one was found by writing a test, not by
accident, which is worth mentioning: it shows the difference between
"tested until it looked fine" and "tested until we tried to break the exact
order operations happen in."

**How to show it:** this one genuinely can't be demoed live (you'd need to
time a real database failure to the millisecond) — say that plainly, then
run the test that proves it deterministically:
```bash
cd services/sla/Sla.Api.Tests
dotnet test --filter CheckForBreaches_SaveChangesFailsAfterPublish_StillPublishesExactlyOnce
```
Walk through what the test does: publish succeeds, save is made to fail on
purpose, next cycle re-checks — and the mock proves the same ticket was
published twice, not once.

---

## Quick reference table

| Bug | Severity | Best demo |
|---|---|---|
| BUG-01 | High | Live, either environment |
| BUG-03 | Critical | Live, **Azure** (more convincing) |
| BUG-04 | Critical | Live, local only (deliberately breaks the running service) |
| BUG-05 | Critical | Live, local only (continuation of BUG-04) |
| BUG-06 | High | Live, either environment |
| BUG-07 | Medium | xUnit test only (Azure is UTC, won't visibly show it) |
| BUG-08 | High | xUnit test only (can't time a real DB failure live) |

# How to Explain and Demo the Sprint 3 QA Work

A cheat sheet for presenting this sprint's QA work out loud — what to say,
what to run, and what to click. Written so you can follow it live without
re-reading the full report or bug files first (those are still there for
backup detail, linked below each section).

---

## 1. The 30-second explanation (say this first)

> "This sprint is built around async, event-driven features — SLA
> deadlines, background breach detection, email notifications, a ticket
> report. I tested it three ways: **Selenium** for anything UI-facing,
> **xUnit** for backend logic I could pull out into its own testable class,
> and **manual/live testing** for anything that only shows up when several
> real services talk to each other. I found 8 bugs this way, documented
> each one with proof, and wrote regression tests for the ones that could
> be turned into a test — so most of them aren't just "I saw this once,"
> they're "here's a test that fails on demand, right now.""

That's the whole pitch. Everything below is just backing it up.

---

## 2. The three kinds of testing, explained simply

| Kind | What it proves | How to tell it apart |
|---|---|---|
| **Selenium** (Java/TestNG) | The actual UI works, in a real browser, end to end | Lives in `selenium-tests/`, run with `mvn test` |
| **xUnit** (C#/Moq) | A specific piece of backend logic behaves correctly (or doesn't) | Lives in each service's `*.Tests` folder, run with `dotnet test` |
| **Manual/live** | Things that only happen when real services genuinely talk to each other (a real crash, a real timezone, a real Kafka message) | No automation — screenshots + terminal output captured at the time |

If someone asks "why not automate everything?" — the honest answer is: some
of these bugs (a process crash taking down a *different* consumer, a
timezone-dependent shift, cross-service state) are either impractical or
meaningless to fake in a unit test. You automated what's genuinely
testable in isolation and were upfront about the rest.

---

## 3. Demoing the xUnit tests (do this first — it's the fastest, cleanest proof)

Three test projects, one per service. Each one has a mix of **passing**
tests (proving correct behaviour) and **deliberately failing** tests (each
failure *is* a bug, not a mistake — say this out loud before you run them,
so a red test doesn't look like something going wrong).

```bash
# SLA-2 / SLA-3 — deadline math + breach detection
cd services/sla/Sla.Api.Tests
dotnet test
# Expect: 9 passed, 1 failed (SaveChangesFailsAfterPublish... = BUG-08, live proof)

# NOTIFY-2 / NOTIFY-4 — notification dispatch
cd services/notification/Notification.Api.Tests
dotnet test
# Expect: 8 passed, 1 failed (EmailServiceThrows_ExceptionShouldNotPropagate = BUG-04, live proof)

# REPORT-1 — date-range filtering
cd services/ticket/Ticket.Api.Tests
dotnet test
# Expect: the 2 TicketReportDateFilterTests fail = BUG-07, live proof
# (the rest of this project needs MySQL/Kafka running locally — if you
# haven't started docker-compose, ignore any other failures, they're
# environmental, not bugs)
```

**What to say while it runs:** "This test isn't checking what the code
*currently* does — it's checking what it's *supposed* to do. When it fails,
that's the bug speaking for itself, not a flaky test."

**Good one to point at directly:** `SlaBreachDetectionServiceTests.cs` →
`CheckForBreaches_SaveChangesFailsAfterPublish_StillPublishesExactlyOnce`.
Walk through it: it fakes a database failure at the exact moment right
after a Kafka message has already gone out, then shows the same alert gets
sent twice. That's a bug you'd almost never catch by clicking around the
UI — this is exactly the kind of thing xUnit is good for.

**Good contrast to point at:** run
`SlaBreachedNotificationServiceTests.ProcessAsync_EmailServiceThrows_ExceptionDoesNotPropagate`
(passes) next to
`TicketCreatedNotificationServiceTests.ProcessAsync_EmailServiceThrows_ExceptionShouldNotPropagate`
(fails) — same simulated failure, two different consumers, only one handles
it. This is the evidence behind BUG-04's real root cause.

---

## 4. Demoing the Selenium tests

```bash
cd selenium-tests
mvn test
```

Watch for `BUILD SUCCESS` and `Tests run: 6, Failures: 0` at the end. A
Chrome window will pop up and drive itself — let it run once with the
window visible before the demo so people see it's a real browser, not a
mock.

**What to say:** "These don't call the API directly — they load the real
page, click the real buttons, and read the real rendered text, so they'd
catch a broken button just as much as a broken endpoint."

Two suites: `LoginSeleniumTest` (3 tests) and `TicketReportSeleniumTest` (3
tests, covering REPORT-1's admin-only access, status filtering, and the
"No tickets found" empty state).

---

## 5. Demoing each bug live

Full reproduction steps (with exact curl commands, exact SQL, exact
screenshots) are in `docs/TEMP_BUGS_SPRINT3/BUG-0X-*.md` — this section is
just the fast, "what to actually click/type in front of people" version.
Prereqs: all 5 backend services running, Docker (MySQL/Kafka) up.

### BUG-03 — Unauthenticated internal endpoints (Critical) — the headline bug
The single most convincing one. Open a browser and just visit:
```
http://localhost:5121/api/auth/internal/admins/emails
```
No login. It returns a real admin email. Then visit:
```
http://localhost:5121/api/auth/internal/users/1/email
```
Also no login — real name + email. Point out the contrast: hit
`http://localhost:5121/api/auth/users` right after (no token) and it
correctly 401s. That proves this app *knows how* to enforce auth — these
two routes are a specific oversight, not a systemic gap.

### BUG-01 — Can't remove an agent from rotation (High)
1. Log in as admin → **Agent rotation** → add an agent.
2. Go to **All accounts** → click **Deactivate** on that same agent.
3. Show the blocked message: *"This agent is still in the ticket
   rotation. Remove them from the rotation before..."* — then show there is
   genuinely no remove button anywhere (Agent Rotation page, or the account
   row). The app's own error message references an action that doesn't exist.

### BUG-06 — SLA false breach on a resolved ticket (High)
1. Create a ticket with `urgency: 0` (1-hour SLA) as an employee.
2. Resolve it immediately as admin (well inside the 1-hour window).
3. Query the SLA table directly:
   ```bash
   docker exec local-mysql mysql -uroot -plocaldevpassword \
     -e "SELECT TicketId, Status FROM sla_db.TicketSlas WHERE TicketId=<id>;"
   ```
   Show `Status: Active` even though the ticket itself is Resolved.
4. (Optional, +1 min) push the deadline into the past via SQL and watch
   Sla.Api's console log `Found 1 breached tickets!` for a ticket that's
   already closed.

### BUG-04 + BUG-05 — Notification crash + silent message loss (Critical, Critical)
Demo together — BUG-05 is a direct consequence of BUG-04.
1. `cd services/notification/Notification.Api && dotnet user-secrets remove "Smtp:Port"`
2. `dotnet run`
3. Create a ticket as an employee — watch the console throw
   `ArgumentNullException` and the whole app shut down
   (`BackgroundServiceExceptionBehavior is configured to StopHost`).
4. Query `notification_db.ProcessedEvents` — no row for that ticket. The
   email is gone, not retried, not logged as missing.
5. Restore the secret after: `dotnet user-secrets set "Smtp:Port" "587"`.

**If you don't want to actually crash the service live:** just run the
xUnit test instead —
`TicketCreatedNotificationServiceTests.ProcessAsync_EmailServiceThrows_ExceptionShouldNotPropagate`
shows the exact same failure, safely, in under a second.

### BUG-07 — Report date filter shifts with server timezone (Medium)
```bash
curl "http://localhost:5164/api/Ticket/report?startDate=<today>" \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```
Point at a returned ticket's `createdDate` — it'll show *yesterday's* UTC
date even though you filtered for today (on a UTC+5:30 machine). Or just
run the two `TicketReportDateFilterTests` — same bug, no server needed,
shows the exact tick-level shift (`00:00:00Z` expected vs `18:30:00Z`
actual — precisely 5h30m, matching the host's own timezone offset).

### BUG-08 — Duplicate SLA breach alert on a DB save failure (High)
This one genuinely can't be demoed live (you'd need to time a real MySQL
failure to the millisecond) — say that plainly, then run the test instead:
```bash
cd services/sla/Sla.Api.Tests
dotnet test --filter CheckForBreaches_SaveChangesFailsAfterPublish_StillPublishesExactlyOnce
```
Explain the setup out loud: publish to Kafka succeeds, the DB save right
after it fails, and the very next cycle re-detects the same ticket as
still-active and re-publishes. Real duplicate admin alert, proven
deterministically instead of by luck.

### BUG-02 — Accounts table ID column cut off (Low, Fixed)
Quick one — just mention it was found, fixed, and closed (one CSS padding
line). Don't spend demo time on it; it's here for completeness in the bug
log, not because it needs proving.

---

## 6. Suggested demo order (if you only have a few minutes)

1. The 30-second pitch (section 1).
2. BUG-03 live in a browser — most visual, most alarming, takes 10 seconds.
3. One xUnit run showing a red test (`Sla.Api.Tests`) — explain what a
   failing test means here.
4. One Selenium run (`mvn test`) — let the browser pop up, that's the
   "wow, it's really driving the app" moment.
5. Mention BUG-04/05/06/07/08 briefly with the summary table from the QA
   report rather than live-demoing all of them — time management.
6. Close with the Conclusion paragraph from `docs/QA_report_IT24101500_Sprint3.docx`
   — it already states the two concrete next steps in one place.

---

## 7. Where everything lives (for quick lookup mid-demo)

| What | Where |
|---|---|
| Full QA report | `docs/QA_report_IT24101500_Sprint3.docx` |
| Full bug write-ups (repro steps, root cause, screenshots) | `docs/TEMP_BUGS_SPRINT3/` |
| xUnit tests | `services/sla/Sla.Api.Tests/`, `services/notification/Notification.Api.Tests/`, `services/ticket/Ticket.Api.Tests/` |
| Selenium tests | `selenium-tests/src/test/java/com/ithelpdesk/` |

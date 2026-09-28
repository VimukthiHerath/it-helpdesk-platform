# BUG-05 — A crash mid-processing silently loses the notification forever

**Severity:** Critical — no error, no retry, no trace; the email just never happens.
**Status:** Open (not fixed).
**Found:** Manual testing, Sprint 3 QA pass (found immediately after reproducing BUG-04).
**Area:** Notification.Api — all four Kafka consumers.

## Summary

When Notification.Api crashes partway through handling a message (see
BUG-04 for one real way that happens), the Kafka message it was working on
does **not** get redelivered when the service comes back. It's gone. No
error is logged about it being dropped, nothing retries it, and there's no
record anywhere that it was ever meant to happen. The ticket it was for
still exists and looks completely normal — there's just no evidence its
notification email was ever supposed to go out.

## Root cause

Every consumer (`TicketCreatedConsumer.cs` in both Notification.Api and
Sla.Api, `TicketAssignedConsumer.cs`, `SlaBreachedConsumer.cs`) is
configured with:

```csharp
var consumerConfig = new ConsumerConfig
{
    ...
    EnableAutoCommit = true
};
```

`EnableAutoCommit = true` means Kafka tracks "this message has been
delivered to the app" independently of whether the app actually finished
(or even started) doing anything useful with it. The idempotency record
(`ProcessedEvents`) is only written *after* the email successfully sends —
so there's a real window where a message has been marked delivered by
Kafka, but the app hasn't actually recorded or acted on it yet. If a crash
happens inside that window, the message's offset is already committed (via
the periodic auto-commit, or the final commit Confluent's client performs
when the consumer shuts down) — so on restart, Kafka does not hand it back.
It's simply gone.

## How to see it yourself

This is a direct continuation of BUG-04's repro — the same crash is what
exposes it.

1. Follow **BUG-04's steps 1-4** to make Notification.Api crash while
   handling a ticket's "ticket received" email (remove the `Smtp:Port`
   secret, start the service, create a ticket, watch it crash).
2. **Check the idempotency table for that event** — it should be empty for
   this ticket, proving the email was never recorded as handled:
   ```bash
   docker exec local-mysql mysql -uroot -plocaldevpassword \
     -e "SELECT * FROM notification_db.ProcessedEvents;"
   ```
   Empty (or missing the row for your ticket) confirms it.
3. **Restore the SMTP secret and restart the service** (see BUG-04 step 7),
   so it comes back up cleanly this time.
4. **Wait — no retry happens.** No new attempt appears in the logs for that
   ticket, and no email ever arrives. Compare against creating a *new*
   ticket right now, which *does* get its "ticket received" email sent
   immediately (proving the mechanism itself works — it's specifically the
   message that was in flight during the crash that's gone).

### What to screenshot for the report
- The empty (or missing-row) result of the `ProcessedEvents` query for the
  ticket that was being processed when it crashed.
- The log from a freshly-created ticket right after, showing `[EMAIL SENT]`
  for *that* one — the contrast proves the lost message never came back.

## Why this matters

This isn't limited to the crash scenario in BUG-04 — any crash, restart, or
deploy at the wrong moment loses whatever notification was mid-flight, with
zero visibility that it happened. For a helpdesk platform, that means an
employee's "ticket received" confirmation, an agent's assignment
notification, or — worst of all — an SLA breach alert to admins, can simply
never arrive, with nothing in the logs pointing at a missing notification
(only the absence of one).

## Suggested fix

Don't commit the offset until the message has been fully and successfully
processed: set `EnableAutoCommit = false` and manually commit only after the
email send and the `ProcessedEvents` write both succeed. This also removes
the *duplicate*-email risk on the other side of the same design (a crash
*after* sending but *before* the auto-commit would otherwise cause a
redelivery and a second email).

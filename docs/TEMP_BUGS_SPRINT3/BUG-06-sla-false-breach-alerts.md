# BUG-06 — SLA breach alerts fire for tickets that are already resolved

**Severity:** High — false alerts to admins for closed work, undermines trust in the alert system.
**Status:** Open (not fixed).
**Found:** Manual testing, Sprint 3 QA pass.
**Area:** Sla.Api — breach monitoring.

## Summary

Resolving or closing a ticket does not update its SLA record. The SLA
record stays `Status: Active` forever, so once the original deadline passes
— even if the ticket was closed within minutes of being created — the
breach monitor still treats it as breached and fires an alert.

## Root cause

- `services/ticket/Ticket.Api` never publishes any event when a ticket's
  status changes to Resolved/Closed (`TicketController.cs` only publishes to
  Kafka on ticket *creation* — see the single `_kafkaProducer.ProduceAsync`
  call in `CreateTicket`).
- `services/sla/Sla.Api` has no consumer listening for a resolved/closed
  ticket at all — there's no topic for it to listen to even if it wanted to.
- `SlaBreachMonitorService.cs` (lines 58-60) only ever checks
  `Status == "Active" && DeadlineUtc <= now` — it has no way to know the
  ticket was actually finished, because nothing ever tells it.

Once a `TicketSla` row is created (`Status: Active`) at ticket-creation
time, the only thing that ever changes its status is the breach monitor
itself, flipping it to `Breached` once the deadline passes. There is no
code path that flips it to anything like `Resolved` or `Closed`.

## How to see it yourself

Fast version — proves the root cause in under a minute, no waiting:

1. **Register an employee and log in** (or use an existing one):
   ```bash
   curl -X POST http://localhost:5121/api/auth/register -H "Content-Type: application/json" \
     -d '{"name":"Demo","email":"demo2@example.com","password":"DemoPass123!"}'
   TOKEN=$(curl -s -X POST http://localhost:5121/api/auth/login -H "Content-Type: application/json" \
     -d '{"email":"demo2@example.com","password":"DemoPass123!"}' | jq -r .token)
   ```
2. **Create a ticket with the fastest SLA tier** (`urgency: 0` = 1 hour):
   ```bash
   curl -X POST http://localhost:5164/api/Ticket -H "Content-Type: application/json" \
     -H "Authorization: Bearer $TOKEN" \
     -d '{"description":"SLA demo","issueType":"Other","urgency":0}'
   ```
   Note the `ticketId` returned.
3. **Resolve it immediately as an admin** (well within the 1-hour window):
   ```bash
   ADMIN_TOKEN=$(curl -s -X POST http://localhost:5121/api/auth/login -H "Content-Type: application/json" \
     -d '{"email":"admin@example.com","password":"Admin123!"}' | jq -r .token)
   curl -X PATCH http://localhost:5164/api/Ticket/<ticketId>/status -H "Content-Type: application/json" \
     -H "Authorization: Bearer $ADMIN_TOKEN" -d '{"newStatus":2}'
   ```
4. **Check the SLA record directly** — this is the proof, no waiting required:
   ```bash
   docker exec local-mysql mysql -uroot -plocaldevpassword \
     -e "SELECT TicketId, Status, DeadlineUtc FROM sla_db.TicketSlas WHERE TicketId=<ticketId>;"
   ```
   You'll see `Status: Active` and a deadline still an hour in the future —
   even though the ticket itself is already Resolved.

Full version — proves the alert actually fires, in about a minute more:

5. **Push the deadline into the past** so the next poll catches it (the
   monitor runs every 60 seconds):
   ```bash
   docker exec local-mysql mysql -uroot -plocaldevpassword \
     -e "UPDATE sla_db.TicketSlas SET DeadlineUtc = UTC_TIMESTAMP() - INTERVAL 1 MINUTE WHERE TicketId=<ticketId>;"
   ```
6. **Watch Sla.Api's console** within the next minute — you'll see:
   ```
   Found 1 breached tickets!
   Published SlaBreached event for Ticket <ticketId>. Partition: [0], Offset: ...
   ```
7. **Confirm the DB flipped to `Breached`** for a ticket that's already
   Resolved:
   ```bash
   docker exec local-mysql mysql -uroot -plocaldevpassword \
     -e "SELECT TicketId, Status FROM sla_db.TicketSlas WHERE TicketId=<ticketId>;"
   ```
8. If SMTP is configured (see BUG-04), an actual "[URGENT] SLA Breach"
   email goes out to every admin, for a ticket that was closed within
   seconds of being opened.

### What to screenshot for the report
- Step 3's response (ticket shows `status: 2`, i.e. Resolved) next to
  step 4's query result (`Status: Active`) — the contradiction is the bug.
- Step 6's console log (`Found 1 breached tickets!` /
  `Published SlaBreached event`).
- Step 7's query result showing `Status: Breached` on an already-resolved ticket.

## Suggested fix

Ticket.Api should publish an event when a ticket is resolved/closed (or SLA
should be told directly), and Sla.Api needs a consumer that marks the
`TicketSla` row as something other than `Active` (e.g. `Resolved`) when
that happens, so the breach monitor stops considering it.

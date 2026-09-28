# BUG-07 — Ticket report date filter shifts with the server's local timezone

**Severity:** Medium — silently wrong/inconsistent report data, no error shown.
**Status:** Open (not fixed).
**Found:** Manual testing, Sprint 3 QA pass.
**Area:** Ticket.Api — admin ticket report (`GET /api/Ticket/report`).

## Summary

The report's `startDate`/`endDate` filters are supposed to mean "tickets
created on or after/before this calendar date." Instead, the exact set of
tickets returned depends on **whatever timezone the server machine happens
to be set to** — not UTC, not the admin's timezone, not anything the admin
can predict or control from the request itself. Deploy this same code to a
server in a different timezone (or just change the machine's clock
settings) and identical requests return different results.

## Live proof (no setup needed, already captured)

The dev/test machine here is set to **Sri Lanka Standard Time (UTC+5:30)**.
Querying the report for tickets starting **2026-09-29** returned tickets
whose own `createdDate` field says **2026-09-28**:

```
GET /api/Ticket/report?startDate=2026-09-29

[
  { "ticketId": 3, "createdDate": "2026-09-28T18:57:37.781868", ... },
  { "ticketId": 2, "createdDate": "2026-09-28T18:49:47.463445", ... }
]
```

The API's own response contradicts its own filter — you asked for tickets
from the 29th onward and got tickets stamped the 28th.

## Root cause

`services/ticket/Ticket.Api/Controller/TicketController.cs`, lines 300-304:

```csharp
if (startDate.HasValue)
    query = query.Where(t => t.CreatedAt >= startDate.Value.ToUniversalTime());

if (endDate.HasValue)
    query = query.Where(t => t.CreatedAt <= endDate.Value.ToUniversalTime().AddDays(1).AddTicks(-1));
```

`startDate`/`endDate` are bound from the query string as plain
`DateTime?` with no timezone info, so ASP.NET Core gives them
`DateTimeKind.Unspecified`. Per .NET's own documented behavior,
`DateTime.ToUniversalTime()` on an `Unspecified` value **assumes it's
already in the server's local timezone** and converts from there. So
`startDate=2026-09-29` (meant as midnight, some day) gets silently
interpreted as "midnight in whatever timezone this particular server
happens to be running in," then converted to UTC by subtracting that
server's offset — here, 5 hours 30 minutes, shifting the real boundary back
into the previous UTC day.

`t.CreatedAt` in the database is stored in UTC (see ticket creation code),
so this filter is comparing a UTC column against a boundary that's silently
off by however many hours the server's local offset happens to be — 0 on a
UTC server, +5:30 here, and something else entirely wherever this actually
gets deployed.

## How to see it yourself

1. Log in as admin, get a token.
2. Create at least one ticket (or use existing ones).
3. Query the report for "today" using your **server's local date**:
   ```bash
   curl "http://localhost:5164/api/Ticket/report?startDate=<today's local date>" \
     -H "Authorization: Bearer $ADMIN_TOKEN"
   ```
4. Compare each returned ticket's `createdDate` (which is UTC) against the
   date you actually asked for. If your server isn't on UTC, you'll see
   tickets from the previous UTC day appear — exactly like the capture
   above.

### What to screenshot for the report
- The request URL (`startDate=2026-09-29`) and the JSON response showing a
  `createdDate` from the day before, side by side.
- The relevant lines of `TicketController.cs` (300-304) in your IDE.

## Why this matters

This isn't just "off by a few hours" — it means the exact same report,
run with the exact same filter, gives **different answers depending on
which server happens to be running it**. An admin auditing "all tickets
from last Monday" gets a silently different (and silently wrong) answer
depending on deployment location, with no indication anything's off.

## Suggested fix

Treat incoming date-only filters as UTC explicitly (e.g.
`DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)` instead of
`.ToUniversalTime()`), or accept an explicit offset/timezone from the
client and convert deliberately instead of relying on whatever the host
machine happens to be set to.

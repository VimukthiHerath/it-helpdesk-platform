# BUG-08 — A transient DB failure after a Kafka publish causes the same SLA breach to be republished

**Severity:** High — duplicate "[URGENT] SLA Breach" emails to every admin for the same ticket; violates SLA-3 AC2 ("published exactly once").
**Status:** Open (not fixed).
**Found:** While writing automated regression tests for SLA-3 (Sla.Api.Tests), Sprint 3 QA pass — not found manually, found by deliberately simulating a DB failure at the exact point between the Kafka publish and the persisting save.
**Area:** Sla.Api — `SlaBreachDetectionService`.

## Summary

`SlaBreachDetectionService.CheckForBreachesAsync` publishes a Kafka
`SlaBreached` event for every newly-breached ticket, flips that ticket's
in-memory `Status` to `"Breached"`, and only then calls a single
`SaveChangesAsync()` for the whole batch at the end. If that save fails for
any reason (a transient DB blip, a lock timeout, a connection drop) *after*
one or more tickets already published successfully to Kafka, every one of
those in-memory status flips is lost. The next monitor cycle (60 seconds
later) reads the database, finds those same tickets still `"Active"`, and
publishes a second `SlaBreached` event for each — sending duplicate breach
alert emails to every admin for a ticket that already triggered one.

This is the same class of bug as BUG-05 (a side effect happens, but the
record of it happening isn't durably persisted before the next thing can go
wrong) — here on the publishing side rather than the consuming side.

## Root cause

`services/sla/Sla.Api/Services/SlaBreachDetectionService.cs`, lines 47-91:

```csharp
foreach (var ticket in breachedTickets)
{
    ...
    var result = await _producer.ProduceAsync(topic, new Message<string, string> { ... }, cancellationToken);
    ticket.Status = "Breached";   // <- only in memory so far
    ...
}

await _db.SaveChangesAsync(cancellationToken);   // <- one call for the whole batch
```

The Kafka publish and the DB write that's supposed to prevent a repeat
publish are two separate, non-atomic steps, and the code path only
succeeds end-to-end if *nothing* goes wrong between them. If
`SaveChangesAsync` throws after even one successful `ProduceAsync` call in
the loop, that ticket's `Status` update never lands, and nothing about the
already-sent Kafka message is rolled back or recorded anywhere.

## Proven by an automated test, not manual reproduction

Reliably forcing a real MySQL failure to land in that exact 1-request
window isn't practical to demonstrate by hand, so this was proven with a
regression test instead —
`services/sla/Sla.Api.Tests/SlaBreachDetectionServiceTests.cs`,
`CheckForBreaches_SaveChangesFailsAfterPublish_StillPublishesExactlyOnce`.

The test uses a `SlaDbContext` subclass whose `SaveChangesAsync` throws
once, seeds one ticket already past its deadline, runs one check cycle
(Kafka publish succeeds, save throws), then runs a second check cycle a
"minute later" against a fresh context reading the real persisted state.
It asserts the ticket was published to Kafka exactly once — matching AC2 —
and it fails against the current code:

```
Failed Sla.Api.Tests.SlaBreachDetectionServiceTests.CheckForBreaches_SaveChangesFailsAfterPublish_StillPublishesExactlyOnce
Moq.MockException :
Expected invocation on the mock once, but was 2 times: p => p.ProduceAsync("sla-breached", It.Is<Message<string, string>>(m => m.Key == "1"), It.IsAny<CancellationToken>())
```

This is a genuine, deterministic proof of the defect — the same ticket was
published twice, exactly as the bug predicts.

### What to screenshot for the report
- The `dotnet test` output above showing this one test failing while the
  rest of `Sla.Api.Tests` passes.

## Suggested fix

Persist the status flip to `"Breached"` (or write an idempotent
"already-notified" marker) *before* or atomically with the Kafka publish for
each ticket — e.g. `SaveChangesAsync` per-ticket immediately after its own
successful `ProduceAsync`, rather than batching every ticket's save until
the very end of the loop. That way a save failure can only ever affect
tickets not yet published this cycle, never one that already went out.

# BUG-04 — Notification.Api crashes entirely (and stays crashed) on any SMTP failure

**Severity:** Critical — takes down the whole service, not just one email; does not self-recover.
**Status:** Open (not fixed). Currently masked in this dev environment because valid SMTP credentials are now configured via `dotnet user-secrets` — the underlying code defect is untouched.
**Found:** Manual testing, Sprint 3 QA pass (found while reproducing BUG-06, the SLA false-breach bug).
**Area:** Notification.Api — email sending.

## Summary

`EmailService.SendAsync` reads its SMTP settings with no null-check and no
error handling, and the two Kafka consumers that call it only catch
`ConsumeException`/`JsonException` — nothing else. So *any* problem with
sending an email (missing config, wrong password, Gmail rejecting it,
a network blip) throws an exception that is never caught, which crashes the
entire Notification.Api process — not just that one email, not just that one
consumer, the whole service. On top of that, restarting it doesn't help: the
same poisoned Kafka message gets redelivered and it crashes again,
immediately, every time. It's a genuine crash loop, not a one-off.

## Root cause

`services/notification/Notification.Api/Services/EmailService.cs`, lines 17-24:

```csharp
public async Task SendAsync(string toEmail, string subject, string body)
{
    var host = _configuration["Smtp:Host"]!;
    var port = int.Parse(_configuration["Smtp:Port"]!);   // <- throws if config missing
    var username = _configuration["Smtp:Username"]!;
    var password = _configuration["Smtp:Password"]!;
    var fromEmail = _configuration["Smtp:FromEmail"]!;
    var fromName = _configuration["Smtp:FromName"]!;
    ...
```

Every value is read with the null-forgiving `!` operator and no validation.
No `appsettings.json` in the repo has an `Smtp` section at all (checked all
of them) — it only ever worked for whoever set it up locally via their own
`dotnet user-secrets`, which never got shared or documented.

The callers don't guard against this either —
`services/notification/Notification.Api/Services/TicketCreatedConsumer.cs`
(line ~133, inside the per-message `try`) and `SlaBreachedConsumer.cs`
(~line 106) both only catch `ConsumeException` and `JsonException`. An
`ArgumentNullException` from `EmailService` isn't caught by either, so it
propagates all the way up and crashes the `BackgroundService`. Since
`HostOptions.BackgroundServiceExceptionBehavior` defaults to `StopHost`,
that takes the entire application down.

## How to see it yourself

This is fully reversible — you're only removing one local secret that isn't
part of the repo, and you can put it straight back after.

1. **Remove the SMTP port secret** (breaks the config on purpose):
   ```bash
   cd services/notification/Notification.Api
   dotnet user-secrets remove "Smtp:Port"
   ```
2. **Start (or restart) Notification.Api:**
   ```bash
   dotnet run
   ```
   It starts up fine — the crash only happens once it actually tries to send an email.
3. **Trigger an email** — create any ticket as an employee (`POST /api/Ticket`)
   so `TicketCreatedConsumer` tries to send the "ticket received" email.
4. **Watch it crash.** Within a couple seconds you'll see in the console:
   ```
   fail: Microsoft.Extensions.Hosting.Internal.Host[9]
         BackgroundService failed
         System.ArgumentNullException: Value cannot be null. (Parameter 's')
            at System.Int32.Parse(String s)
            at Notification.Api.Services.EmailService.SendAsync(...) in EmailService.cs:line 20
            at Notification.Api.Services.TicketCreatedConsumer.RunConsumerLoop(...)
   crit: Microsoft.Extensions.Hosting.Internal.Host[10]
         The HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost...
   info: Microsoft.Hosting.Lifetime[0]
         Application is shutting down...
   ```
5. **Confirm it's actually down:** `curl http://localhost:5214/health` → connection refused.
6. **Confirm the crash loop:** run `dotnet run` again — it crashes again immediately,
   on the same message, because nothing was ever successfully processed.
7. **Put the secret back** to restore your working environment:
   ```bash
   dotnet user-secrets set "Smtp:Port" "587"
   ```

### What to screenshot for the report
- The terminal showing the unhandled `ArgumentNullException` and
  `Application is shutting down.`
- A second terminal (or `curl`) showing `/health` failing right after.
- The same crash repeating on a second `dotnet run`, proving it's a loop, not
  a one-off.

## Suggested fix

- Validate SMTP config at startup (fail fast with a clear error, not a
  mid-request crash), or
- Wrap `EmailService.SendAsync` in a try/catch and let a send failure log an
  error and move on, rather than take the whole host down, and
- Change `HostOptions.BackgroundServiceExceptionBehavior` to `Ignore` for
  these consumers, or catch `Exception` (not just `ConsumeException`/
  `JsonException`) in the consumer loops — see also BUG-05, which is a
  direct consequence of this same gap.

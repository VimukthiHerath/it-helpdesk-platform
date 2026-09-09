# Kafka consumer startup crash (hotfix)

## What happened

`assignment-service`, `notification-service`, and `sla-service` on Azure
were stuck in continuous crash-restart loops (Container Apps showed
`ActivationFailed`), still incurring cost even after being scaled to
`min-replicas: 0`, because a stuck crash-loop keeps retrying regardless
of the replica floor.

## Root cause

Each service's `TicketCreatedConsumer.ExecuteAsync` (a `BackgroundService`
override) is not actually asynchronous — it never `await`s anything, and
Confluent.Kafka's `consumer.Consume(...)` is a blocking synchronous call.
Calling this method therefore runs the entire consume loop synchronously
on the same thread that's calling `BackgroundService.StartAsync()` —
which is part of the ASP.NET Core host's own startup sequence.

When Kafka (`kafka-broker`) is unreachable, `Consume()` blocks
indefinitely retrying the connection. Host startup has a timeout; once
it's hit, the pending startup task is canceled
(`TaskCanceledException`), which is unhandled and — per .NET's default
`BackgroundServiceExceptionBehavior` — takes down the entire host.
Kestrel never binds, the container's startup probe fails, Azure restarts
the container, and it crashes again on the same path. Forever.

`auth-service` and `ticket-service` don't have this problem: Ticket's
Kafka *producer* connects lazily on first publish rather than blocking
at startup, and Auth doesn't touch Kafka at all.

## Fix

`ExecuteAsync` now just kicks the existing consume loop onto a
background thread (`Task.Run(...)`) and returns immediately, so
`StartAsync` — and therefore the whole host — completes startup right
away regardless of whether Kafka is reachable yet. The consumer keeps
retrying the connection in the background exactly as before; it just no
longer blocks anything else while doing it.

Verified locally for all three services: with the local Kafka container
stopped, each service now reports `/health` as healthy within ~1 second
of starting, and the Kafka client logs connection retries in the
background without crashing the process.

## Immediate mitigation (already done, separate from the code fix)

The stuck crash-looping revisions on Azure were force-deactivated
(`az containerapp revision deactivate`) rather than relying on
`min-replicas: 0` alone, since a revision already mid-crash-loop keeps
retrying regardless of the replica floor. `min-replicas: 0` only
prevents *new* activations from being attempted for no reason — it
doesn't interrupt one that's already stuck retrying.

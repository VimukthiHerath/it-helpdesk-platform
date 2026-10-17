# Unit testing and mocking (Shamim)

**Goal:** show the Ticket service's logic tested in isolation, with parameterised cases and test doubles.

## First steps
1. Run the baseline: `./scripts/run-tests.sh unit`. The existing Ticket tests should pass, with a coverage report in `TestResults/unit/`. First start MySQL and prepare the database (see `testing/README.md`). If `dotnet ef database update` says `Table 'Tickets' already exists`, your local database was created the old way: run `services/ticket/baseline-migrations.sql` against it once (or drop `ticket_db` and run the update again). The tests still run, but the update is needed for a clean setup.
2. Read `TicketController`, `TicketStatusService`, `TicketAssignmentSyncService` and `ReportDateRangeHelper`. Most of the real rules (which statuses an agent may set, the "only your own tickets" check, the report filters) are in the **controller**, which makes them hard to test alone.
3. Pull those rules into small classes with no database or HTTP, keeping behaviour identical. The existing tests must still pass. This gives you, and the mutation tester, real logic to work on.
4. Write **parameterised** tests (`[Theory]` with `[InlineData]`) for the rules, covering the edge cases.
5. Use **test doubles** with Moq (already added to `Ticket.Api.Tests`): fake the Kafka producer to prove "a ticket is saved but the event fails, so the user gets a 503", and verify a call happened with `Verify`.
6. Keep `testing/unit/run.sh` the single command that runs it all.

## Done looks like (for the 2-minute demo)
Run the script, show a parameterised test expanding into several cases, and show one mock verifying a call.

## Be ready to explain
Stub, mock, fake and spy; arrange, act, assert; why parameterised tests; what "isolated" means; what Moq `Setup` and `Verify` do.

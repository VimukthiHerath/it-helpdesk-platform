# Ticket service unit-test coverage

This document records the scenarios and test doubles used by the Ticket service
unit-test project at
`services/ticket/Ticket.Api.Tests/UnitTest`.

## Running the tests

Run the unit-test layer from the repository root:

```bash
./scripts/run-tests.sh unit
```

The same test command is available through `testing/unit/run.sh`. A coverage
report is written to `TestResults/unit/` when the unit-test layer completes.

## Ticket unit test coverage by file

### `CreateTicketTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.CreateTicket`

Scenarios covered:

- Valid ticket creation:
  - Persists the ticket.
  - Uses the authenticated user as `CreatedBy`.
  - Publishes a `TicketCreated` Kafka event.
- Invalid model state:
  - Returns `400 Bad Request`.
  - Does not save or publish.
- Kafka publishing failure:
  - Returns `503 Service Unavailable`.
  - Includes the created ticket ID.
  - Logs the error.
- Validation error handling:
  - Ignores blank first validation errors.
  - Uses the first meaningful validation message.
  - Uses a fallback message when all errors are blank.
- Missing Kafka topic configuration:
  - Uses the default `ticket-created` topic.
  - Returns the success message.
- Invalid or missing user ID:
  - Returns `500 Internal Server Error`.
  - Returns the retry-oriented error message.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database, used for ticket persistence and queries.
- **Mock:** `ILogger<TicketController>`, `IProducer<string, string>`, and `IConfiguration`, created with Moq.
- **Stub:** The mocked Kafka producer and configuration are configured to return a topic, complete successfully, or throw a `ProduceException` for failure scenarios.
- **Spy:** The logger and producer mocks are verified to confirm logging and event publication.
- **Dummy:** `TicketCreateDTO` instances and sample ticket values used as test inputs.

---

### `GetTicketsTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.GetTickets`

Scenarios covered:

- Returns all tickets.
- Handles database/context failures with a `500` problem response.
- Returns the exact problem detail and logs the expected error.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketController>`.
- **Spy:** The logger mock is verified for error logging.
- **Dummy:** Seeded `Tickets` entities used as query data.

---

### `GetMyTicketsTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.GetMyTickets`

Scenarios covered:

- Returns only tickets created by the current user.
- Handles database/context failures.
- Uses the JWT `sub` claim before `NameIdentifier`.
- Returns the exact problem detail and logs the expected error.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketController>`.
- **Spy:** The logger mock is verified for error logging.
- **Dummy:** Seeded tickets and manually constructed JWT claims used as test inputs.

---

### `GetReportTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.GetReport`

Scenarios covered:

- Applies status, urgency, date, and assignment filters.
- Maps enum values to human-readable labels.
- Returns tickets newest first when no filters are supplied.
- Includes tickets at the start and end of the requested day.
- Returns an empty result for reversed date ranges.
- Handles database/context failures.
- Uses fallback string values for out-of-range enum values.
- Maps every supported ticket status label.
- Maps every supported urgency label.
- Returns exact error details and logs the expected error.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketController>`, `IProducer<string, string>`, and `IConfiguration`.
- **Spy:** The logger mock is verified for the expected error.
- **Dummy:** Seeded ticket entities and filter values.

---

### `GetStatusHistoryTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.GetStatusHistory`

Scenarios covered:

- Returns status history in chronological order.
- Returns `404 Not Found` when the ticket does not exist.
- Returns `403 Forbidden` when an agent is not assigned to the ticket.
- Handles database/context failures.
- Returns the exact ticket-not-found message.
- Returns the exact unauthorized-assignment message.
- Logs expected errors.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketController>`.
- **Spy:** The logger mock is verified for error logging.
- **Dummy:** Ticket/status-history entities and authenticated user claims.

---

### `UpdateStatusTests.cs`

**Source:** `Ticket.Api.Controller.TicketController.UpdateStatus`

Scenarios covered:

- Assigned agent changes a ticket status.
- Persists status history.
- Returns `404` when the ticket does not exist.
- Returns `403` when the agent is not assigned.
- Rejects statuses that are not agent-settable.
- Rejects invalid model state.
- Allows an administrator to update another user’s ticket.
- Handles database failures with `500`.
- Returns exact validation messages.
- Returns the exact invalid-status message.
- Publishes a complete `TicketResolved` event when closing a ticket.
- Uses the default Kafka topic when no topic is configured.
- Uses a configured topic for resolved tickets.
- Handles Kafka publishing failures and logs the expected message.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketController>`, `IProducer<string, string>`, and `IConfiguration`.
- **Stub:** The Kafka producer and configuration are configured with successful results, topics, or publishing exceptions.
- **Spy:** The logger and producer mocks are verified for logging and event publication.
- **Dummy:** `UpdateTicketStatusDTO`, ticket entities, user claims, and Kafka message values.

---

### `ReportsControllerTests.cs`

**Source:** `Ticket.Api.Controller.ReportsController.GetAgentPerformanceReport`

Scenarios covered:

- Rejects reversed date ranges with `400 Bad Request`.
- Includes resolutions when start and end dates are equal.
- Applies a start-date-only boundary.
- Applies an end-date-only boundary.
- Filters by:
  - Resolved/closed status.
  - Date range.
  - Assigned agent.
  - SLA eligibility.
- Excludes tickets without an assigned agent.
- Excludes duplicate, spam, cancelled, and other non-resolved statuses.
- Returns zero SLA breach rate when no SLA deadlines exist.
- Includes closed tickets.
- Counts only tickets resolved strictly after the SLA deadline as breached.
- Calculates average TTR, SLA-eligible count, missing SLA count, and breach rate.

Test doubles used:

- **Fake:** EF Core `TicketDbContext` backed by an in-memory database.
- **Dummy:** `Ticket` entities with controlled status, agent, date, and SLA values.
- **Mock/Stub/Spy:** None; the `ReportsController` and database context execute real logic.

---

### `ReportDateRangeHelperTests.cs`

**Source:** `Ticket.Api.Services.ReportDateRangeHelper.ToUtcEndBoundary`

Scenarios covered:

- Converts a date-only end value to the final instant of that UTC day:
  - `23:59:59.9999999 UTC`.

Test doubles used:

- **Dummy:** A fixed `DateTime` value used as the boundary input.
- **Mock/Stub/Fake/Spy:** None; this is a direct test of the real helper method.

---

### `TicketCreateDtoTests.cs`

**Source:** `Ticket.Api.DTO.TicketCreateDTO`

Scenarios covered:

- Validates the maximum description length:
  - 255 characters accepted.
  - 256 characters rejected.
- Validates the maximum issue type length:
  - 100 characters accepted.
  - 101 characters rejected.

Test doubles used:

- **Dummy:** `TicketCreateDTO` instances and generated strings used as validation inputs.
- **Mock/Stub/Fake/Spy:** None; validation executes against the real DTO and data-annotation rules.

---

### `TicketStatusServiceTests.cs`

**Source classes:**

- `Ticket.Api.Services.TicketStatusService`
- `Ticket.Api.Services.TicketAssignmentSyncService`

#### `TicketStatusService.ApplyStatusChangeAsync`

Scenarios covered:

- Updates the ticket status.
- Sets `UpdatedAt`.
- Creates complete status history containing:
  - Ticket ID.
  - Old status.
  - New status.
  - User who changed the status.

#### `TicketStatusService.SettableStatuses`

Scenarios covered:

- Allows:
  - `in_progress`
  - `resolved`
  - `closed`
- Rejects:
  - `unassigned`
  - `assigned`

#### `TicketAssignmentSyncService.ApplyAsync`

Scenarios covered:

- Synchronizes assignment and status when the ticket exists.
- Sets the assigned agent.
- Changes the status to assigned.
- Updates the timestamp.
- Persists the changes.
- Skips missing tickets.
- Logs warning/information messages appropriately.
- Propagates cancellation without persisting.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketAssignmentSyncService>` in assignment synchronization tests.
- **Spy:** The logger mock is verified for warning and information messages.
- **Dummy:** `Tickets`, `TicketAssignedEvent`, and cancellation-token values.
- **Stub:** No separate stub object; database state and logger behavior are supplied through the fake context and mock.

---

### `TicketAssignmentSyncServiceTests.cs`

**Source:** `Ticket.Api.Services.TicketAssignmentSyncService.ApplyAsync`

Additional scenarios covered:

- Logs the exact warning when the assigned ticket does not exist.
- Logs the exact information message when synchronization succeeds.
- Propagates `OperationCanceledException` when cancellation occurs.
- Verifies assignment and status synchronization.
- Verifies no persistence occurs for a missing ticket.

Test doubles used:

- **Fake:** EF Core `ApplicationDbContext` backed by an in-memory database.
- **Mock:** `ILogger<TicketAssignmentSyncService>`.
- **Spy:** The logger mock is verified for exact warning/information messages.
- **Dummy:** `TicketAssignedEvent`, ticket entities, and cancellation tokens.

---

### `TicketAssignedConsumerTests.cs`

**Source:** `Ticket.Api.Services.TicketAssignedConsumer.Run`

Scenarios covered:

- Processes a valid Kafka `TicketAssignedEvent`.
- Passes the event to `ITicketAssignmentSyncService`.
- Subscribes to the configured topic.
- Closes the Kafka consumer during shutdown.
- Skips empty messages.
- Skips invalid JSON messages.
- Skips JSON `null` messages.
- Uses configured Kafka bootstrap servers and topic.
- Uses default bootstrap server `localhost:9092` when configuration is missing.
- Enables auto-commit.
- Handles consumer cancellation.
- Logs shutdown information.
- Logs synchronization failures.
- Logs Kafka consume failures.

Test doubles used:

- **Mock:** `IConsumer<string, string>`, `ITicketAssignmentSyncService`, `ITicketConsumerFactory`, `IServiceScopeFactory`, `IServiceScope`, `IServiceProvider`, and `ILogger<TicketAssignedConsumer>`.
- **Stub:** Kafka consumer results, configuration values, service resolution, factory creation, and exception paths are configured with Moq callbacks, return values, or throws.
- **Spy:** Consumer subscription/close calls, synchronization calls, and logger calls are verified.
- **Dummy:** `ConsumeResult`, Kafka messages, `ConsumerConfig`, and serialized `TicketAssignedEvent` values.
- **Fake:** `TestConsumer` is a small concrete test subclass used to exercise the real consumer workflow with mocked infrastructure.

---

### `TicketControllerTestFixture.cs`

**Test infrastructure, not a test-case file.**

**Helpers provided:**

- `CreateDependencies`
  - Creates an in-memory `ApplicationDbContext`.
  - Creates mocked logger, Kafka producer, and configuration.
  - Creates `TicketStatusService`.
- `CreateController`
  - Creates a `TicketController`.
  - Configures authenticated user claims.
  - Configures role claims.
  - Sets up the controller HTTP context.

  Test doubles used:

  - **Fake:** The in-memory `ApplicationDbContext` is a persistence fake.
  - **Mock:** Logger, Kafka producer, and configuration dependencies are created with Moq.
  - **Dummy:** Optional user IDs, roles, and dependency values used to construct a controller.

  ---

### `LoggerMoqExtensions.cs`

**Test infrastructure, not a test-case file.**

**Helper provided:**

- `VerifyLog`
  - Verifies that a mocked logger was called with a specified log level and invocation count.

Test doubles used:

- **Spy:** The `Mock<ILogger<T>>` supplied to `VerifyLog` records and verifies logger interactions.
- **Mock:** The logger itself is a Moq mock.
- **Stub/Fake/Dummy:** None; this file only provides a verification helper.

---

### `TestObjectExtensions.cs`

**Test infrastructure, not a test-case file.**

**Helper provided:**

- `GetProperty`
  - Reads anonymous-object properties from controller response bodies for assertions.

Test doubles used:

- **Dummy:** Anonymous response objects supplied by controller actions.
- **Mock/Stub/Fake/Spy:** None; this file only provides an assertion helper.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sla.Api.Data;
using Sla.Api.DTO;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests;

// SLA-2 (P2) AC2: the deadline persists in the SLA database, tied to the ticket ID.
public class SlaRecordCreationServiceTests
{
    private static SlaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TicketCreatedEvent CreateEvent(int ticketId, int urgency = 0) => new()
    {
        TicketId = ticketId,
        Description = "Something is broken",
        IssueType = "Hardware",
        Urgency = urgency,
        Status = 0,
        CreatedBy = 1,
        CreatedAtUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task CreateAsync_PersistsRecordTiedToTicketId()
    {
        await using var context = CreateContext();
        var service = new SlaRecordCreationService(context, NullLogger<SlaRecordCreationService>.Instance);

        await service.CreateAsync(CreateEvent(42), "TicketCreated-0-1", CancellationToken.None);

        var record = Assert.Single(context.TicketSlas);
        Assert.Equal(42, record.TicketId);
        Assert.Equal("Active", record.Status);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEventKey_DoesNotCreateASecondRecord()
    {
        await using var context = CreateContext();
        var service = new SlaRecordCreationService(context, NullLogger<SlaRecordCreationService>.Instance);

        await service.CreateAsync(CreateEvent(42), "TicketCreated-0-1", CancellationToken.None);
        await service.CreateAsync(CreateEvent(42), "TicketCreated-0-1", CancellationToken.None); // same eventKey

        Assert.Single(context.TicketSlas);
        Assert.Single(context.ProcessedEvents);
    }
}

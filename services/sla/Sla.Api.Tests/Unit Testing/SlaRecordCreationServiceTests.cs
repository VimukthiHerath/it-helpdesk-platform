using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sla.Api.Data;
using Sla.Api.DTO;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests.UnitTesting;

public class SlaRecordCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_NewEvent_CreatesSlaAndProcessedEvent()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var createdAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync(CreateEvent(17, 2, createdAt), "TicketCreated-0-4", CancellationToken.None);

        var sla = await context.TicketSlas.SingleAsync();
        Assert.Equal(17, sla.TicketId);
        Assert.Equal(2, sla.Urgency);
        Assert.Equal(createdAt, sla.CreatedAtUtc);
        Assert.Equal(createdAt.AddHours(12), sla.DeadlineUtc);
        Assert.Equal("Active", sla.Status);
        var processed = await context.ProcessedEvents.SingleAsync();
        Assert.Equal("TicketCreated-0-4", processed.EventKey);
        Assert.True(processed.ProcessedAtUtc <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 24)]
    [InlineData(-1, 24)]
    [InlineData(4, 24)]
    public async Task CreateAsync_UrgencyBoundaryOrFallback_StoresExpectedDeadline(
        int urgency,
        int expectedHours)
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        var createdAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync(
            CreateEvent(17, urgency, createdAt),
            $"TicketCreated-{urgency}",
            CancellationToken.None);

        var sla = await context.TicketSlas.SingleAsync();
        Assert.Equal(urgency, sla.Urgency);
        Assert.Equal(createdAt.AddHours(expectedHours), sla.DeadlineUtc);
        Assert.Equal("Active", sla.Status);
    }

    [Fact]
    public async Task CreateAsync_AlreadyProcessedEvent_DoesNotCreateDuplicateState()
    {
        await using var context = CreateContext();
        context.ProcessedEvents.Add(new Models.ProcessedEvent
        {
            EventKey = "TicketCreated-0-4",
            ProcessedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var logger = new Mock<ILogger<SlaRecordCreationService>>();
        var service = CreateService(context, logger);

        await service.CreateAsync(CreateEvent(17, 2, DateTime.UtcNow), "TicketCreated-0-4", CancellationToken.None);

        Assert.Empty(context.TicketSlas);
        Assert.Equal(1, await context.ProcessedEvents.CountAsync());
        logger.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) =>
                state.ToString()!.Contains("already processed")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_CanceledDuringSave_PropagatesCancellationAndDoesNotPersist()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var service = CreateService(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(
                CreateEvent(17, 1, DateTime.UtcNow),
                "TicketCreated-0-5",
                cancellation.Token));

        await using var verificationContext = CreateContext(databaseName);
        Assert.Empty(await verificationContext.TicketSlas.ToListAsync());
        Assert.Empty(await verificationContext.ProcessedEvents.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_EmptyEventKey_IsPersistedAsProvided()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        await service.CreateAsync(
            CreateEvent(17, 1, DateTime.UtcNow),
            string.Empty,
            CancellationToken.None);

        var processed = await context.ProcessedEvents.SingleAsync();
        Assert.Equal(string.Empty, processed.EventKey);
    }

    private static SlaRecordCreationService CreateService(
        SlaDbContext context,
        Mock<ILogger<SlaRecordCreationService>>? logger = null)
    {
        return new SlaRecordCreationService(
            context,
            (logger ?? new Mock<ILogger<SlaRecordCreationService>>()).Object);
    }

    private static SlaDbContext CreateContext(string? databaseName = null)
    {
        return new SlaDbContext(new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options);
    }

    private static TicketCreatedEvent CreateEvent(int ticketId, int urgency, DateTime createdAt)
    {
        return new TicketCreatedEvent
        {
            TicketId = ticketId,
            Urgency = urgency,
            CreatedAtUtc = createdAt
        };
    }
}

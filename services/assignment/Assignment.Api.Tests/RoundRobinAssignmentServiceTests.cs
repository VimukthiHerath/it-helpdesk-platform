using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Assignment.Api.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Assignment.Api.Tests;

// SCRUM-19 (ASSIGN-4): round-robin auto-assignment. These exercise the
// business logic directly (in-memory DB, mocked Kafka producer) rather than
// through the Kafka consumer, since Assignment.Api has no HTTP endpoints to
// drive this through - see RoundRobinAssignmentService for why.
public class RoundRobinAssignmentServiceTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().Build();

    private static Mock<IProducer<string, string>> CreateProducerMock()
    {
        var mock = new Mock<IProducer<string, string>>();
        mock.Setup(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());
        return mock;
    }

    private static TicketCreatedEvent CreateTicketEvent(int ticketId) => new()
    {
        TicketId = ticketId,
        Description = "Something is broken",
        IssueType = "Hardware",
        Urgency = 0,
        Status = 0,
        CreatedBy = 1,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static RoundRobinAssignmentService CreateService(
        ApplicationDbContext context, IProducer<string, string> producer) =>
        new(context, producer, CreateConfiguration(), NullLogger<RoundRobinAssignmentService>.Instance);

    [Fact]
    public async Task AssignAsync_FirstTicket_AssignsFirstAgentInRotationOrder()
    {
        await using var context = CreateContext();
        context.Agents.AddRange(
            new Agent { UserId = 100, DisplayOrder = 0 },
            new Agent { UserId = 200, DisplayOrder = 1 });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateProducerMock().Object);

        await service.AssignAsync(CreateTicketEvent(1), CancellationToken.None);

        var assignment = Assert.Single(context.Assignments);
        var assignedAgent = await context.Agents.FindAsync(assignment.AgentId);
        Assert.Equal(100, assignedAgent!.UserId);
    }

    [Fact]
    public async Task AssignAsync_ConsecutiveTickets_RotateToDifferentAgentsInFixedOrder()
    {
        await using var context = CreateContext();
        context.Agents.AddRange(
            new Agent { UserId = 100, DisplayOrder = 0 },
            new Agent { UserId = 200, DisplayOrder = 1 },
            new Agent { UserId = 300, DisplayOrder = 2 });
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateProducerMock().Object);

        await service.AssignAsync(CreateTicketEvent(1), CancellationToken.None);
        await service.AssignAsync(CreateTicketEvent(2), CancellationToken.None);
        await service.AssignAsync(CreateTicketEvent(3), CancellationToken.None);

        var agentUserIds = await context.Assignments
            .OrderBy(a => a.Id)
            .Join(context.Agents, a => a.AgentId, ag => ag.Id, (a, ag) => ag.UserId)
            .ToListAsync();

        Assert.Equal(new[] { 100, 200, 300 }, agentUserIds);
    }

    [Fact]
    public async Task AssignAsync_AfterLastAgent_WrapsBackToFirst()
    {
        await using var context = CreateContext();
        var first = new Agent { UserId = 100, DisplayOrder = 0 };
        var last = new Agent { UserId = 200, DisplayOrder = 1 };
        context.Agents.AddRange(first, last);
        await context.SaveChangesAsync();

        var service = CreateService(context, CreateProducerMock().Object);

        await service.AssignAsync(CreateTicketEvent(1), CancellationToken.None); // -> first
        await service.AssignAsync(CreateTicketEvent(2), CancellationToken.None); // -> last
        await service.AssignAsync(CreateTicketEvent(3), CancellationToken.None); // -> wraps to first

        var thirdAssignment = await context.Assignments.OrderBy(a => a.Id).Skip(2).FirstAsync();
        Assert.Equal(first.Id, thirdAssignment.AgentId);
    }

    [Fact]
    public async Task AssignAsync_CreatesAssignmentRecord_AndPublishesTicketAssignedEvent()
    {
        await using var context = CreateContext();
        var agent = new Agent { UserId = 100, DisplayOrder = 0 };
        context.Agents.Add(agent);
        await context.SaveChangesAsync();

        var producerMock = CreateProducerMock();
        var service = CreateService(context, producerMock.Object);

        await service.AssignAsync(CreateTicketEvent(42), CancellationToken.None);

        var assignment = Assert.Single(context.Assignments);
        Assert.Equal(42, assignment.TicketId);
        Assert.Equal(agent.Id, assignment.AgentId);

        producerMock.Verify(p => p.ProduceAsync(
            "ticket-assigned",
            It.Is<Message<string, string>>(m =>
                m.Key == "42" &&
                m.Value.Contains("\"ticketId\":42") &&
                m.Value.Contains("\"agentUserId\":100")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AssignAsync_NoAgentsConfigured_SkipsAssignmentWithoutThrowing()
    {
        await using var context = CreateContext();
        var producerMock = CreateProducerMock();
        var service = CreateService(context, producerMock.Object);

        await service.AssignAsync(CreateTicketEvent(1), CancellationToken.None);

        Assert.Empty(context.Assignments);
        producerMock.Verify(p => p.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

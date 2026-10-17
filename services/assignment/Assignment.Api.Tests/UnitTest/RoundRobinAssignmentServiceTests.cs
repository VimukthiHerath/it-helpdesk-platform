using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Assignment.Api.Services;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class RoundRobinAssignmentServiceTests
{
    [Fact]
    public async Task AssignAsync_WithNoAgents_DoesNotPersistOrPublish()
    {
        await using var context = CreateContext();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.AssignAsync(CreateEvent(1), CancellationToken.None);

        Assert.Empty(context.Assignments);
        producer.Verify(x => x.ProduceAsync(
            It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_WithNoPreviousAssignment_UsesFirstDisplayOrder()
    {
        await using var context = CreateContext();
        context.Agents.AddRange(
            new Agent { Id = 1, UserId = 101, DisplayOrder = 10 },
            new Agent { Id = 2, UserId = 102, DisplayOrder = 20 });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.AssignAsync(CreateEvent(1), CancellationToken.None);

        Assert.Equal(1, (await context.Assignments.SingleAsync()).AgentId);
    }

    [Fact]
    public async Task AssignAsync_AfterLastAgent_WrapsToFirstAgent()
    {
        await using var context = CreateContext();
        context.Agents.AddRange(
            new Agent { Id = 1, UserId = 101, DisplayOrder = 0 },
            new Agent { Id = 2, UserId = 102, DisplayOrder = 1 });
        context.Assignments.Add(new TicketAssignment { Id = 1, TicketId = 10, AgentId = 2, Urgency = 1 });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.AssignAsync(CreateEvent(11), CancellationToken.None);

        Assert.Equal(1, (await context.Assignments.OrderByDescending(x => x.Id).FirstAsync()).AgentId);
    }

    [Fact]
    public async Task AssignAsync_WhenPreviousAgentWasRemoved_RestartsAtFirstAgent()
    {
        await using var context = CreateContext();
        context.Agents.AddRange(
            new Agent { Id = 1, UserId = 101, DisplayOrder = 0 },
            new Agent { Id = 2, UserId = 102, DisplayOrder = 1 });
        context.Assignments.Add(new TicketAssignment { Id = 1, TicketId = 10, AgentId = 99, Urgency = 1 });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.AssignAsync(CreateEvent(11), CancellationToken.None);

        Assert.Equal(1, (await context.Assignments.OrderByDescending(x => x.Id).FirstAsync()).AgentId);
    }

    [Fact]
    public async Task AssignAsync_WithSingleAgent_AlwaysUsesThatAgent()
    {
        await using var context = CreateContext();
        context.Agents.Add(new Agent { Id = 1, UserId = 101, DisplayOrder = 0 });
        await context.SaveChangesAsync();
        var producer = CreateProducer();
        var service = CreateService(context, producer);

        await service.AssignAsync(CreateEvent(1), CancellationToken.None);
        await service.AssignAsync(CreateEvent(2), CancellationToken.None);

        Assert.All(context.Assignments, assignment => Assert.Equal(1, assignment.AgentId));
    }

    private static TicketCreatedEvent CreateEvent(int ticketId) => new()
    {
        TicketId = ticketId,
        Urgency = 0,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static RoundRobinAssignmentService CreateService(
        ApplicationDbContext context, Mock<IProducer<string, string>> producer) =>
        new(
            context,
            producer.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:TicketAssignedTopic"] = "ticket-assigned"
            }).Build(),
            new Mock<ILogger<RoundRobinAssignmentService>>().Object);

    private static Mock<IProducer<string, string>> CreateProducer()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer.Setup(x => x.ProduceAsync(
                It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryResult<string, string>)null!);
        return producer;
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

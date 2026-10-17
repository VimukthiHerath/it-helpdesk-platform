using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class ReportsControllerTests
{
    [Fact]
    public async Task Report_WhenDateRangeIsReversed_ReturnsBadRequest()
    {
        await using var context = CreateReportContext();
        var result = await new ReportsController(context).GetAgentPerformanceReport(
            new DateTime(2026, 10, 18), new DateTime(2026, 10, 17), CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Start date cannot be after end date.",
            TestObjectExtensions.GetProperty(response.Value!, "message"));
    }

    [Fact]
    public async Task Report_WhenDatesAreEqual_IncludesBoundaryResolution()
    {
        await using var context = CreateReportContext();
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        context.Tickets.Add(Resolved(1, "agent-a", day, day, day));
        await context.SaveChangesAsync();

        var result = await new ReportsController(context).GetAgentPerformanceReport(
            day, day, CancellationToken.None);

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(result).Value));
    }

    [Fact]
    public async Task Report_WhenOnlyOneDateIsSupplied_AppliesThatBoundary()
    {
        await using var context = CreateReportContext();
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        context.Tickets.AddRange(
            Resolved(1, "agent-a", day, day, null),
            Resolved(2, "agent-a", day.AddDays(1), day.AddDays(1).AddHours(1), null));
        await context.SaveChangesAsync();

        var startResult = await new ReportsController(context).GetAgentPerformanceReport(
            day, null, CancellationToken.None);
        var endResult = await new ReportsController(context).GetAgentPerformanceReport(
            null, day, CancellationToken.None);

        Assert.Equal(2, Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(startResult).Value)).TotalResolvedTickets);
        Assert.Equal(1, Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(endResult).Value)).TotalResolvedTickets);
    }

    [Fact]
    public async Task Report_FiltersStatusesDatesAssignmentsAndExclusions()
    {
        await using var context = CreateReportContext();
        var start = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        context.Tickets.AddRange(
            Resolved(1, "agent-a", start.AddHours(1), start.AddHours(2), start.AddHours(3)),
            Resolved(2, "agent-a", start.AddHours(2), start.AddHours(5), start.AddHours(4)),
            Resolved(3, "agent-a", start.AddHours(3), start.AddHours(4), null),
            new Ticket.Api.Model.Ticket { Id = 4, AssignedAgentId = null, Status = "Resolved", CreatedAtUtc = start, ResolvedAtUtc = start.AddHours(1) },
            new Ticket.Api.Model.Ticket { Id = 5, AssignedAgentId = "agent-a", Status = "Duplicate", CreatedAtUtc = start, ResolvedAtUtc = start.AddHours(1) },
            new Ticket.Api.Model.Ticket { Id = 6, AssignedAgentId = "agent-a", Status = "Open", CreatedAtUtc = start, ResolvedAtUtc = start.AddHours(1) },
            new Ticket.Api.Model.Ticket { Id = 7, AssignedAgentId = "agent-a", Status = "Closed", CreatedAtUtc = start.AddDays(-1), ResolvedAtUtc = start.AddDays(-1) });
        await context.SaveChangesAsync();

        var result = await new ReportsController(context).GetAgentPerformanceReport(
            start, start.AddDays(1), CancellationToken.None);

        var report = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(result).Value));
        Assert.Equal("agent-a", report.AgentId);
        Assert.Equal(3, report.TotalResolvedTickets);
        Assert.Equal(2, report.SlaEligibleTickets);
        Assert.Equal(1, report.TicketsMissingSla);
        Assert.Equal(100, report.AverageTtrMinutes);
        Assert.Equal(50, report.SlaBreachRate);
    }

    [Fact]
    public async Task Report_WhenNoSlaDeadlines_ReturnsZeroBreachRate()
    {
        await using var context = CreateReportContext();
        var now = DateTime.UtcNow;
        context.Tickets.Add(Resolved(1, "agent-a", now.AddMinutes(-30), now, null));
        await context.SaveChangesAsync();

        var result = await new ReportsController(context).GetAgentPerformanceReport(
            null, null, CancellationToken.None);

        var report = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(result).Value));
        Assert.Equal(30, report.AverageTtrMinutes);
        Assert.Equal(0, report.SlaBreachRate);
    }

    [Fact]
    public async Task Report_IncludesClosedTicketsAndExcludesEveryNonResolvedStatus()
    {
        await using var context = CreateReportContext();
        var now = DateTime.UtcNow;
        context.Tickets.AddRange(
            Resolved(1, "closed-agent", now.AddHours(-2), now.AddHours(-1), null, "Closed"),
            Resolved(2, "excluded-agent", now.AddHours(-2), now.AddHours(-1), null, "Duplicate"),
            Resolved(3, "excluded-agent", now.AddHours(-2), now.AddHours(-1), null, "Spam"),
            Resolved(4, "excluded-agent", now.AddHours(-2), now.AddHours(-1), null, "Cancelled"));
        await context.SaveChangesAsync();

        var result = await new ReportsController(context).GetAgentPerformanceReport(
            null, null, CancellationToken.None);

        var report = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(result).Value));
        Assert.Equal("closed-agent", report.AgentId);
        Assert.Equal(1, report.TotalResolvedTickets);
    }

    [Fact]
    public async Task Report_CountsOnlyStrictlyLateSlaTicketsAsBreaches()
    {
        await using var context = CreateReportContext();
        var now = DateTime.UtcNow;
        context.Tickets.AddRange(
            Resolved(1, "agent-a", now.AddHours(-3), now, now),
            Resolved(2, "agent-a", now.AddHours(-3), now, now.AddMinutes(1)));
        await context.SaveChangesAsync();

        var result = await new ReportsController(context).GetAgentPerformanceReport(
            null, null, CancellationToken.None);

        var report = Assert.Single(Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(
            Assert.IsType<OkObjectResult>(result).Value));
        Assert.Equal(0, report.SlaBreachRate);
    }

    private static Ticket.Api.Model.Ticket Resolved(
        int id, string agent, DateTime created, DateTime resolved, DateTime? deadline,
        string status = "Resolved") =>
        new() { Id = id, AssignedAgentId = agent, Status = status, CreatedAtUtc = created,
            ResolvedAtUtc = resolved, SlaDeadlineUtc = deadline };

    private static TicketDbContext CreateReportContext() =>
        new(new DbContextOptionsBuilder<TicketDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

public class TicketAssignmentSyncServiceAdditionalTests
{
    [Fact]
    public async Task Apply_WhenTicketIsMissing_LogsExactWarningMessage()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 404, AgentUserId = 42 }, CancellationToken.None);

        VerifyLog(logger, LogLevel.Warning,
            "Received TicketAssigned for ticket 404, but no such ticket exists. Skipping.");
    }

    [Fact]
    public async Task Apply_WhenTicketExists_LogsExactInformationMessage()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Tickets.Add(new Tickets { Id = 12, Description = "Issue", IssueType = "Network" });
        await context.SaveChangesAsync();
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
            new TicketAssignedEvent { TicketId = 12, AgentUserId = 42 }, CancellationToken.None);

        VerifyLog(logger, LogLevel.Information,
            "Synced ticket 12: now assigned to agent user 42");
    }

    [Fact]
    public async Task Apply_WhenCancellationOccurs_DoesNotPersistAndPropagatesCancellation()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Tickets.Add(new Tickets { Id = 1, Description = "Issue", IssueType = "Network" });
        await context.SaveChangesAsync();
        var cancellation = new CancellationToken(true);
        var logger = new Mock<ILogger<TicketAssignmentSyncService>>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TicketAssignmentSyncService(context, logger.Object).ApplyAsync(
                new TicketAssignedEvent { TicketId = 1, AgentUserId = 9 }, cancellation));
    }

    private static void VerifyLog(
        Mock<ILogger<TicketAssignmentSyncService>> logger,
        LogLevel level,
        string text) =>
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(text)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
}

public class TicketAssignedConsumerTests
{
    [Fact]
    public async Task Consumer_ProcessesValidEventAndClosesConsumerOnCancellation()
    {
        var sync = new Mock<ITicketAssignmentSyncService>(MockBehavior.Strict);
        sync.Setup(x => x.ApplyAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var assignedAt = new DateTime(2026, 10, 17, 12, 30, 0, DateTimeKind.Utc);
        var consumer = CreateConsumer(
            new ConsumeResult<string, string>
            {
                Partition = new Partition(3),
                Offset = new Offset(17),
                Message = new Message<string, string>
                {
                    Value = JsonSerializer.Serialize(new TicketAssignedEvent
                    {
                        TicketId = 7, AgentUserId = 42, AssignedAtUtc = assignedAt
                    })
                }
            });
        var scopeProvider = CreateScopeProvider(sync.Object);
        var logger = new Mock<ILogger<TicketAssignedConsumer>>();
        var service = CreateService(consumer.Object, scopeProvider.Object, logger);

        await service.Run(CancellationToken.None);

        sync.Verify(x => x.ApplyAsync(It.Is<TicketAssignedEvent>(
            e => e.TicketId == 7 && e.AgentUserId == 42), It.IsAny<CancellationToken>()), Times.Once);
        consumer.Verify(x => x.Subscribe("ticket-assigned"), Times.Once);
        consumer.Verify(x => x.Close(), Times.Once);
        VerifyLog(logger, LogLevel.Information,
            "TicketId=7, AgentUserId=42");
        VerifyLog(logger, LogLevel.Information, "AssignedAtUtc=");
        VerifyLog(logger, LogLevel.Information, "AssignedAtUtc=10/17/2026 12:30:00, Partition=[3], Offset=17");
    }

    [Fact]
    public async Task Consumer_SkipsEmptyAndInvalidMessages()
    {
        var sync = new Mock<ITicketAssignmentSyncService>(MockBehavior.Strict);
        var consumer = CreateConsumer(
            new ConsumeResult<string, string> { Message = new Message<string, string> { Value = "" } },
            new ConsumeResult<string, string> { Message = new Message<string, string> { Value = "{invalid" } },
            new ConsumeResult<string, string> { Message = new Message<string, string> { Value = "null" } });
        var scopeProvider = CreateScopeProvider(sync.Object);
        var consumerLogger = new Mock<ILogger<TicketAssignedConsumer>>();

        await CreateService(consumer.Object, scopeProvider.Object, consumerLogger).Run(CancellationToken.None);

        sync.Verify(x => x.ApplyAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyLog(consumerLogger, LogLevel.Warning, "Received an empty TicketAssigned message.");
        VerifyLog(consumerLogger, LogLevel.Warning, "Received an invalid TicketAssigned event.");
        VerifyLog(consumerLogger, LogLevel.Warning, "Received malformed TicketAssigned event");
    }

    [Fact]
    public async Task Consumer_UsesConfiguredKafkaSettingsAndLogsShutdown()
    {
        var sync = new Mock<ITicketAssignmentSyncService>(MockBehavior.Strict);
        var consumer = CreateConsumer();
        var scopeProvider = CreateScopeProvider(sync.Object);
        var logger = new Mock<ILogger<TicketAssignedConsumer>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "broker:9093",
                ["Kafka:TicketAssignedTopic"] = "assignments"
            }).Build();
        var factory = new Mock<ITicketConsumerFactory>();
        ConsumerConfig? captured = null;
        factory.Setup(x => x.Create(It.IsAny<ConsumerConfig>()))
            .Callback<ConsumerConfig>(config => captured = config)
            .Returns(consumer.Object);
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(x => x.CreateScope()).Returns(scopeProvider.Object);
        var service = new TestConsumer(configuration, logger.Object, scopes.Object, factory.Object);

        await service.Run(CancellationToken.None);

        Assert.Equal("broker:9093", captured!.BootstrapServers);
        Assert.Equal("assignments", consumer.Invocations.First(x => x.Method.Name == nameof(IConsumer<string, string>.Subscribe)).Arguments[0]);
        VerifyLog(logger, LogLevel.Information, "Ticket consumer started. Topic=assignments");
        VerifyLog(logger, LogLevel.Information, "GroupId=ticket-workers");
        VerifyLog(logger, LogLevel.Information, "BootstrapServers=broker:9093");
    }

    [Fact]
    public async Task Consumer_WhenBootstrapServerIsMissing_UsesDefaultAndEnablesAutoCommit()
    {
        var sync = new Mock<ITicketAssignmentSyncService>(MockBehavior.Strict);
        var consumer = CreateConsumer();
        var scopeProvider = CreateScopeProvider(sync.Object);
        var logger = new Mock<ILogger<TicketAssignedConsumer>>();
        var factory = new Mock<ITicketConsumerFactory>();
        ConsumerConfig? captured = null;
        factory.Setup(x => x.Create(It.IsAny<ConsumerConfig>()))
            .Callback<ConsumerConfig>(config => captured = config)
            .Returns(consumer.Object);
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(x => x.CreateScope()).Returns(scopeProvider.Object);
        var service = new TestConsumer(new ConfigurationBuilder().Build(), logger.Object,
            scopes.Object, factory.Object);

        await service.Run(CancellationToken.None);

        Assert.Equal("localhost:9092", captured!.BootstrapServers);
        Assert.True(captured.EnableAutoCommit);
    }

    [Fact]
    public async Task Consumer_WhenConsumeIsCanceled_LogsShutdownMessage()
    {
        var cancellation = new CancellationTokenSource();
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.Setup(x => x.Consume(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => cancellation.Cancel())
            .Throws<OperationCanceledException>();
        var scopeProvider = CreateScopeProvider(
            new Mock<ITicketAssignmentSyncService>(MockBehavior.Strict).Object);
        var logger = new Mock<ILogger<TicketAssignedConsumer>>();
        var service = CreateService(consumer.Object, scopeProvider.Object, logger);

        await service.Run(cancellation.Token);

        VerifyLog(logger, LogLevel.Information, "Ticket Kafka consumer is shutting down.");
        consumer.Verify(x => x.Close(), Times.Once);
    }

    [Fact]
    public async Task Consumer_LogsSyncFailuresAndConsumeFailures()
    {
        var sync = new Mock<ITicketAssignmentSyncService>();
        sync.Setup(x => x.ApplyAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sync failed"));
        var consumer = CreateConsumer(
            new ConsumeResult<string, string>
            {
                Message = new Message<string, string>
                {
                    Value = JsonSerializer.Serialize(new TicketAssignedEvent { TicketId = 7, AgentUserId = 42 })
                }
            },
            new ConsumeException(
                new ConsumeResult<byte[], byte[]>
                {
                    Message = new Message<byte[], byte[]>()
                }, new Error(ErrorCode.Local_Fail)));
        var scopeProvider = CreateScopeProvider(sync.Object);
        var logger = new Mock<ILogger<TicketAssignedConsumer>>();

        await CreateService(consumer.Object, scopeProvider.Object, logger).Run(CancellationToken.None);

        logger.VerifyLog(LogLevel.Error, Times.AtLeast(2));
        VerifyLog(logger, LogLevel.Information, "Received TicketAssigned event.");
        VerifyLog(logger, LogLevel.Error, "Failed to sync assignment for ticket 7");
        VerifyLog(logger, LogLevel.Error, "Kafka consume error:");
    }

    private static Mock<IConsumer<string, string>> CreateConsumer(params object[] actions)
    {
        var mock = new Mock<IConsumer<string, string>>();
        var index = 0;
        mock.Setup(x => x.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (index >= actions.Length)
                    throw new OperationCanceledException();
                var action = actions[index++];
                if (action is Exception exception)
                    throw exception;
                return (ConsumeResult<string, string>)action;
            });
        return mock;
    }

    private static Mock<IServiceScope> CreateScopeProvider(ITicketAssignmentSyncService sync)
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(x => x.GetService(typeof(ITicketAssignmentSyncService))).Returns(sync);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(x => x.ServiceProvider).Returns(provider.Object);
        return scope;
    }

    private static TestConsumer CreateService(
        IConsumer<string, string> consumer,
        IServiceScope scope,
        Mock<ILogger<TicketAssignedConsumer>>? logger = null)
    {
        var factory = new Mock<ITicketConsumerFactory>();
        factory.Setup(x => x.Create(It.IsAny<ConsumerConfig>())).Returns(consumer);
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(x => x.CreateScope()).Returns(scope);
        var configuration = new ConfigurationBuilder().Build();
        return new TestConsumer(configuration, logger?.Object ?? Mock.Of<ILogger<TicketAssignedConsumer>>(),
            scopes.Object, factory.Object);
    }

    private sealed class TestConsumer : TicketAssignedConsumer
    {
        public TestConsumer(IConfiguration configuration, ILogger<TicketAssignedConsumer> logger,
            IServiceScopeFactory scopes, ITicketConsumerFactory factory)
            : base(configuration, logger, scopes, factory) { }

        public async Task Run(CancellationToken token)
        {
            try
            {
                await base.ExecuteAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void VerifyLog(Mock<ILogger<TicketAssignedConsumer>> logger, LogLevel level, string text) =>
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(text)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
}

internal static class TestObjectExtensions
{
    internal static object? GetProperty(object value, string name) =>
        value.GetType().GetProperty(name)!.GetValue(value);
}

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

        VerifyLog(logger, LogLevel.Error, Times.AtLeast(2));
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

    private static void VerifyLog(
        Mock<ILogger<TicketAssignedConsumer>> logger,
        LogLevel level,
        Times times) =>
        logger.Verify(x => x.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class GetReportTests
{
    [Fact]
    public async Task GetReport_AppliesFiltersAndMapsHumanReadableLabels()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var baseDate = DateTime.UtcNow.Date;
        context.Tickets.AddRange(
            new Tickets
            {
                Id = 1, Description = "Matching", IssueType = "Network", CreatedBy = 1,
                Status = TicketStatus.resolved, Urgency = TicketUrgency.six_hours,
                CreatedAt = baseDate.AddHours(10), AssignedTo = 42
            },
            new Tickets
            {
                Id = 2, Description = "Different", IssueType = "Hardware", CreatedBy = 2,
                Status = TicketStatus.closed, Urgency = TicketUrgency.six_hours,
                CreatedAt = baseDate.AddHours(11)
            });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<OkObjectResult>(await controller.GetReport(
            (int)TicketStatus.resolved, (int)TicketUrgency.six_hours, baseDate, baseDate));

        var report = Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(result.Value);
        var item = Assert.Single(report);
        Assert.Equal(1, item.TicketId);
        Assert.Equal("Resolved", item.Status);
        Assert.Equal("Within 6 hours", item.Urgency);
        Assert.Equal(42, item.AssignedAgent);
    }

    [Fact]
    public async Task GetReport_WhenNoFiltersAreSupplied_ReturnsNewestFirst()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.AddRange(
            new Tickets { Id = 1, Description = "Old", IssueType = "Network", CreatedBy = 1, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new Tickets { Id = 2, Description = "New", IssueType = "Network", CreatedBy = 1, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<OkObjectResult>(await controller.GetReport(null, null, null, null));

        var report = Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(result.Value);
        Assert.Equal(new[] { 2, 1 }, report.Select(item => item.TicketId));
    }

    [Fact]
    public async Task GetReport_DateRangeIncludesStartAndEndOfDayBoundaries()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        var day = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        context.Tickets.AddRange(
            new Tickets
            {
                Id = 1, Description = "At start", IssueType = "Network", CreatedBy = 1,
                CreatedAt = day
            },
            new Tickets
            {
                Id = 2, Description = "At end", IssueType = "Network", CreatedBy = 1,
                CreatedAt = day.AddDays(1).AddTicks(-1)
            },
            new Tickets
            {
                Id = 3, Description = "Outside", IssueType = "Network", CreatedBy = 1,
                CreatedAt = day.AddDays(1)
            });
        await context.SaveChangesAsync();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<OkObjectResult>(await controller.GetReport(null, null, day, day));

        var report = Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(result.Value);
        Assert.Equal(new[] { 2, 1 }, report.Select(item => item.TicketId));
    }

    [Fact]
    public async Task GetReport_WhenDateRangeIsReversed_ReturnsEmptyResult()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 1, Description = "Ticket", IssueType = "Network", CreatedBy = 1,
            CreatedAt = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<OkObjectResult>(await controller.GetReport(
            null, null,
            new DateTime(2026, 10, 18),
            new DateTime(2026, 10, 17)));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(result.Value));
    }

    [Fact]
    public async Task GetReport_WhenContextFails_ReturnsProblemAndLogsError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService, userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<ObjectResult>(await controller.GetReport(null, null, null, null));

        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task GetReport_WhenEnumValuesAreOutOfRange_UsesEnumNamesAsFallbackLabels()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.Add(new Tickets
        {
            Id = 99, Description = "Unknown values", CreatedBy = 1,
            Status = (TicketStatus)99, Urgency = (TicketUrgency)99
        });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetReport(null, null, null, null));

        var item = Assert.Single(
            Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(result.Value));
        Assert.Equal("99", item.Status);
        Assert.Equal("99", item.Urgency);
    }

    [Fact]
    public async Task GetReport_MapsEveryStatusAndUrgencyLabel()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Tickets.AddRange(
            new Tickets { Id = 10, Description = "Unassigned", Status = TicketStatus.unassigned, Urgency = TicketUrgency.one_hour },
            new Tickets { Id = 11, Description = "Assigned", Status = TicketStatus.assigned, Urgency = TicketUrgency.six_hours },
            new Tickets { Id = 12, Description = "Resolved", Status = TicketStatus.resolved, Urgency = TicketUrgency.twelve_hours },
            new Tickets { Id = 13, Description = "In progress", Status = TicketStatus.in_progress, Urgency = TicketUrgency.twenty_four_hours },
            new Tickets { Id = 14, Description = "Closed", Status = TicketStatus.closed, Urgency = TicketUrgency.one_hour });
        context.SaveChanges();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = await controller.GetReport(null, null, null, null);
        var report = Assert.IsAssignableFrom<IEnumerable<TicketReportItemDTO>>(
            Assert.IsType<OkObjectResult>(result).Value);
        var byId = report.ToDictionary(item => item.TicketId);

        Assert.Equal("Unassigned", byId[10].Status);
        Assert.Equal("Assigned", byId[11].Status);
        Assert.Equal("Resolved", byId[12].Status);
        Assert.Equal("In Progress", byId[13].Status);
        Assert.Equal("Closed", byId[14].Status);
        Assert.Equal("Within 1 hour", byId[10].Urgency);
        Assert.Equal("Within 6 hours", byId[11].Urgency);
        Assert.Equal("Within 12 hours", byId[12].Urgency);
        Assert.Equal("Within 24 hours", byId[13].Urgency);
    }

    [Fact]
    public async Task GetReport_WhenContextFails_ReturnsExactProblemAndLogsExpectedError()
    {
        var (context, logger, producer, configuration, statusService) =
            TicketControllerTestFixture.CreateDependencies();
        context.Dispose();
        var controller = TicketControllerTestFixture.CreateController(
            context, logger, producer, configuration, statusService,
            userId: 42, roles: new[] { "Administrator" });

        var result = Assert.IsType<ObjectResult>(
            await controller.GetReport(null, null, null, null));

        Assert.Equal("Unable to generate report. Please try again later.",
            Assert.IsType<ProblemDetails>(result.Value).Detail);
        VerifyLog(logger, LogLevel.Error, "Error generating ticket report");
    }

    private static void VerifyLog(Mock<ILogger<TicketController>> logger, LogLevel level, string message)
    {
        var prefix = message.Split('{')[0].TrimEnd();
        logger.Verify(x => x.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.StartsWith(prefix)),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}

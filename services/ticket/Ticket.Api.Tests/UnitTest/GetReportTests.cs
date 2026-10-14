using Microsoft.AspNetCore.Mvc;
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
}

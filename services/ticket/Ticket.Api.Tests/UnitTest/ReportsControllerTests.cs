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
            GetProperty(response.Value!, "message"));
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

    private static object? GetProperty(object value, string name) =>
        value.GetType().GetProperty(name)!.GetValue(value);
}

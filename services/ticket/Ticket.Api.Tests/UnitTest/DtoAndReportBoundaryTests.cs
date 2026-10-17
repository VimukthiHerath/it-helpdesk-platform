using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests.UnitTest;

public class DtoAndReportBoundaryTests
{
    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void TicketCreate_DescriptionLengthBoundary_IsValidated(int length, bool valid)
    {
        var dto = new TicketCreateDTO
        {
            Description = new string('d', length),
            IssueType = "Network",
            Urgency = TicketUrgency.one_hour
        };

        Assert.Equal(valid, Validate(dto).Count == 0);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void TicketCreate_IssueTypeLengthBoundary_IsValidated(int length, bool valid)
    {
        var dto = new TicketCreateDTO
        {
            Description = "Issue",
            IssueType = new string('i', length),
            Urgency = TicketUrgency.one_hour
        };

        Assert.Equal(valid, Validate(dto).Count == 0);
    }

    [Theory]
    [InlineData(TicketStatus.in_progress, true)]
    [InlineData(TicketStatus.resolved, true)]
    [InlineData(TicketStatus.closed, true)]
    [InlineData(TicketStatus.unassigned, false)]
    [InlineData(TicketStatus.assigned, false)]
    public void SettableStatuses_ContainsOnlyAgentSettableValues(TicketStatus status, bool expected) =>
        Assert.Equal(expected, TicketStatusService.SettableStatuses.Contains(status));

    [Fact]
    public void ReportDateRange_EndBoundaryIncludesTheEntireDate()
    {
        var end = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

        var result = ReportDateRangeHelper.ToUtcEndBoundary(end);

        Assert.Equal(new DateTime(2026, 10, 17, 23, 59, 59, 999, DateTimeKind.Utc)
            .AddTicks(9999), result);
    }

    private static IList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}

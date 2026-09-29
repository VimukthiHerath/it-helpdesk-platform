using Ticket.Api.Services;
using Xunit;

namespace Ticket.Api.Tests;

// REPORT-1 (P2) AC3: filterable by status, urgency, date range.
// This targets the date-range boundary computation specifically - see
// docs/TEMP_BUGS_SPRINT3/BUG-07. AC1 (admin-only access) is enforced by the
// [Authorize] attribute, which is framework middleware rather than logic
// inside GetReport itself, and is already covered by the manual/live
// verification in the QA report (a non-admin caller correctly receives 403
// against the real running service).
//
// Note: this bug is inherently host-timezone-dependent - that's the whole
// defect. This test fails on any machine whose local timezone offset isn't
// exactly zero (true of most developer laptops, and true of the environment
// this bug was originally found on: UTC+5:30). On a server actually
// configured for UTC, this specific failure mode cannot occur - which is a
// correct, meaningful result, not a flaw in the test.
public class TicketReportDateFilterTests
{
    [Fact]
    public void ToUtcStartBoundary_UnspecifiedKindInput_ShouldBeTreatedAsUtc()
    {
        // A date-only query string value model-binds with DateTimeKind.Unspecified.
        // AC3 needs "2026-09-29" to mean midnight UTC on that date - not
        // midnight in whatever timezone the host server happens to be set to.
        var input = DateTime.SpecifyKind(new DateTime(2026, 9, 29), DateTimeKind.Unspecified);
        var expectedUtcBoundary = DateTime.SpecifyKind(input, DateTimeKind.Utc);

        var actualBoundary = ReportDateRangeHelper.ToUtcStartBoundary(input);

        Assert.Equal(expectedUtcBoundary, actualBoundary);
    }

    [Fact]
    public void ToUtcEndBoundary_UnspecifiedKindInput_ShouldBeTreatedAsUtc()
    {
        var input = DateTime.SpecifyKind(new DateTime(2026, 9, 29), DateTimeKind.Unspecified);
        var expectedUtcBoundary = DateTime.SpecifyKind(input, DateTimeKind.Utc).AddDays(1).AddTicks(-1);

        var actualBoundary = ReportDateRangeHelper.ToUtcEndBoundary(input);

        Assert.Equal(expectedUtcBoundary, actualBoundary);
    }
}

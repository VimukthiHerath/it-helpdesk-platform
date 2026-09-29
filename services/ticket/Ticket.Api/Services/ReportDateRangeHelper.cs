namespace Ticket.Api.Services;

// REPORT-1 AC3: computes the UTC boundaries used to filter the ticket report
// by date range. Extracted from TicketController.GetReport so the boundary
// computation - and its known bug (see docs/TEMP_BUGS_SPRINT3/BUG-07) - can
// be unit-tested deterministically, independent of any ticket data.
public static class ReportDateRangeHelper
{
    public static DateTime ToUtcStartBoundary(DateTime startDate) => startDate.ToUniversalTime();

    public static DateTime ToUtcEndBoundary(DateTime endDate) => endDate.ToUniversalTime().AddDays(1).AddTicks(-1);
}

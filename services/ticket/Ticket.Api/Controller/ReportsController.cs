using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ticket.Api.Data;
using Ticket.Api.DTO;

namespace Ticket.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Administrator")]
public class ReportsController : ControllerBase
{
    private readonly TicketDbContext _context;

    public ReportsController(TicketDbContext context)
    {
        _context = context;
    }

    [HttpGet("agent-performance")]
    public async Task<IActionResult> GetAgentPerformanceReport(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return BadRequest(new { message = "Start date cannot be after end date." });
        }

        // Apply BUG-6 UTC Normalization Fix
        var startUtc = startDate.HasValue 
            ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) 
            : DateTime.MinValue.ToUniversalTime();
            
        var endUtc = endDate.HasValue 
            ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) 
            : DateTime.MaxValue.ToUniversalTime();

        var ticketsRef = await _context.Tickets
            .Where(t => t.AssignedAgentId != null)
            .Where(t => t.Status == "Resolved" || t.Status == "Closed")
            // Strict exclusion just in case "Duplicate", "Spam", "Cancelled" have "Closed" overlap behavior mapped externally
            .Where(t => t.Status != "Duplicate" && t.Status != "Spam" && t.Status != "Cancelled")
            .Where(t => t.ResolvedAtUtc != null && t.ResolvedAtUtc.Value >= startUtc && t.ResolvedAtUtc.Value <= endUtc)
            .ToListAsync(cancellationToken);

        var report = ticketsRef
            .GroupBy(t => t.AssignedAgentId!)
            .Select(g =>
            {
                var totalResolved = g.Count();

                if (totalResolved == 0)
                {
                    return new AgentPerformanceReportDto
                    {
                        AgentId = g.Key,
                        TotalResolvedTickets = 0,
                        AverageTtrMinutes = null,
                        SlaBreachRate = 0.00
                    };
                }

                var totalTtrMinutes = g.Sum(t => (t.ResolvedAtUtc!.Value - t.CreatedAtUtc).TotalMinutes);
                var avgTtr = totalTtrMinutes / totalResolved;
                avgTtr = Math.Round(avgTtr, 2);

                var breachedCount = g.Count(t => t.SlaDeadlineUtc.HasValue && t.ResolvedAtUtc!.Value > t.SlaDeadlineUtc.Value);
                var breachRate = Math.Round(((double)breachedCount / totalResolved) * 100, 2);

                return new AgentPerformanceReportDto
                {
                    AgentId = g.Key,
                    TotalResolvedTickets = totalResolved,
                    AverageTtrMinutes = avgTtr,
                    SlaBreachRate = breachRate
                };
            })
            .ToList();

        return Ok(report);
    }
}

namespace Ticket.Api.DTO;

public class AgentPerformanceReportDto
{
    public string AgentId { get; set; } = string.Empty;
    public int TotalResolvedTickets { get; set; }
    public double? AverageTtrMinutes { get; set; }
    public double SlaBreachRate { get; set; } // Percentage (0.00 to 100.00)
}

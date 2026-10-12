using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests;

// SLA-2 (P2) AC1: 1h / 6h / 12h / 24h urgency maps to the correct deadline timestamp.
public class SlaDeadlineCalculatorTests
{
    [Fact]
    public void ComputeDeadline_Urgency0_SetsDeadline1HourFromCreation()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var deadline = SlaDeadlineCalculator.ComputeDeadline(createdAt, urgency: 0);

        Assert.Equal(createdAt.AddHours(1), deadline);
    }

    [Fact]
    public void ComputeDeadline_Urgency1_SetsDeadline6HoursFromCreation()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var deadline = SlaDeadlineCalculator.ComputeDeadline(createdAt, urgency: 1);

        Assert.Equal(createdAt.AddHours(6), deadline);
    }

    [Fact]
    public void ComputeDeadline_Urgency2_SetsDeadline12HoursFromCreation()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var deadline = SlaDeadlineCalculator.ComputeDeadline(createdAt, urgency: 2);

        Assert.Equal(createdAt.AddHours(12), deadline);
    }

    [Fact]
    public void ComputeDeadline_Urgency3_SetsDeadline24HoursFromCreation()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var deadline = SlaDeadlineCalculator.ComputeDeadline(createdAt, urgency: 3);

        Assert.Equal(createdAt.AddHours(24), deadline);
    }
}

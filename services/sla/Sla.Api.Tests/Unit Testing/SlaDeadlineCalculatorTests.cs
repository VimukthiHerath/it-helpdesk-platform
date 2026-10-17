using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests.UnitTesting;

public class SlaDeadlineCalculatorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 6)]
    [InlineData(2, 12)]
    [InlineData(3, 24)]
    [InlineData(-1, 24)]
    [InlineData(99, 24)]
    public void DurationFor_ReturnsExpectedDuration(int urgency, int expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), SlaDeadlineCalculator.DurationFor(urgency));
    }

    [Fact]
    public void ComputeDeadline_AddsUrgencyDurationToCreationTime()
    {
        var createdAt = new DateTime(2026, 10, 17, 8, 30, 0, DateTimeKind.Utc);

        var deadline = SlaDeadlineCalculator.ComputeDeadline(createdAt, 2);

        Assert.Equal(createdAt.AddHours(12), deadline);
    }
}

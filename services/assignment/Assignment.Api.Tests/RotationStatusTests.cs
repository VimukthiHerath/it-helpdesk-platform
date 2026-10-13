using System.Text.Json;
using Assignment.Api.Controller;
using Assignment.Api.Data;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Assignment.Api.Tests;

public class RotationStatusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusReportsWhetherAnExistingAgentIsActive(bool isActive)
    {
        using var db = CreateDb();
        db.Agents.Add(new Agent { UserId = 42, DisplayOrder = 0, IsActive = isActive });
        await db.SaveChangesAsync();

        var result = await CreateController(db).GetAgentRotationStatus(42);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(42, body.GetProperty("userId").GetInt32());
        Assert.Equal(isActive, body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task StatusReturnsNotFoundForMissingAgent()
    {
        using var db = CreateDb();
        Assert.IsType<NotFoundObjectResult>(await CreateController(db).GetAgentRotationStatus(99999));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RotationController CreateController(ApplicationDbContext db) =>
        new(db, NullLogger<RotationController>.Instance);
}

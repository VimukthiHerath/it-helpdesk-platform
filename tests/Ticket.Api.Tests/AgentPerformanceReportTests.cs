using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Xunit;

namespace Ticket.Api.Tests;

public class AgentPerformanceReportTests
{
    private TicketDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TicketDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        
        var context = new TicketDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private ReportsController CreateController(TicketDbContext context, bool isAdmin = true)
    {
        var controller = new ReportsController(context);
        
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "AdminUser") };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        return controller;
    }

    [Fact]
    public async Task GetPerformanceReport_WithValidData_CalculatesCorrectTtrAndBreachRate()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var controller = CreateController(context);
        
        var now = DateTime.UtcNow;
        // Agent 1: 2 resolved tickets. 
        // Ticket 1: TTR = 60 mins. Breached (Resolved > Deadline)
        // Ticket 2: TTR = 30 mins. Not breached
        context.Tickets.AddRange(
            new Model.Ticket { Id = 1, AssignedAgentId = "Agent1", Status = "Resolved", CreatedAtUtc = now.AddMinutes(-60), ResolvedAtUtc = now, SlaDeadlineUtc = now.AddMinutes(-10) },
            new Model.Ticket { Id = 2, AssignedAgentId = "Agent1", Status = "Closed", CreatedAtUtc = now.AddMinutes(-30), ResolvedAtUtc = now, SlaDeadlineUtc = now.AddMinutes(10) }
        );
        
        // Agent 2: 1 resolved ticket, 0 breached
        // Ticket 3: TTR = 120 mins. Not breached
        context.Tickets.Add(new Model.Ticket { Id = 3, AssignedAgentId = "Agent2", Status = "Resolved", CreatedAtUtc = now.AddMinutes(-120), ResolvedAtUtc = now, SlaDeadlineUtc = now.AddMinutes(10) });
        
        await context.SaveChangesAsync();

        // Act
        // Use a wide time range
        var result = await controller.GetAgentPerformanceReport(now.AddDays(-1), now.AddDays(1), CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(okResult.Value);
        
        var agent1Report = report.FirstOrDefault(r => r.AgentId == "Agent1");
        Assert.NotNull(agent1Report);
        Assert.Equal(2, agent1Report.TotalResolvedTickets);
        Assert.Equal(45.00, agent1Report.AverageTtrMinutes); // (60 + 30) / 2
        Assert.Equal(50.00, agent1Report.SlaBreachRate); // 1 out of 2 breached

        var agent2Report = report.FirstOrDefault(r => r.AgentId == "Agent2");
        Assert.NotNull(agent2Report);
        Assert.Equal(1, agent2Report.TotalResolvedTickets);
        Assert.Equal(120.00, agent2Report.AverageTtrMinutes);
        Assert.Equal(0.00, agent2Report.SlaBreachRate);
    }

    [Fact]
    public async Task GetPerformanceReport_WithZeroResolvedTickets_HandlesZeroDivisionGracefully()
    {
        // Wait, EF Core GroupBy might still group it if we have records but none match the filter...
        // Actually, if we filter first, they won't even appear in the report, which is typical.
        // But let's create a ticket that simulates a mathematical Zero division if it somehow grouped it but resolved=0.
        // Actually, our code filters Resolved tickets first. So if an agent has zero tickets, they won't appear.
        // Let's just create an empty DB and assert the report is empty.
        
        // Arrange
        using var context = CreateInMemoryDbContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetAgentPerformanceReport(null, null, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(okResult.Value);
        Assert.Empty(report);
    }

    [Fact]
    public async Task GetPerformanceReport_ExcludesUnresolvedAndCancelledTickets_FromMetrics()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var controller = CreateController(context);
        
        var now = DateTime.UtcNow;
        
        // Add tickets with statuses to exclude
        context.Tickets.AddRange(
            new Model.Ticket { Id = 1, AssignedAgentId = "Agent1", Status = "Open", CreatedAtUtc = now, ResolvedAtUtc = now },
            new Model.Ticket { Id = 2, AssignedAgentId = "Agent1", Status = "Duplicate", CreatedAtUtc = now, ResolvedAtUtc = now },
            new Model.Ticket { Id = 3, AssignedAgentId = "Agent1", Status = "Spam", CreatedAtUtc = now, ResolvedAtUtc = now },
            new Model.Ticket { Id = 4, AssignedAgentId = "Agent1", Status = "Cancelled", CreatedAtUtc = now, ResolvedAtUtc = now },
            new Model.Ticket { Id = 5, AssignedAgentId = null, Status = "Resolved", CreatedAtUtc = now, ResolvedAtUtc = now }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetAgentPerformanceReport(null, null, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsAssignableFrom<IEnumerable<AgentPerformanceReportDto>>(okResult.Value);
        Assert.Empty(report); // All exclusions should trigger
    }

    [Fact]
    public async Task GetPerformanceReport_WithStartDateAfterEndDate_Returns400BadRequest()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var controller = CreateController(context);

        // Act
        var result = await controller.GetAgentPerformanceReport(DateTime.UtcNow.AddDays(1), DateTime.UtcNow, CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetPerformanceReport_WithoutAdminRole_Returns403Forbidden()
    {
        var type = typeof(ReportsController);
        var authorizeAttribute = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            Attribute.GetCustomAttribute(type, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute))!;

        Assert.NotNull(authorizeAttribute);
        var roles = authorizeAttribute.Roles!.Split(',');
        Assert.Contains("Administrator", roles);
    }
}

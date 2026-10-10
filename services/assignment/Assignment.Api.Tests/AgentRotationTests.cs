using System.Security.Claims;
using Assignment.Api.Authorization;
using Assignment.Api.Controller;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Assignment.Api.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Assignment.Api.Tests;

public class AgentRotationTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        
        return new ApplicationDbContext(options);
    }
    
    private RotationController CreateController(ApplicationDbContext context, bool isAdmin = true)
    {
        var controller = new RotationController(context, new NullLogger<RotationController>());
        
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "999") };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.Administrator));
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
    public async Task AddAgent_WhenNotExists_CreatesActiveRecord_Returns201()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var controller = CreateController(context);
        var request = new AddAgentToRotationDTO { UserId = 100 };

        // Act
        var result = await controller.AddAgentToRotation(request);

        // Assert
        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, createdResult.StatusCode);
        
        var agentDto = Assert.IsType<AgentDTO>(createdResult.Value);
        Assert.True(agentDto.IsActive);

        var savedAgent = await context.Agents.FirstOrDefaultAsync(a => a.UserId == 100);
        Assert.NotNull(savedAgent);
        Assert.True(savedAgent.IsActive);
    }

    [Fact]
    public async Task AddAgent_WhenExistsButInactive_ReactivatesRecord_Returns200()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        context.Agents.Add(new Agent { UserId = 100, DisplayOrder = 1, IsActive = false });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context);
        var request = new AddAgentToRotationDTO { UserId = 100 };

        // Act
        var result = await controller.AddAgentToRotation(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        
        var savedAgent = await context.Agents.FirstOrDefaultAsync(a => a.UserId == 100);
        Assert.NotNull(savedAgent);
        Assert.True(savedAgent.IsActive);
    }

    [Fact]
    public async Task AddAgent_WhenAlreadyActive_Returns409Conflict()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        context.Agents.Add(new Agent { UserId = 100, DisplayOrder = 1, IsActive = true });
        await context.SaveChangesAsync();
        
        var controller = CreateController(context);
        var request = new AddAgentToRotationDTO { UserId = 100 };

        // Act
        var result = await controller.AddAgentToRotation(request);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflictResult.StatusCode);
    }

    [Fact]
    public async Task RemoveAgent_SoftDeactivates_AndMaintainsExistingAssignments()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var agent = new Agent { UserId = 100, DisplayOrder = 1, IsActive = true };
        context.Agents.Add(agent);
        await context.SaveChangesAsync();
        
        var assignment = new TicketAssignment { TicketId = 1, AgentId = agent.Id, Urgency = 1, AssignedAtUtc = DateTime.UtcNow };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync();
        
        var controller = CreateController(context);

        // Act
        var result = await controller.RemoveAgentFromRotation(100);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        
        var modifiedAgent = await context.Agents.FirstOrDefaultAsync(a => a.UserId == 100);
        Assert.NotNull(modifiedAgent);
        Assert.False(modifiedAgent.IsActive);
        
        var savedAssignment = await context.Assignments.FirstOrDefaultAsync(a => a.TicketId == 1);
        Assert.NotNull(savedAssignment);
        Assert.Equal(modifiedAgent.Id, savedAssignment.AgentId); // Ticket remains assigned to the soft-deleted agent
    }

    [Fact]
    public async Task RoundRobin_WhenLastAgentRemoved_CalculatesNextIndexWithoutException()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        
        // Agent 1 is deleted, Agent 2 is active
        var removedAgent = new Agent { Id = 1, UserId = 100, DisplayOrder = 0, IsActive = false };
        var activeAgent = new Agent { Id = 2, UserId = 101, DisplayOrder = 1, IsActive = true };
        context.Agents.AddRange(removedAgent, activeAgent);
        
        // The last assigned ticket went to the removed agent
        context.Assignments.Add(new TicketAssignment { TicketId = 10, AgentId = removedAgent.Id, Urgency = 1, AssignedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
        
        var mockProducer = new Mock<IProducer<string, string>>();
        var config = new ConfigurationBuilder().Build();
        var service = new RoundRobinAssignmentService(context, mockProducer.Object, config, new NullLogger<RoundRobinAssignmentService>());

        var ticketEvent = new TicketCreatedEvent { TicketId = 11, Urgency = 2 };

        // Act
        // This should not throw exception despite last agent being inactive (index -1)
        await service.AssignAsync(ticketEvent, CancellationToken.None);

        // Assert
        var newAssignment = await context.Assignments.FirstOrDefaultAsync(a => a.TicketId == 11);
        Assert.NotNull(newAssignment);
        Assert.Equal(activeAgent.Id, newAssignment.AgentId);
    }

    [Fact]
    public async Task RoundRobin_WhenEmptyRoster_HandlesGracefullyWithoutCrashing()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        // Database is empty (no active agents)
        
        var mockProducer = new Mock<IProducer<string, string>>();
        var config = new ConfigurationBuilder().Build();
        var service = new RoundRobinAssignmentService(context, mockProducer.Object, config, new NullLogger<RoundRobinAssignmentService>());

        var ticketEvent = new TicketCreatedEvent { TicketId = 11, Urgency = 2 };

        // Act
        await service.AssignAsync(ticketEvent, CancellationToken.None);

        // Assert
        // Should return gracefully, without assignments created.
        var assignmentsCount = await context.Assignments.CountAsync();
        Assert.Equal(0, assignmentsCount);
    }
}

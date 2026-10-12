using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Assignment.Api.Tests;

public class AssignmentQueryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public AssignmentQueryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAdminList_IncludesAssignments_ForAgentsRemovedFromRotation()
    {
        var testUserId = Random.Shared.Next(4_000_000, 5_000_000);
        var ticketId = Random.Shared.Next(5_000_000, 6_000_000);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Arrange: Add an agent to the rotation roster and seed an assignment
        var agent = new Agent
        {
            UserId = testUserId,
            DisplayOrder = 9999,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var assignment = new TicketAssignment
        {
            TicketId = ticketId,
            AgentId = agent.Id,
            Urgency = 1,
            AssignedAtUtc = DateTime.UtcNow,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        try
        {
            // Deactivate / remove Agent from rotation roster
            db.Agents.Remove(agent);
            await db.SaveChangesAsync();

            // 2. Act: Call GET /api/Assignments as Admin
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

            var response = await client.GetAsync("/api/assignments");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var items = await response.Content.ReadFromJsonAsync<List<AssignmentListItemDTO>>();

            // 3. Assert: The assignment is returned successfully
            Assert.NotNull(items);
            var found = items.FirstOrDefault(i => i.TicketId == ticketId);
            Assert.NotNull(found);
            Assert.Equal(ticketId, found.TicketId);
        }
        finally
        {
            // Cleanup
            var toRemove = db.Assignments.Where(a => a.TicketId == ticketId);
            db.Assignments.RemoveRange(toRemove);
            var agentRow = db.Agents.Where(a => a.UserId == testUserId);
            db.Agents.RemoveRange(agentRow);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetAgentQueue_IncludesAssignments_WhenAgentIsRemovedFromRotation()
    {
        var testUserId = Random.Shared.Next(6_000_000, 7_000_000);
        var ticketId = Random.Shared.Next(7_000_000, 8_000_000);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Arrange: Add agent to rotation and seed assignment
        var agent = new Agent
        {
            UserId = testUserId,
            DisplayOrder = 9998,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var assignment = new TicketAssignment
        {
            TicketId = ticketId,
            AgentId = agent.Id,
            Urgency = 0,
            AssignedAtUtc = DateTime.UtcNow,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        try
        {
            // Deactivate / remove Agent from rotation (ASSIGN-5 soft-delete)
            agent.IsActive = false;
            await db.SaveChangesAsync();

            // 2. Act: Call GET /api/assignments/queue authenticated as Agent
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: testUserId, role: "Agent"));

            var response = await client.GetAsync("/api/assignments/queue");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var queue = await response.Content.ReadFromJsonAsync<List<AssignmentQueueItemDTO>>();
            Assert.NotNull(queue);
            Assert.Contains(queue, item => item.TicketId == ticketId);
        }
        finally
        {
            // Cleanup
            var toRemove = db.Assignments.Where(a => a.TicketId == ticketId);
            db.Assignments.RemoveRange(toRemove);
            var agentRow = db.Agents.Where(a => a.UserId == testUserId);
            db.Agents.RemoveRange(agentRow);
            await db.SaveChangesAsync();
        }
    }

    private static string CreateToken(int userId, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

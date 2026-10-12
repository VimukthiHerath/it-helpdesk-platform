using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Assignment.Api.Data;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Assignment.Api.Tests;

// GET /api/assignments (list all) and PATCH /api/assignments/{id}/reassign
// (SCRUM-20 / ASSIGN-5). Same locally-minted-JWT pattern as the other suites
// here. Needs both the local MySQL instance AND the local Kafka broker from
// docker-compose.yml running - unlike AssignmentQueueTests/AssignmentAgentsTests,
// the reassign happy path actually calls the real Kafka producer registered
// in Program.cs.
//
// Uses the seeded rotation (Agent UserId 3 and 26) and inserts its own
// Assignment rows in a randomized TicketId range so it doesn't collide with
// other tests or local-dev data left behind by manually running the service.
public class AssignmentReassignTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private const int SeededAgentUserId = 3;
    private const int OtherSeededAgentUserId = 26;

    private readonly WebApplicationFactory<Program> _factory;

    public AssignmentReassignTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAllAssignments_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/assignments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllAssignments_WithEmployeeRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));

        var response = await client.GetAsync("/api/assignments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAllAssignments_AsAgentOrAdministrator_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.GetAsync("/api/assignments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReassignTicket_WithEmployeeRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));

        var response = await client.PatchAsJsonAsync("/api/assignments/1/reassign", new { newAgentUserId = SeededAgentUserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Administrator-only by explicit request - reassigning is no longer
    // something any agent can do, even to their own tickets.
    [Fact]
    public async Task ReassignTicket_WithAgentRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: SeededAgentUserId, role: "Agent"));

        var response = await client.PatchAsJsonAsync("/api/assignments/1/reassign", new { newAgentUserId = OtherSeededAgentUserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReassignTicket_NonExistentTicket_Returns404()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PatchAsJsonAsync("/api/assignments/99999999/reassign", new { newAgentUserId = SeededAgentUserId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReassignTicket_TargetNotInRotation_Returns400()
    {
        var ticketId = NextTestTicketId();
        await SeedAssignmentAsync(ticketId, SeededAgentUserId, urgency: 0);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

            var response = await client.PatchAsJsonAsync($"/api/assignments/{ticketId}/reassign", new { newAgentUserId = 999_999 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await RemoveAssignmentAsync(ticketId);
        }
    }

    [Fact]
    public async Task ReassignTicket_SameAgent_Returns409()
    {
        var ticketId = NextTestTicketId();
        await SeedAssignmentAsync(ticketId, SeededAgentUserId, urgency: 0);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

            var response = await client.PatchAsJsonAsync($"/api/assignments/{ticketId}/reassign", new { newAgentUserId = SeededAgentUserId });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            await RemoveAssignmentAsync(ticketId);
        }
    }

    [Fact]
    public async Task ReassignTicket_ToAnotherAgent_UpdatesRecordAndMovesBetweenQueues()
    {
        var ticketId = NextTestTicketId();
        await SeedAssignmentAsync(ticketId, SeededAgentUserId, urgency: 2);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

            var response = await client.PatchAsJsonAsync($"/api/assignments/{ticketId}/reassign", new { newAgentUserId = OtherSeededAgentUserId });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ReassignResponse>();
            Assert.Equal(ticketId, body?.TicketId);
            Assert.Equal(OtherSeededAgentUserId, body?.AgentUserId);

            // AC3: gone from the previous agent's queue, present in the new one.
            var oldAgentClient = _factory.CreateClient();
            oldAgentClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: SeededAgentUserId, role: "Agent"));
            var oldQueue = await (await oldAgentClient.GetAsync("/api/assignments/queue")).Content.ReadFromJsonAsync<List<QueueItem>>();
            Assert.DoesNotContain(oldQueue!, item => item.TicketId == ticketId);

            var newAgentClient = _factory.CreateClient();
            newAgentClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: OtherSeededAgentUserId, role: "Agent"));
            var newQueue = await (await newAgentClient.GetAsync("/api/assignments/queue")).Content.ReadFromJsonAsync<List<QueueItem>>();
            Assert.Contains(newQueue!, item => item.TicketId == ticketId);
        }
        finally
        {
            await RemoveAssignmentAsync(ticketId);
        }
    }

    private static int NextTestTicketId() => Random.Shared.Next(3_000_000, 4_000_000);

    private async Task SeedAssignmentAsync(int ticketId, int agentUserId, int urgency)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var agent = await db.Agents.FirstAsync(a => a.UserId == agentUserId);

        db.Assignments.Add(new TicketAssignment
        {
            TicketId = ticketId,
            AgentId = agent.Id,
            Urgency = urgency,
            AssignedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task RemoveAssignmentAsync(int ticketId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.Assignments.Where(a => a.TicketId == ticketId);
        db.Assignments.RemoveRange(rows);
        await db.SaveChangesAsync();
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

    private sealed class ReassignResponse
    {
        public int TicketId { get; set; }
        public int AgentUserId { get; set; }
    }

    private sealed class QueueItem
    {
        public int TicketId { get; set; }
    }
}

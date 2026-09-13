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

// GET /api/assignments/queue (ASSIGN-4). Tokens are minted locally with the
// same key/issuer/audience every service validates against, so these tests
// never call Auth. Needs the local MySQL instance from docker-compose.yml,
// same as the other WebApplicationFactory-based test suites in this repo.
//
// Uses the Agents seeded by the round-robin migration (UserId 3 and 26,
// see ApplicationDbContext) rather than inserting new Agent rows, since
// DisplayOrder is uniquely indexed and there's no agent-management endpoint
// yet to create one through. Assignment rows are inserted/removed per test
// with a randomized TicketId range so tests don't collide with each other
// or with data left behind by manually running the service locally.
public class AssignmentQueueTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private const int SeededAgentUserId = 3;
    private const int OtherSeededAgentUserId = 26;

    private readonly WebApplicationFactory<Program> _factory;

    public AssignmentQueueTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetQueue_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/assignments/queue");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_WithWrongRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: SeededAgentUserId, role: "Employee"));

        var response = await client.GetAsync("/api/assignments/queue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_WithAgentRole_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: SeededAgentUserId, role: "Agent"));

        var response = await client.GetAsync("/api/assignments/queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetQueue_WithNoMatchingAgentRow_ReturnsEmptyList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 999_999, role: "Agent"));

        var response = await client.GetAsync("/api/assignments/queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var queue = await response.Content.ReadFromJsonAsync<List<QueueItem>>();
        Assert.Empty(queue!);
    }

    [Fact]
    public async Task GetQueue_ReturnsOnlyCallersTickets_SortedMostUrgentFirst()
    {
        var ticketIdBase = Random.Shared.Next(1_000_000, 2_000_000);
        var oneHourTicketId = ticketIdBase;
        var sixHourTicketId = ticketIdBase + 1;
        var twentyFourHourTicketId = ticketIdBase + 2;
        var otherAgentTicketId = ticketIdBase + 3;

        await SeedAssignmentsAsync(
            (twentyFourHourTicketId, SeededAgentUserId, Urgency: 3),
            (oneHourTicketId, SeededAgentUserId, Urgency: 0),
            (sixHourTicketId, SeededAgentUserId, Urgency: 1),
            (otherAgentTicketId, OtherSeededAgentUserId, Urgency: 0));

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: SeededAgentUserId, role: "Agent"));

            var response = await client.GetAsync("/api/assignments/queue");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var queue = await response.Content.ReadFromJsonAsync<List<QueueItem>>();
            var ownTicketIds = queue!
                .Where(item => item.TicketId >= ticketIdBase && item.TicketId < ticketIdBase + 10)
                .Select(item => item.TicketId)
                .ToList();

            Assert.Equal(new[] { oneHourTicketId, sixHourTicketId, twentyFourHourTicketId }, ownTicketIds);
            Assert.DoesNotContain(otherAgentTicketId, queue!.Select(item => item.TicketId));
        }
        finally
        {
            await RemoveSeededAssignmentsAsync(ticketIdBase);
        }
    }

    private async Task SeedAssignmentsAsync(params (int TicketId, int AgentUserId, int Urgency)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var row in rows)
        {
            var agent = await db.Agents.FirstAsync(a => a.UserId == row.AgentUserId);
            db.Assignments.Add(new TicketAssignment
            {
                TicketId = row.TicketId,
                AgentId = agent.Id,
                Urgency = row.Urgency,
                AssignedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task RemoveSeededAssignmentsAsync(int ticketIdBase)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.Assignments.Where(a => a.TicketId >= ticketIdBase && a.TicketId < ticketIdBase + 10);
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

    private sealed class QueueItem
    {
        public int TicketId { get; set; }
        public int Urgency { get; set; }
        public DateTime AssignedAtUtc { get; set; }
    }
}

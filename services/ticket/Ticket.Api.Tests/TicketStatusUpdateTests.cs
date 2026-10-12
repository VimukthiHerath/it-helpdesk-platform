using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Ticket.Api.Data;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests;

// SCRUM-17: PATCH /api/ticket/{id}/status and GET /api/ticket/{id}/status-history.
// Same WebApplicationFactory + locally-minted-JWT pattern as
// TicketAuthorizationTests/TicketCreateValidationTests - needs the local
// MySQL instance running. Each test seeds its own ticket directly via the
// DbContext (CreateTicket doesn't let a test control AssignedTo) and
// cleans it up afterward.
public class TicketStatusUpdateTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public TicketStatusUpdateTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateStatus_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync("/api/ticket/1/status", new { newStatus = 3 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_WithEmployeeRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));

        var response = await client.PatchAsJsonAsync("/api/ticket/1/status", new { newStatus = 3 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_NonExistentTicket_Returns404()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PatchAsJsonAsync("/api/ticket/99999999/status", new { newStatus = 3 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_InvalidTargetStatus_Returns400()
    {
        var ticketId = await SeedTicketAsync(assignedTo: 5);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 5, role: "Agent"));

            // AC1: unassigned/assigned aren't settable through this endpoint -
            // those stay system-managed by round-robin/reassignment.
            var response = await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 0 });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await DeleteTicketAsync(ticketId);
        }
    }

    [Fact]
    public async Task UpdateStatus_AgentNotAssigned_Returns403()
    {
        var ticketId = await SeedTicketAsync(assignedTo: 5);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 6, role: "Agent"));

            var response = await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 3 });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await DeleteTicketAsync(ticketId);
        }
    }

    [Fact]
    public async Task UpdateStatus_AssignedAgent_Returns200AndUpdatesStatus()
    {
        var ticketId = await SeedTicketAsync(assignedTo: 5);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 5, role: "Agent"));

            var response = await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 3 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
            Assert.Equal(3, body?.Status);
        }
        finally
        {
            await DeleteTicketAsync(ticketId);
        }
    }

    [Fact]
    public async Task UpdateStatus_Administrator_CanActOnAnyTicketRegardlessOfAssignment()
    {
        var ticketId = await SeedTicketAsync(assignedTo: 5);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 999, role: "Administrator"));

            var response = await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 4 });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await DeleteTicketAsync(ticketId);
        }
    }

    [Fact]
    public async Task GetStatusHistory_AfterMultipleChanges_ReturnsEveryEntryInOrder()
    {
        var ticketId = await SeedTicketAsync(assignedTo: 5);

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", CreateToken(userId: 5, role: "Agent"));

            await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 3 });
            await client.PatchAsJsonAsync($"/api/ticket/{ticketId}/status", new { newStatus = 2 });

            var response = await client.GetAsync($"/api/ticket/{ticketId}/status-history");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var history = await response.Content.ReadFromJsonAsync<List<HistoryItem>>();
            Assert.Equal(2, history!.Count);
            Assert.Equal(1, history[0].OldStatus);
            Assert.Equal(3, history[0].NewStatus);
            Assert.Equal(3, history[1].OldStatus);
            Assert.Equal(2, history[1].NewStatus);
        }
        finally
        {
            await DeleteTicketAsync(ticketId);
        }
    }

    private async Task<int> SeedTicketAsync(int assignedTo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ticket = new Tickets
        {
            Description = $"Seeded ticket {Guid.NewGuid():N}",
            IssueType = "Hardware",
            Urgency = TicketUrgency.one_hour,
            Status = TicketStatus.assigned,
            AssignedTo = assignedTo,
            CreatedBy = 1,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    private async Task DeleteTicketAsync(int ticketId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.StatusHistory.RemoveRange(db.StatusHistory.Where(h => h.TicketId == ticketId));
        var ticket = await db.Tickets.FindAsync(ticketId);
        if (ticket is not null)
        {
            db.Tickets.Remove(ticket);
        }
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

    private sealed class StatusResponse
    {
        public int TicketId { get; set; }
        public int Status { get; set; }
    }

    private sealed class HistoryItem
    {
        public int OldStatus { get; set; }
        public int NewStatus { get; set; }
        public int ChangedBy { get; set; }
    }
}

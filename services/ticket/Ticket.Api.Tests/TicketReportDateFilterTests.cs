using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Xunit;

namespace Ticket.Api.Tests;

public class TicketReportDateFilterTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public TicketReportDateFilterTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));
    }

    [Fact]
    public async Task ReportEndpoint_StartDateProvided_ExcludesPriorDay()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var ticketA = new Tickets { Description = "A", IssueType = "HW", Urgency = 0, CreatedBy = 1, CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 28, 23, 59, 0), DateTimeKind.Utc) };
        var ticketB = new Tickets { Description = "B", IssueType = "HW", Urgency = 0, CreatedBy = 1, CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 29, 0, 1, 0), DateTimeKind.Utc) };
        
        db.Tickets.AddRange(ticketA, ticketB);
        await db.SaveChangesAsync();

        try
        {
            var response = await _client.GetAsync("/api/Ticket/report?startDate=2026-09-29");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var items = await response.Content.ReadFromJsonAsync<List<TicketReportItemDTO>>();
            Assert.NotNull(items);
            Assert.DoesNotContain(items, t => t.TicketId == ticketA.Id);
            Assert.Contains(items, t => t.TicketId == ticketB.Id);
        }
        finally
        {
            db.Tickets.Remove(ticketA);
            db.Tickets.Remove(ticketB);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ReportEndpoint_EndDateProvided_ExcludesNextDay()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        var ticketC = new Tickets { Description = "C", IssueType = "HW", Urgency = 0, CreatedBy = 1, CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 29, 23, 30, 0), DateTimeKind.Utc) };
        var ticketD = new Tickets { Description = "D", IssueType = "HW", Urgency = 0, CreatedBy = 1, CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 30, 0, 5, 0), DateTimeKind.Utc) };
        
        db.Tickets.AddRange(ticketC, ticketD);
        await db.SaveChangesAsync();

        try
        {
            var response = await _client.GetAsync("/api/Ticket/report?endDate=2026-09-29");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var items = await response.Content.ReadFromJsonAsync<List<TicketReportItemDTO>>();
            Assert.NotNull(items);
            Assert.Contains(items, t => t.TicketId == ticketC.Id);
            Assert.DoesNotContain(items, t => t.TicketId == ticketD.Id);
        }
        finally
        {
            db.Tickets.Remove(ticketC);
            db.Tickets.Remove(ticketD);
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
        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Assignment.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Assignment.Api.Tests;

// GET/POST /api/assignments/agents - lets an Administrator manage who's in
// the round-robin rotation. Tokens are minted locally, same pattern as
// AssignmentQueueTests. Uses a randomized UserId range per test so it never
// collides with the seeded rotation (UserId 3 and 26) or other tests, and
// removes what it adds so re-runs stay repeatable against the shared local
// MySQL instance.
public class AssignmentAgentsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public AssignmentAgentsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAgents_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/assignments/agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAgents_WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Agent"));

        var response = await client.GetAsync("/api/assignments/agents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAgents_AsAdministrator_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.GetAsync("/api/assignments/agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AddAgent_WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Agent"));

        var response = await client.PostAsJsonAsync("/api/assignments/agents", new { userId = NextTestUserId() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddAgent_AsAdministrator_AppendsToEndOfRotation()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var newUserId = NextTestUserId();

        try
        {
            var maxOrderBefore = await GetMaxDisplayOrderAsync();

            var response = await client.PostAsJsonAsync("/api/assignments/agents", new { userId = newUserId });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<AgentResponse>();
            Assert.Equal(newUserId, created?.UserId);
            Assert.Equal(maxOrderBefore + 1, created?.DisplayOrder);
        }
        finally
        {
            await RemoveTestAgentAsync(newUserId);
        }
    }

    [Fact]
    public async Task AddAgent_AlreadyInRotation_Returns409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var newUserId = NextTestUserId();

        try
        {
            var first = await client.PostAsJsonAsync("/api/assignments/agents", new { userId = newUserId });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            var second = await client.PostAsJsonAsync("/api/assignments/agents", new { userId = newUserId });

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
        finally
        {
            await RemoveTestAgentAsync(newUserId);
        }
    }

    private static int NextTestUserId() => Random.Shared.Next(2_000_000, 3_000_000);

    private async Task<int> GetMaxDisplayOrderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Agents.Select(a => (int?)a.DisplayOrder).MaxAsync() ?? -1;
    }

    private async Task RemoveTestAgentAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = db.Agents.Where(a => a.UserId == userId);
        db.Agents.RemoveRange(rows);
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

    private sealed class AgentResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DisplayOrder { get; set; }
    }
}

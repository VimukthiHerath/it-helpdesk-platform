using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Auth.Api.Data;
using Auth.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

// GET/PUT /api/auth/users and PATCH /api/auth/users/{id}/deactivate.
// UpdateUser/DeactivateUser call out to Assignment.Api to check whether an
// agent is still in the round-robin rotation (see AssignmentRotationClient) -
// the only cross-service call in this codebase. Rather than requiring a real
// Assignment.Api running for every test (like the local-MySQL requirement
// every other suite here has), the "AssignmentApi" named HttpClient's
// primary handler is swapped for a stub per test via
// WithWebHostBuilder/ConfigureTestServices, so each test controls exactly
// what Assignment.Api "says" without a live dependency.
public class AdminManageUsersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminManageUsersTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUsers_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));

        var response = await client.GetAsync("/api/auth/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsAdministrator_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.GetAsync("/api/auth/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_NonExistentUser_Returns404()
    {
        var client = WithStubbedAssignmentApi(NotInRotation).CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PutAsJsonAsync("/api/auth/users/99999999", new
        {
            name = "Nobody",
            email = $"nobody-{Guid.NewGuid():N}@example.com",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_DuplicateEmail_Returns409()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var takenEmail = $"taken-{Guid.NewGuid():N}@example.com";
        var otherUserId = await CreateEmployeeAsync(factory, takenEmail);
        var targetUserId = await CreateEmployeeAsync(factory, $"target-{Guid.NewGuid():N}@example.com");

        var response = await client.PutAsJsonAsync($"/api/auth/users/{targetUserId}", new
        {
            name = "Target",
            email = takenEmail,
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_SelfRoleChangeAwayFromAdministrator_Returns409()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var client = factory.CreateClient();
        var adminId = await CreateUserAsync(factory, $"self-admin-{Guid.NewGuid():N}@example.com", UserRole.Administrator);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: adminId, role: "Administrator"));

        var response = await client.PutAsJsonAsync($"/api/auth/users/{adminId}", new
        {
            name = "Self Admin",
            email = $"self-admin-{Guid.NewGuid():N}@example.com",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AgentStillInRotation_Returns409()
    {
        var agentId = await CreateUserAsync(_factory.WithWebHostBuilder(_ => { }), $"in-rotation-{Guid.NewGuid():N}@example.com", UserRole.Agent);
        var factory = WithStubbedAssignmentApi(request => InRotationFor(request, agentId));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PutAsJsonAsync($"/api/auth/users/{agentId}", new
        {
            name = "Agent In Rotation",
            email = $"in-rotation-{Guid.NewGuid():N}@example.com",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("rotation", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateUser_AgentNotInRotation_AppliesChange()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var agentId = await CreateUserAsync(factory, $"not-in-rotation-{Guid.NewGuid():N}@example.com", UserRole.Agent);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var newEmail = $"promoted-{Guid.NewGuid():N}@example.com";
        var response = await client.PutAsJsonAsync($"/api/auth/users/{agentId}", new
        {
            name = "Promoted",
            email = newEmail,
            role = 1,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(1, body?.Role);
        Assert.Equal(newEmail, body?.Email);
    }

    [Fact]
    public async Task UpdateUser_RotationCheckUnreachable_Returns503()
    {
        var factory = WithStubbedAssignmentApi(_ => throw new HttpRequestException("simulated network failure"));
        var agentId = await CreateUserAsync(factory, $"unreachable-{Guid.NewGuid():N}@example.com", UserRole.Agent);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PutAsJsonAsync($"/api/auth/users/{agentId}", new
        {
            name = "Agent",
            email = $"unreachable-{Guid.NewGuid():N}@example.com",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_SelfDeactivation_Returns409()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var adminId = await CreateUserAsync(factory, $"self-deactivate-{Guid.NewGuid():N}@example.com", UserRole.Administrator);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: adminId, role: "Administrator"));

        var response = await client.PatchAsync($"/api/auth/users/{adminId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_AlreadyInactive_IsIdempotent()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var userId = await CreateEmployeeAsync(factory, $"already-inactive-{Guid.NewGuid():N}@example.com");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var first = await client.PatchAsync($"/api/auth/users/{userId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PatchAsync($"/api/auth/users/{userId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_AgentStillInRotation_Returns409()
    {
        var agentId = await CreateUserAsync(_factory.WithWebHostBuilder(_ => { }), $"deactivate-blocked-{Guid.NewGuid():N}@example.com", UserRole.Agent);
        var factory = WithStubbedAssignmentApi(request => InRotationFor(request, agentId));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PatchAsync($"/api/auth/users/{agentId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_AgentNotInRotation_Succeeds()
    {
        var factory = WithStubbedAssignmentApi(NotInRotation);
        var agentId = await CreateUserAsync(factory, $"deactivate-allowed-{Guid.NewGuid():N}@example.com", UserRole.Agent);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var response = await client.PatchAsync($"/api/auth/users/{agentId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.False(body?.IsActive);
    }

    private async Task<int> CreateEmployeeAsync(WebApplicationFactory<Program> factory, string email) =>
        await CreateUserAsync(factory, email, UserRole.Employee);

    private async Task<int> CreateUserAsync(WebApplicationFactory<Program> factory, string email, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User
        {
            Name = "Test User",
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private WebApplicationFactory<Program> WithStubbedAssignmentApi(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("AssignmentApi")
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(respond));
            });
        });

    private static HttpResponseMessage NotInRotation(HttpRequestMessage request) =>
        JsonArrayResponse("[]");

    private static HttpResponseMessage InRotationFor(HttpRequestMessage request, int agentUserId) =>
        JsonArrayResponse($"[{{\"id\":1,\"userId\":{agentUserId},\"displayOrder\":0}}]");

    private static HttpResponseMessage JsonArrayResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

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

    private sealed class UserResponse
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public int Role { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}

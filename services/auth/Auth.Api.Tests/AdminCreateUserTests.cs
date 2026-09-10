using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Auth.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

// AUTH-4: POST /api/auth/users is admin-only. Tokens are minted locally
// with the same key/issuer/audience Auth signs with, so these tests never
// go through /api/auth/login for the caller's own token.
public class AdminCreateUserTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminCreateUserTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateUser_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email = $"no-token-{Guid.NewGuid():N}@example.com",
            password = "Password123!",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithNonAdminRole_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));

        var response = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email = $"wrong-role-{Guid.NewGuid():N}@example.com",
            password = "Password123!",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsAdministrator_Returns201WithCreatedUser()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var email = $"admin-created-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email,
            password = "Password123!",
            role = 2,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Id > 0);
        Assert.Equal("New User", body.Name);
        Assert.Equal(email, body.Email);
        Assert.Equal(2, body.Role);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var email = $"duplicate-{Guid.NewGuid():N}@example.com";
        var payload = new
        {
            name = "First Attempt",
            email,
            password = "Password123!",
            role = 1,
        };

        var first = await client.PostAsJsonAsync("/api/auth/users", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/users", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ThenLogin_Succeeds()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var email = $"login-after-create-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!";

        var created = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email,
            password,
            role = 1,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Login must not be authenticated as the admin who created the account.
        var anonymousClient = _factory.CreateClient();
        var loginResponse = await anonymousClient.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
    }

    [Fact]
    public async Task CreateUser_AsAdministrator_RecordsWhichAdminCreatedIt()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 7, role: "Administrator"));

        var email = $"audit-trail-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email,
            password = "Password123!",
            role = 2,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        Assert.Equal(7, body?.CreatedBy);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Users.FirstAsync(u => u.Email == email);
        Assert.Equal(7, stored.CreatedBy);
    }

    [Fact]
    public async Task Register_SelfSignup_LeavesCreatedByNull()
    {
        var client = _factory.CreateClient();

        var email = $"self-signup-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Self Signup",
            email,
            password = "Password123!",
            role = 1,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Users.FirstAsync(u => u.Email == email);
        Assert.Null(stored.CreatedBy);
    }

    [Fact]
    public async Task CreateUser_StoresPasswordHashed_NeverPlaintext()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Administrator"));

        var email = $"hash-check-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!";

        var response = await client.PostAsJsonAsync("/api/auth/users", new
        {
            name = "New User",
            email,
            password,
            role = 1,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Users.FirstAsync(u => u.Email == email);

        Assert.NotEqual(password, stored.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, stored.Password));
    }

    private sealed class LoginResponse
    {
        public string? Token { get; set; }
    }

    private sealed class CreatedUserResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Role { get; set; }
        public int? CreatedBy { get; set; }
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

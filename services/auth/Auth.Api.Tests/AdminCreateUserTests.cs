using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

// AUTH-3: POST /api/auth/users is admin-only. Tokens are minted locally
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

    private sealed class CreatedUserResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Role { get; set; }
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

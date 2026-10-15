using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Auth.Api.Data;
using Auth.Api.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

// GET /api/auth/users/{id}: lets an agent read a requester's contact details.
// Listing every user (GET /api/auth/users) stays Administrator-only.
public class AdminGetUserTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminGetUserTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUser_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/auth/users/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_AsEmployee_Returns403()
    {
        var client = await ClientAsync(userId: 1, role: UserRole.Employee);

        var response = await client.GetAsync("/api/auth/users/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Agent)]
    [InlineData(UserRole.Administrator)]
    public async Task GetUser_AsStaff_ReturnsNameEmailAndPhone(UserRole role)
    {
        var requesterId = await CreateRequesterAsync($"requester-{Guid.NewGuid():N}@example.com", "0771234567");
        var client = await ClientAsync(userId: 1, role);

        var response = await client.GetAsync($"/api/auth/users/{requesterId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(requesterId, body!.Id);
        Assert.Equal("Requester", body.Name);
        Assert.Equal("0771234567", body.Phone);
        Assert.Contains("@example.com", body.Email);
    }

    [Fact]
    public async Task GetUser_NeverReturnsPasswordHash()
    {
        var requesterId = await CreateRequesterAsync($"no-hash-{Guid.NewGuid():N}@example.com", "0771234567");
        var client = await ClientAsync(userId: 1, role: UserRole.Agent);

        var json = await (await client.GetAsync($"/api/auth/users/{requesterId}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUser_UnknownId_Returns404()
    {
        var client = await ClientAsync(userId: 1, role: UserRole.Agent);

        var response = await client.GetAsync("/api/auth/users/99999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<int> CreateRequesterAsync(string email, string? phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User
        {
            Name = "Requester",
            Email = email,
            Phone = phone,
            Password = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<HttpClient> ClientAsync(int userId, UserRole role)
    {
        await TestUsers.EnsureAsync(_factory.Services, userId, role);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId, role.ToString()));
        return client;
    }

    private static string CreateToken(int userId, string role)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("SecurityStamp", TestUsers.Stamp),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class UserResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
    }
}

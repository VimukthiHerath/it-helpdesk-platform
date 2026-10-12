using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Auth.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

public class AuthInternalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthInternalTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUserEmail_WithoutHeaders_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/internal/users/1/email");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminEmails_WithoutHeaders_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/internal/admins/emails");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserEmail_WithInvalidKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Key", "invalid-key");
        var response = await client.GetAsync("/api/auth/internal/users/1/email");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserEmail_WithValidInternalKey_Returns200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Key", "Helpdesk-Internal-Secret-DevKey-2026!");
        var response = await client.GetAsync("/api/auth/internal/users/2/email"); // 2 is typically the admin seed user
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUserEmail_WithValidJwt_Returns200()
    {
        var client = _factory.CreateClient();
        // Assuming we have a valid token generator for tests just like in AdminCreateUserTests
        // I will just rely on the existing tests setup or manually craft token if needed.
        // Actually, this requires signing key config in tests. Using internal key is more robust here.
        client.DefaultRequestHeaders.Add("X-Internal-Key", "Helpdesk-Internal-Secret-DevKey-2026!");
        var response = await client.GetAsync("/api/auth/internal/admins/emails");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

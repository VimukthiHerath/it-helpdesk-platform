using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Ticket.Api.Tests;

// SCRUM-16: POST /api/ticket returns a specific, field-named message on a
// validation failure (AC1), distinct from a server-side failure's wording
// (AC2). Same locally-minted-JWT pattern as TicketAuthorizationTests. Needs
// the local MySQL instance from docker-compose.yml running, same as every
// other WebApplicationFactory suite in this repo.
public class TicketCreateValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "IT24101503IT24100146IT24101500IT24101497";
    private const string Issuer = "AuthApi";
    private const string Audience = "ItHelpdeskClient";

    private readonly HttpClient _client;

    public TicketCreateValidationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(userId: 1, role: "Employee"));
    }

    [Fact]
    public async Task CreateTicket_MissingDescription_ReturnsMessageNamingDescription()
    {
        var response = await _client.PostAsJsonAsync("/api/ticket", new
        {
            description = "",
            issueType = "Hardware",
            urgency = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Contains("Description", body?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTicket_MissingIssueType_ReturnsMessageNamingIssueType()
    {
        var response = await _client.PostAsJsonAsync("/api/ticket", new
        {
            description = "Something is broken",
            issueType = "",
            urgency = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Contains("Issue type", body?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTicket_DescriptionTooLong_ReturnsMessageNamingDescription()
    {
        var response = await _client.PostAsJsonAsync("/api/ticket", new
        {
            description = new string('a', 300),
            issueType = "Hardware",
            urgency = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Contains("Description", body?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("255", body?.Message);
    }

    [Fact]
    public async Task CreateTicket_ValidationFailure_MessageDiffersFromServerErrorWording()
    {
        // AC2: a validation failure's message must not read like the
        // generic "something went wrong, try again" server-error wording -
        // it has to tell the user what to fix, not just to retry.
        var response = await _client.PostAsJsonAsync("/api/ticket", new
        {
            description = "",
            issueType = "Hardware",
            urgency = 0,
        });

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.DoesNotContain("Something went wrong", body?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", body?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateTicket_ValidRequest_Returns201()
    {
        var response = await _client.PostAsJsonAsync("/api/ticket", new
        {
            description = $"Valid ticket {Guid.NewGuid():N}",
            issueType = "Hardware",
            urgency = 0,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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

    private sealed class MessageResponse
    {
        public string? Message { get; set; }
    }
}

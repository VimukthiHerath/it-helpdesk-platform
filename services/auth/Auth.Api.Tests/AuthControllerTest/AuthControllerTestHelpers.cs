using System.Security.Claims;
using Auth.Api.Controller;
using Auth.Api.Data;
using Auth.Api.DTO;
using Auth.Api.Model;
using Auth.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Api.Tests;

internal static class AuthControllerTestHelpers
{
    private const string JwtKey = "IT24101503IT24100146IT24101500IT24101497";

    internal static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    internal static Mock<IAssignmentRotationClient> CreateRotationClient(
        RotationCheckResult result = RotationCheckResult.NotInRotation)
    {
        var mock = new Mock<IAssignmentRotationClient>();
        mock.Setup(client => client.IsUserInRotationAsync(
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    internal static AuthController CreateController(
        ApplicationDbContext context,
        IAssignmentRotationClient rotationClient,
        int callerId = 99,
        string? email = null,
        string? role = null)
    {
        return new AuthController(
            context,
            NullLogger<AuthController>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = JwtKey,
                    ["Jwt:Issuer"] = "AuthApi",
                    ["Jwt:Audience"] = "ItHelpdeskClient",
                    ["Jwt:ExpiryMinutes"] = "5",
                })
                .Build(),
            rotationClient)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, callerId.ToString()),
                            new Claim(ClaimTypes.Email, email ?? string.Empty),
                            new Claim(ClaimTypes.Role, role ?? UserRole.Administrator.ToString()),
                        },
                        "UnitTest")),
                },
            },
        };
    }

    internal static User CreateUser(int id, string email, UserRole role) => new()
    {
        Id = id,
        Name = $"User {id}",
        Email = email,
        Password = BCrypt.Net.BCrypt.HashPassword("password"),
        Role = role,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    internal static User CreateAgent(int id) => new()
    {
        Id = id,
        Name = "Agent",
        Email = "agent@example.com",
        Password = "not-used",
        Role = UserRole.Agent,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };

    internal static UpdateUserDTO ValidUpdate(UserRole role) => new()
    {
        Name = "Updated User",
        Email = "updated@example.com",
        Role = role,
    };

    internal static int GetStatusCode(IActionResult result) =>
        result switch
        {
            ObjectResult objectResult => objectResult.StatusCode
                ?? (objectResult is OkObjectResult
                    ? StatusCodes.Status200OK
                    : objectResult.Value is ValidationProblemDetails
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status500InternalServerError),
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected an HTTP result, but received {result.GetType().Name}."),
        };
}

using Auth.Api.DTO;
using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class LoginEndpointTests
{
    [Theory]
    [InlineData("wrong-password", true)]
    [InlineData("password", false)]
    public async Task Login_InvalidCredentialsOrInactiveUser_ReturnsUnauthorized(
        string password, bool isActive)
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(new User
        {
            Id = 1,
            Name = "Login User",
            Email = "login@example.com",
            Password = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Employee,
            IsActive = isActive,
        });
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.Login(new UserLoginDTO
        {
            Email = "login@example.com",
            Password = password,
        });

        Assert.Equal(StatusCodes.Status401Unauthorized,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUpdatesLastLogin()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var user = AuthControllerTestHelpers.CreateUser(
            1, "login@example.com", UserRole.Employee);
        user.Password = BCrypt.Net.BCrypt.HashPassword("password");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.Login(new UserLoginDTO
        {
            Email = "login@example.com",
            Password = "password",
        });

        var response = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode ?? StatusCodes.Status200OK);
        Assert.NotNull(response.Value);
        var body = response.Value;
        var token = body!.GetType().GetProperty("token")!.GetValue(body) as string;
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal("Login successful", body.GetType().GetProperty("message")!.GetValue(body));
        Assert.NotNull((await context.Users.SingleAsync()).LastLoginAt);
    }

    [Fact]
    public async Task Login_WhenModelStateIsInvalid_ReturnsValidationProblem()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);
        controller.ModelState.AddModelError("Email", "Email is required.");

        var result = await controller.Login(new UserLoginDTO());

        Assert.Equal(StatusCodes.Status400BadRequest,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }
}

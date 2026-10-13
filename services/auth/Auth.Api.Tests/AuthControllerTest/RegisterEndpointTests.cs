using Auth.Api.DTO;
using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class RegisterEndpointTests
{
    [Fact]
    public async Task Register_WithValidRequest_CreatesUserAndReturnsCreated()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.Register(new UserRegisterDTO
        {
            Name = "New User",
            Email = "new.user@example.com",
            Password = "password",
            Role = UserRole.Employee,
        });

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var body = Assert.IsType<Auth.Api.DTO.UserRegisterResponseDTO>(response.Value);
        Assert.Equal("New User", body.Name);
        Assert.Equal("new.user@example.com", body.Email);
        Assert.Equal(UserRole.Employee, body.Role);
        var user = await context.Users.SingleAsync();
        Assert.Equal("New User", user.Name);
        Assert.Equal(UserRole.Employee, user.Role);
        Assert.True(BCrypt.Net.BCrypt.Verify("password", user.Password));
    }

    [Fact]
    public async Task Register_WhenEmailExists_ReturnsConflict()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateUser(
            1, "existing@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.Register(new UserRegisterDTO
        {
            Name = "Duplicate",
            Email = "existing@example.com",
            Password = "password",
        });

        Assert.Equal(StatusCodes.Status409Conflict,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }

    [Fact]
    public async Task Register_WhenModelStateIsInvalid_ReturnsValidationProblem()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);
        controller.ModelState.AddModelError("Email", "Email is required.");

        var result = await controller.Register(new UserRegisterDTO());

        Assert.Equal(StatusCodes.Status400BadRequest,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }
}

using Auth.Api.DTO;
using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class CreateUserEndpointTests
{
    [Fact]
    public async Task CreateUser_WithValidAdministratorClaim_SetsCreatedBy()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object, callerId: 42);

        var result = await controller.CreateUser(new UserRegisterDTO
        {
            Name = "Managed User",
            Email = "managed@example.com",
            Password = "password",
            Role = UserRole.Agent,
        });

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var body = Assert.IsType<Auth.Api.DTO.AdminCreateUserResponseDTO>(response.Value);
        Assert.Equal("Managed User", body.Name);
        Assert.Equal("managed@example.com", body.Email);
        Assert.Equal(42, body.CreatedBy);
        var user = await context.Users.SingleAsync();
        Assert.Equal(42, user.CreatedBy);
        Assert.Equal(UserRole.Agent, user.Role);
    }

    [Fact]
    public async Task CreateUser_WhenEmailExists_ReturnsConflict()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateUser(
            1, "existing@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object, callerId: 42);

        var result = await controller.CreateUser(new UserRegisterDTO
        {
            Name = "Duplicate",
            Email = "existing@example.com",
            Password = "password",
        });

        Assert.Equal(StatusCodes.Status409Conflict,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }

    [Fact]
    public async Task CreateUser_WhenModelStateIsInvalid_ReturnsValidationProblem()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object, callerId: 42);
        controller.ModelState.AddModelError("Name", "Name is required.");

        var result = await controller.CreateUser(new UserRegisterDTO());

        Assert.Equal(StatusCodes.Status400BadRequest,
            AuthControllerTestHelpers.GetStatusCode(result.Result!));
    }
}

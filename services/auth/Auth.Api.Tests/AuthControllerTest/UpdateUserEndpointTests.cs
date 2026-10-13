using Auth.Api.Model;
using Auth.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Auth.Api.Tests;

public class UpdateUserEndpointTests
{
    public static TheoryData<RotationCheckResult, int> RotationOutcomes =>
        new()
        {
            { RotationCheckResult.NotInRotation, StatusCodes.Status200OK },
            { RotationCheckResult.InRotation, StatusCodes.Status409Conflict },
            { RotationCheckResult.Unreachable, StatusCodes.Status503ServiceUnavailable },
        };

    [Theory]
    [MemberData(nameof(RotationOutcomes))]
    public async Task UpdateUser_WhenChangingAgentRole_ReturnsResultForRotationOutcome(
        RotationCheckResult rotationOutcome, int expectedStatusCode)
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateAgent(7));
        await context.SaveChangesAsync();
        var rotationClient = AuthControllerTestHelpers.CreateRotationClient(rotationOutcome);
        var controller = AuthControllerTestHelpers.CreateController(
            context, rotationClient.Object);

        var result = await controller.UpdateUser(7, AuthControllerTestHelpers.ValidUpdate(
            UserRole.Employee));

        Assert.Equal(expectedStatusCode, AuthControllerTestHelpers.GetStatusCode(result));
        rotationClient.Verify(client => client.IsUserInRotationAsync(
                7, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var savedUser = await context.Users.SingleAsync();
        Assert.Equal(
            rotationOutcome == RotationCheckResult.NotInRotation
                ? UserRole.Employee
                : UserRole.Agent,
            savedUser.Role);

        if (rotationOutcome == RotationCheckResult.NotInRotation)
        {
            var response = Assert.IsType<OkObjectResult>(result);
            var body = Assert.IsType<Auth.Api.DTO.AdminUserListItemDTO>(response.Value);
            Assert.Equal("Updated User", body.Name);
            Assert.Equal("updated@example.com", body.Email);
            Assert.True(body.IsActive);
        }
    }

    [Fact]
    public async Task UpdateUser_WhenUserDoesNotExist_ReturnsNotFound()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.UpdateUser(
            7, AuthControllerTestHelpers.ValidUpdate(UserRole.Employee));

        Assert.Equal(StatusCodes.Status404NotFound,
            AuthControllerTestHelpers.GetStatusCode(result));
    }

    [Fact]
    public async Task UpdateUser_WhenChangingOwnAdministratorRole_ReturnsConflict()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateUser(
            99, "admin@example.com", UserRole.Administrator));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.UpdateUser(
            99, AuthControllerTestHelpers.ValidUpdate(UserRole.Employee));

        Assert.Equal(StatusCodes.Status409Conflict,
            AuthControllerTestHelpers.GetStatusCode(result));
    }

    [Fact]
    public async Task UpdateUser_WhenEmailBelongsToAnotherUser_ReturnsConflict()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.AddRange(
            AuthControllerTestHelpers.CreateUser(1, "target@example.com", UserRole.Employee),
            AuthControllerTestHelpers.CreateUser(2, "taken@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);
        var request = AuthControllerTestHelpers.ValidUpdate(UserRole.Employee);
        request.Email = "taken@example.com";

        var result = await controller.UpdateUser(1, request);

        Assert.Equal(StatusCodes.Status409Conflict,
            AuthControllerTestHelpers.GetStatusCode(result));
    }

    [Fact]
    public async Task UpdateUser_WhenModelStateIsInvalid_ReturnsValidationProblem()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);
        controller.ModelState.AddModelError("Email", "Email is required.");

        var result = await controller.UpdateUser(
            1, AuthControllerTestHelpers.ValidUpdate(UserRole.Employee));

        Assert.Equal(StatusCodes.Status400BadRequest,
            AuthControllerTestHelpers.GetStatusCode(result));
    }
}

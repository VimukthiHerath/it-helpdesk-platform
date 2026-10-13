using Auth.Api.Model;
using Auth.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Auth.Api.Tests;

public class DeactivateUserEndpointTests
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
    public async Task DeactivateUser_WhenTargetIsAgent_ReturnsResultForRotationOutcome(
        RotationCheckResult rotationOutcome, int expectedStatusCode)
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateAgent(7));
        await context.SaveChangesAsync();
        var rotationClient = AuthControllerTestHelpers.CreateRotationClient(rotationOutcome);
        var controller = AuthControllerTestHelpers.CreateController(
            context, rotationClient.Object);

        var result = await controller.DeactivateUser(7);

        Assert.Equal(expectedStatusCode, AuthControllerTestHelpers.GetStatusCode(result));
        rotationClient.Verify(client => client.IsUserInRotationAsync(
                7, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(
            rotationOutcome != RotationCheckResult.NotInRotation,
            (await context.Users.SingleAsync()).IsActive);

        if (rotationOutcome == RotationCheckResult.NotInRotation)
        {
            var response = Assert.IsType<OkObjectResult>(result);
            var body = Assert.IsType<Auth.Api.DTO.AdminUserListItemDTO>(response.Value);
            Assert.Equal("agent@example.com", body.Email);
            Assert.False(body.IsActive);
        }
    }

    [Fact]
    public async Task DeactivateUser_WhenUserDoesNotExist_ReturnsNotFound()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.DeactivateUser(7);

        Assert.Equal(StatusCodes.Status404NotFound,
            AuthControllerTestHelpers.GetStatusCode(result));
    }

    [Fact]
    public async Task DeactivateUser_WhenCallerIsTarget_ReturnsConflict()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateUser(
            99, "admin@example.com", UserRole.Administrator));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.DeactivateUser(99);

        Assert.Equal(StatusCodes.Status409Conflict,
            AuthControllerTestHelpers.GetStatusCode(result));
    }

    [Fact]
    public async Task DeactivateUser_WhenAlreadyInactive_IsIdempotent()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var user = AuthControllerTestHelpers.CreateUser(
            7, "inactive@example.com", UserRole.Employee);
        user.IsActive = false;
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var rotationClient = AuthControllerTestHelpers.CreateRotationClient();
        var controller = AuthControllerTestHelpers.CreateController(
            context, rotationClient.Object);

        var result = await controller.DeactivateUser(7);

        Assert.Equal(StatusCodes.Status200OK,
            AuthControllerTestHelpers.GetStatusCode(result));
        rotationClient.Verify(client => client.IsUserInRotationAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

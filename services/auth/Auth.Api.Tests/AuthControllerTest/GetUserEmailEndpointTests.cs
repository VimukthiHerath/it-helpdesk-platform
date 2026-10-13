using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class GetUserEmailEndpointTests
{
    [Fact]
    public async Task GetUserEmail_WhenUserExists_ReturnsEmailAndName()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.Add(AuthControllerTestHelpers.CreateUser(
            7, "user@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var response = Assert.IsType<OkObjectResult>(await controller.GetUserEmail(7));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode ?? StatusCodes.Status200OK);
        Assert.NotNull(response.Value);
        var body = response.Value;
        Assert.Equal("user@example.com", body!.GetType().GetProperty("email")!.GetValue(body));
        Assert.Equal("User 7", body.GetType().GetProperty("name")!.GetValue(body));
    }

    [Fact]
    public async Task GetUserEmail_WhenUserDoesNotExist_ReturnsNotFound()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.GetUserEmail(7);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
    }
}

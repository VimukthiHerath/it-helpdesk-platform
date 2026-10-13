using Auth.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Auth.Api.Tests;

public class GetMeEndpointTests
{
    [Fact]
    public void GetMe_ReturnsOkResponse()
    {
        using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context,
            AuthControllerTestHelpers.CreateRotationClient().Object,
            email: "me@example.com",
            role: UserRole.Agent.ToString());

        var result = controller.GetMe();

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, response.StatusCode ?? 200);
    }

    [Fact]
    public void GetMe_UsesClaimsToBuildResponseBody()
    {
        using var context = AuthControllerTestHelpers.CreateContext();
        var controller = AuthControllerTestHelpers.CreateController(
            context,
            AuthControllerTestHelpers.CreateRotationClient().Object,
            callerId: 17,
            email: "agent@example.com",
            role: UserRole.Agent.ToString());

        var response = Assert.IsType<OkObjectResult>(controller.GetMe());
        Assert.NotNull(response.Value);
        var body = response.Value;

        Assert.Equal("17", body!.GetType().GetProperty("userId")!.GetValue(body));
        Assert.Equal("agent@example.com", body.GetType().GetProperty("email")!.GetValue(body));
        Assert.Equal("Agent", body.GetType().GetProperty("role")!.GetValue(body));
    }
}

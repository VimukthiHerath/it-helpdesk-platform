using Auth.Api.DTO;
using Auth.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class GetUsersEndpointTests
{
    [Fact]
    public async Task GetUsers_ReturnsUsersOrderedById()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.AddRange(
            AuthControllerTestHelpers.CreateUser(2, "second@example.com", UserRole.Agent),
            AuthControllerTestHelpers.CreateUser(1, "first@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = await controller.GetUsers();

        var response = Assert.IsType<OkObjectResult>(result);
        var users = Assert.IsAssignableFrom<IEnumerable<AdminUserListItemDTO>>(response.Value);
        Assert.Equal(new[] { 1, 2 }, users.Select(user => user.Id));
        Assert.Equal("first@example.com", users.First().Email);
        Assert.Equal(UserRole.Agent, users.Last().Role);
    }
}

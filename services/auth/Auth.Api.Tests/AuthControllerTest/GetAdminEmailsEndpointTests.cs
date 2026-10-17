using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Auth.Api.Tests;

public class GetAdminEmailsEndpointTests
{
    [Fact]
    public async Task GetAdminEmails_ReturnsOnlyActiveAdministrators()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        var inactiveAdmin = AuthControllerTestHelpers.CreateUser(
            1, "inactive-admin@example.com", UserRole.Administrator);
        inactiveAdmin.IsActive = false;
        context.Users.AddRange(
            inactiveAdmin,
            AuthControllerTestHelpers.CreateUser(
                2, "active-admin@example.com", UserRole.Administrator),
            AuthControllerTestHelpers.CreateUser(
                3, "employee@example.com", UserRole.Employee));
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = Assert.IsType<OkObjectResult>(await controller.GetAdminEmails());
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode ?? StatusCodes.Status200OK);
        var emails = (IEnumerable<string>)result.Value!.GetType()
            .GetProperty("emails")!.GetValue(result.Value)!;

        Assert.Equal(new[] { "active-admin@example.com" }, emails);
    }

    [Fact]
    public async Task GetAdminEmails_WhenNoActiveAdministrators_ReturnsEmptyList()
    {
        await using var context = AuthControllerTestHelpers.CreateContext();
        context.Users.AddRange(
            new User
            {
                Id = 1, Name = "Inactive", Email = "inactive@example.com",
                Password = "password", Role = UserRole.Administrator, IsActive = false
            },
            new User
            {
                Id = 2, Name = "Agent", Email = "agent@example.com",
                Password = "password", Role = UserRole.Agent, IsActive = true
            });
        await context.SaveChangesAsync();
        var controller = AuthControllerTestHelpers.CreateController(
            context, AuthControllerTestHelpers.CreateRotationClient().Object);

        var result = Assert.IsType<OkObjectResult>(await controller.GetAdminEmails());

        var emails = (IEnumerable<string>)result.Value!.GetType()
            .GetProperty("emails")!.GetValue(result.Value)!;
        Assert.Empty(emails);
    }
}

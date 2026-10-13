using Auth.Api.Controller;
using Auth.Api.Data;
using Auth.Api.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Auth.Api.Tests;

public class UserControllerTests
{
    [Fact]
    public async Task GetUsers_ReturnsUsersFromDatabase()
    {
        await using var context = CreateContext();
        context.Users.AddRange(
            new User
            {
                Id = 1,
                Name = "First User",
                Email = "first@example.com",
                Password = "hashed-password",
                Role = UserRole.Employee,
            },
            new User
            {
                Id = 2,
                Name = "Second User",
                Email = "second@example.com",
                Password = "hashed-password",
                Role = UserRole.Agent,
            });
        await context.SaveChangesAsync();

        var logger = new Mock<ILogger<UserController>>();
        var controller = new UserController(context, logger.Object);

        var result = await controller.GetUsers();

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var users = Assert.IsAssignableFrom<IEnumerable<User>>(response.Value);
        Assert.Equal(new[] { "first@example.com", "second@example.com" },
            users.Select(user => user.Email));
        logger.Verify(
            loggerMock => loggerMock.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUsers_WhenDatabaseFails_ReturnsProblemAndLogsError()
    {
        await using var context = CreateContext();
        var logger = new Mock<ILogger<UserController>>();
        var controller = new UserController(context, logger.Object);
        await context.DisposeAsync();

        var result = await controller.GetUsers();

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal("Unable to load users. Please try again later.", problem.Detail);
        logger.Verify(
            loggerMock => loggerMock.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString() == "Error retrieving users"),
                It.Is<Exception>(exception =>
                    exception.GetType() == typeof(ObjectDisposedException)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

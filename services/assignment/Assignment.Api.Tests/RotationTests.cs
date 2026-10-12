using System.Net;
using System.Security.Claims;
using Assignment.Api.Authorization;
using Assignment.Api.Controller;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Assignment.Api.Tests;

public class RotationTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private RotationController CreateController(
        ApplicationDbContext context,
        HttpMessageHandler httpMessageHandler,
        bool isAdmin = true)
    {
        var httpClient = new HttpClient(httpMessageHandler)
        {
            BaseAddress = new Uri("http://localhost:5121")
        };

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("AuthApi")).Returns(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AuthApi:BaseUrl", "http://localhost:5121" }
            })
            .Build();

        var controller = new RotationController(
            context,
            new NullLogger<RotationController>(),
            mockFactory.Object,
            config);

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "999") };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.Administrator));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    [Fact]
    public async Task AddAgentToRotation_WhenAuthApiReturns404_Returns404NotFound()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent("{\"message\":\"User not found\"}")
            });

        var controller = CreateController(context, mockHandler.Object);
        var request = new AddAgentToRotationDTO { UserId = 404 };

        // Act
        var result = await controller.AddAgentToRotation(request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFoundResult.StatusCode);

        // Verify the Assignment DB remains unchanged
        var agentInDb = await context.Agents.FirstOrDefaultAsync(a => a.UserId == 404);
        Assert.Null(agentInDb);
    }
}

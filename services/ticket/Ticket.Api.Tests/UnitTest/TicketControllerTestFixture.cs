using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Ticket.Api.Controller;
using Ticket.Api.Data;
using Ticket.Api.Services;
using Confluent.Kafka;

namespace Ticket.Api.Tests.UnitTest;

internal static class TicketControllerTestFixture
{
    internal static (ApplicationDbContext Context, Mock<ILogger<TicketController>> Logger,
        Mock<IProducer<string, string>> Producer, Mock<IConfiguration> Configuration,
        TicketStatusService StatusService) CreateDependencies()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        return (
            context,
            new Mock<ILogger<TicketController>>(),
            new Mock<IProducer<string, string>>(),
            new Mock<IConfiguration>(),
            new TicketStatusService(context));
    }

    internal static TicketController CreateController(
        ApplicationDbContext context,
        Mock<ILogger<TicketController>> logger,
        Mock<IProducer<string, string>> producer,
        Mock<IConfiguration> configuration,
        TicketStatusService statusService,
        int? userId = null,
        string[]? roles = null)
    {
        var controller = new TicketController(
            context, logger.Object, producer.Object, configuration.Object, statusService);
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>();

        if (userId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        foreach (var role in roles ?? Array.Empty<string>())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = httpContext
        };
        return controller;
    }
}

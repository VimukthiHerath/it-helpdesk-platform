using System.Security.Claims;
using Assignment.Api.Controller;
using Assignment.Api.Data;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Assignment.Api.Tests.UnitTest;

internal static class AssignmentsControllerTestFixture
{
    internal static (ApplicationDbContext Context, Mock<ILogger<AssignmentsController>> Logger,
        Mock<IProducer<string, string>> Producer, Mock<IConfiguration> Configuration,
        Mock<IHttpClientFactory> HttpClientFactory) CreateDependencies()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return (
            new ApplicationDbContext(options),
            new Mock<ILogger<AssignmentsController>>(),
            new Mock<IProducer<string, string>>(),
            new Mock<IConfiguration>(),
            new Mock<IHttpClientFactory>());
    }

    internal static AssignmentsController CreateController(
        ApplicationDbContext context,
        Mock<ILogger<AssignmentsController>> logger,
        Mock<IProducer<string, string>> producer,
        Mock<IConfiguration> configuration,
        Mock<IHttpClientFactory> httpClientFactory,
        int? userId = null,
        string authorization = "Bearer test-token")
    {
        var controller = new AssignmentsController(
            context, logger.Object, producer.Object, configuration.Object, httpClientFactory.Object);

        var httpContext = new DefaultHttpContext();
        if (userId.HasValue)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "Test"));
        }

        httpContext.Request.Headers.Authorization = authorization;
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = httpContext
        };
        return controller;
    }

    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        internal StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}

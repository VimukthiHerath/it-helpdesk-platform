using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class AuthApiAdminEmailResolverTests
{
    [Fact]
    public async Task ResolveAdminEmailsAsync_SuccessfulResponse_ReturnsEmailsAndUsesConfiguredUrl()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """{"emails":["admin1@example.com","admin2@example.com"]}""");
        var resolver = CreateResolver(CreateFactory(handler), "https://auth.example.test");

        var result = await resolver.ResolveAdminEmailsAsync(CancellationToken.None);

        Assert.Equal(new[] { "admin1@example.com", "admin2@example.com" }, result);
        Assert.Equal(
            "https://auth.example.test/api/auth/internal/admins/emails",
            handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task ResolveAdminEmailsAsync_NullResponse_ReturnsEmptyList()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "null");
        var resolver = CreateResolver(CreateFactory(handler), "https://auth.example.test");

        var result = await resolver.ResolveAdminEmailsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAdminEmailsAsync_RequestFails_ReturnsEmptyListAndLogsError()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException("Auth API unavailable"));
        var logger = new Mock<ILogger<AuthApiAdminEmailResolver>>();
        var resolver = CreateResolver(CreateFactory(handler), "https://auth.example.test", logger);

        var result = await resolver.ResolveAdminEmailsAsync(CancellationToken.None);

        Assert.Empty(result);
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static AuthApiAdminEmailResolver CreateResolver(
        Mock<IHttpClientFactory> factory,
        string baseUrl,
        Mock<ILogger<AuthApiAdminEmailResolver>>? logger = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthApiUrl"] = baseUrl
            })
            .Build();
        return new AuthApiAdminEmailResolver(
            factory.Object,
            configuration,
            (logger ?? new Mock<ILogger<AuthApiAdminEmailResolver>>()).Object);
    }

    private static Mock<IHttpClientFactory> CreateFactory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler));
        return factory;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            };
        }

        public StubHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            if (_exception is not null)
            {
                return Task.FromException<HttpResponseMessage>(_exception);
            }

            return Task.FromResult(_response!);
        }
    }
}

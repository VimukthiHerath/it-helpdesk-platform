using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Notification.Api.Services;
using Xunit;

namespace Notification.Api.Tests.UnitTesting;

public class AuthApiUserEmailResolverTests
{
    [Fact]
    public async Task ResolveUserEmailAsync_SuccessfulResponse_ReturnsEmailAndUsesConfiguredUrl()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """{"email":"user@example.com","name":"User"}""");
        var factory = CreateFactory(handler);
        var resolver = CreateResolver(factory, "https://auth.example.test");

        var result = await resolver.ResolveUserEmailAsync(42, CancellationToken.None);

        Assert.Equal("user@example.com", result);
        Assert.Equal(
            "https://auth.example.test/api/auth/internal/users/42/email",
            handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task ResolveUserEmailAsync_NullResponse_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "null");
        var resolver = CreateResolver(CreateFactory(handler), "https://auth.example.test");

        var result = await resolver.ResolveUserEmailAsync(42, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveUserEmailAsync_RequestFails_ReturnsNullAndLogsError()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException("Auth API unavailable"));
        var logger = new Mock<ILogger<AuthApiUserEmailResolver>>();
        var resolver = CreateResolver(CreateFactory(handler), "https://auth.example.test", logger);

        var result = await resolver.ResolveUserEmailAsync(42, CancellationToken.None);

        Assert.Null(result);
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static AuthApiUserEmailResolver CreateResolver(
        Mock<IHttpClientFactory> factory,
        string baseUrl,
        Mock<ILogger<AuthApiUserEmailResolver>>? logger = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthApiUrl"] = baseUrl
            })
            .Build();
        return new AuthApiUserEmailResolver(
            factory.Object,
            configuration,
            (logger ?? new Mock<ILogger<AuthApiUserEmailResolver>>()).Object);
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

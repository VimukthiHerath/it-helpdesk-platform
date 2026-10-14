using System.Net;
using System.Net.Http.Json;
using Assignment.Api.Controller;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Assignment.Api.Tests.UnitTest;

public class AddAgentToRotationTests
{
    [Fact]
    public async Task AddAgentToRotation_WhenModelStateIsInvalid_ReturnsValidationProblemWithoutCallingAuthApi()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);
        controller.ModelState.AddModelError(nameof(AddAgentToRotationDTO.UserId), "UserId is required.");

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO());

        Assert.IsType<ObjectResult>(result);
        httpClientFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddAgentToRotation_WhenAuthUserIsAgent_ReturnsCreatedAndPersistsAgent()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.Add(new Agent { UserId = 1, DisplayOrder = 3 });
        context.SaveChanges();
        configuration.SetupGet(x => x["AuthApiUrl"]).Returns("http://auth.test");
        var request = new HttpRequestMessage();
        var handler = new AssignmentsControllerTestFixture.StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new AuthUserDto { Id = 42, Role = 2 } })
            });
        var client = new HttpClient(handler);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<AgentDTO>(created.Value);
        Assert.Equal(42, response.UserId);
        Assert.Equal(4, response.DisplayOrder);
        Assert.Equal(42, context.Agents.Single(agent => agent.UserId == 42).UserId);
        httpClientFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AddAgentToRotation_ForwardsAuthorizationTokenAndCallsUsersEndpoint()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        HttpRequestMessage? receivedRequest = null;
        var client = new HttpClient(new AssignmentsControllerTestFixture.StubHttpMessageHandler(request =>
        {
            receivedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new AuthUserDto { Id = 42, Role = 2 } })
            };
        }));
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        Assert.NotNull(receivedRequest);
        Assert.Equal(HttpMethod.Get, receivedRequest!.Method);
        Assert.Equal("Bearer test-token", receivedRequest.Headers.Authorization!.ToString());
        Assert.EndsWith("/api/auth/users", receivedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task AddAgentToRotation_WhenAuthApiFails_ReturnsProblemWithoutPersisting()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        var client = new HttpClient(new AssignmentsControllerTestFixture.StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(context.Agents);
    }

    [Fact]
    public async Task AddAgentToRotation_WhenUserIsMissing_ReturnsNotFoundAndDoesNotSave()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(
            new AssignmentsControllerTestFixture.StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Array.Empty<AuthUserDto>())
                })));
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(context.Agents);
    }

    [Fact]
    public async Task AddAgentToRotation_WhenUserIsNotAgent_ReturnsBadRequest()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(
            new AssignmentsControllerTestFixture.StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new AuthUserDto { Id = 42, Role = 1 } })
                })));
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddAgentToRotation_WhenAlreadyInRotation_ReturnsConflict()
    {
        var (context, logger, producer, configuration, httpClientFactory) =
            AssignmentsControllerTestFixture.CreateDependencies();
        context.Agents.Add(new Agent { UserId = 42, DisplayOrder = 0 });
        context.SaveChanges();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(
            new AssignmentsControllerTestFixture.StubHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new AuthUserDto { Id = 42, Role = 2 } })
                })));
        var controller = AssignmentsControllerTestFixture.CreateController(
            context, logger, producer, configuration, httpClientFactory);

        var result = await controller.AddAgentToRotation(new AddAgentToRotationDTO { UserId = 42 });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(context.Agents);
    }
}

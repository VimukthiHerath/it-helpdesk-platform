using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sla.Api.Data;
using Xunit;

namespace Sla.Api.Tests;

public class SlaApiAuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SlaApiAuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Remove MySQL registration and replace with in-memory db for integration test
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SlaDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<SlaDbContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestSlaDb");
                });
            });
        });
    }

    [Fact]
    public async Task GetTiers_WithoutToken_Returns401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/Sla/admin/tiers");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

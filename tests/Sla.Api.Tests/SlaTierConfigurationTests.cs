using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sla.Api.Controller;
using Sla.Api.Data;
using Sla.Api.Model;
using Xunit;
using Sla.Api.Services;
using Sla.Api.DTO;

namespace Sla.Api.Tests;

public class SlaTierConfigurationTests
{
    private SlaDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        
        var context = new SlaDbContext(options);
        // Ensure default data like standard migrations
        context.Database.EnsureCreated();
        return context;
    }
    
    private AdminSlaController CreateController(SlaDbContext context, IMemoryCache memoryCache, bool isAdmin = true)
    {
        var controller = new AdminSlaController(context, memoryCache, new NullLogger<AdminSlaController>());
        
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "Admin777") };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };

        return controller;
    }

    [Fact]
    public async Task UpdateTier_WithValidAdmin_UpdatesDatabaseAndInvalidatesCache_Returns200()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var tierName = "HIGH";
        
        // Setup cache beforehand to verify it gets removed
        var cacheKey = $"SlaTier_{tierName}";
        cache.Set(cacheKey, 9999);
        
        var controller = CreateController(context, cache);
        var request = new UpdateTierRequest { DurationMinutes = 120 };

        // Act
        var result = await controller.UpdateTier(tierName, request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        
        var updatedPolicy = await context.SlaTierPolicies.FirstOrDefaultAsync(p => p.TierName == tierName);
        Assert.NotNull(updatedPolicy);
        Assert.Equal(120, updatedPolicy.DurationMinutes);
        Assert.Equal("Admin777", updatedPolicy.UpdatedByAdminId);
        
        // Assert cache eviction
        Assert.False(cache.TryGetValue(cacheKey, out _));
    }

    [Fact]
    public async Task UpdateTier_WithNegativeOrExtremeDuration_Returns400BadRequest()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(context, cache);

        // Act & Assert for Negative
        var requestNeg = new UpdateTierRequest { DurationMinutes = -50 };
        var resultNeg = await controller.UpdateTier("HIGH", requestNeg, CancellationToken.None);
        var badReqObj = Assert.IsType<BadRequestObjectResult>(resultNeg);
        Assert.Equal(400, badReqObj.StatusCode);

        // Act & Assert for Extreme (> 43200)
        var requestExt = new UpdateTierRequest { DurationMinutes = 50000 };
        var resultExt = await controller.UpdateTier("HIGH", requestExt, CancellationToken.None);
        var badReqObj2 = Assert.IsType<BadRequestObjectResult>(resultExt);
        Assert.Equal(400, badReqObj2.StatusCode);
    }

    [Fact]
    public async Task UpdateTier_WithInvalidTierName_Returns404NotFound()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(context, cache);
        var request = new UpdateTierRequest { DurationMinutes = 120 };

        // Act
        var result = await controller.UpdateTier("NONEXISTENT", request, CancellationToken.None);

        // Assert
        var notFoundObj = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFoundObj.StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAdminRole_Return403Forbidden()
    {
        // Basic check for Authorize(Roles = "Administrator") attribute
        var type = typeof(AdminSlaController);
        var authorizeAttribute = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            Attribute.GetCustomAttribute(type, typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute))!;

        Assert.NotNull(authorizeAttribute);
        var roles = authorizeAttribute.Roles!.Split(',');
        Assert.Contains("Administrator", roles);
    }

    [Fact]
    public async Task NewTicket_AppliesUpdatedDuration_FromCacheOrDatabase()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        
        // Set policy to 120 in DB
        var policy = await context.SlaTierPolicies.FirstOrDefaultAsync(p => p.TierName == "HIGH");
        policy!.DurationMinutes = 120;
        await context.SaveChangesAsync();

        var scopeFactoryMock = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scopeMock = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        
        serviceProviderMock.Setup(sp => sp.GetService(typeof(SlaDbContext))).Returns(context);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(sf => sf.CreateScope()).Returns(scopeMock.Object);
        
        // Mock the consumer internally just to invoke its protected method
        // To do this strictly in standard test we can reflect or make the method public for testing
        // As TicketCreatedConsumer.ProcessSlaAsync is private, we will reflect into it.
        var consumer = new TicketCreatedConsumer(new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object, new NullLogger<TicketCreatedConsumer>(), scopeFactoryMock.Object, cache);
        var ticketEvent = new TicketCreatedEvent { TicketId = 10, Urgency = 3, CreatedAtUtc = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc) };
        
        var methodInfo = typeof(TicketCreatedConsumer).GetMethod("ProcessSlaAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var task = (Task)methodInfo!.Invoke(consumer, new object[] { ticketEvent })!;
        await task;

        // Assert
        var createdSla = await context.TicketSlas.FirstOrDefaultAsync(ts => ts.TicketId == 10);
        Assert.NotNull(createdSla);
        // 10:00:00 + 120 minutes = 12:00:00
        Assert.Equal(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc), createdSla.TargetResolutionTimeUtc);
        
        // Verify cache was populated
        Assert.True(cache.TryGetValue("SlaTier_HIGH", out int cachedDuration));
        Assert.Equal(120, cachedDuration);
    }
    
    [Fact]
    public async Task ExistingTickets_AreNotAffected_ByTierPolicyUpdate()
    {
        // By design, the controller PUT endpoint only updates SlaTierPolicies,
        // and doesn't query or touch TicketSlas at all, maintaining non-retroactive integrity.
        
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = CreateController(context, cache);
        
        // Seed an existing TicketSla that was created BEFORE with 60 mins offset
        var originalTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        context.TicketSlas.Add(new TicketSla { TicketId = 20, TargetResolutionTimeUtc = originalTime.AddMinutes(60) });
        await context.SaveChangesAsync();

        var request = new UpdateTierRequest { DurationMinutes = 90 };

        // Act
        var result = await controller.UpdateTier("LOW", request, CancellationToken.None);

        // Assert
        Assert.Equal(200, (result as OkObjectResult)!.StatusCode);
        
        // Verification that preexisting ticket was not altered
        var existingTicket = await context.TicketSlas.FirstOrDefaultAsync(t => t.TicketId == 20);
        Assert.NotNull(existingTicket);
        Assert.Equal(originalTime.AddMinutes(60), existingTicket.TargetResolutionTimeUtc);
    }
}

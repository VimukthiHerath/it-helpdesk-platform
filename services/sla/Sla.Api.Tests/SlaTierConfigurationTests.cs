using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Sla.Api.Controller;
using Sla.Api.Data;
using Sla.Api.DTO;
using Sla.Api.Services;
using Xunit;

namespace Sla.Api.Tests;

public class SlaTierConfigurationTests
{
    private static SlaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SlaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new SlaDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static AdminSlaController CreateController(SlaDbContext context, IMemoryCache cache)
    {
        var controller = new AdminSlaController(context, cache, NullLogger<AdminSlaController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "Admin777"), new Claim(ClaimTypes.Role, "Administrator") },
                    "TestAuth"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task UpdateTier_ChangesDurationAndEvictsCachedValue()
    {
        await using var context = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("SlaTier_HIGH", 9999);

        var result = await CreateController(context, cache).UpdateTier(
            "HIGH", new UpdateTierRequest { DurationMinutes = 120 }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var policy = await context.SlaTierPolicies.SingleAsync(p => p.TierName == "HIGH");
        Assert.Equal(120, policy.DurationMinutes);
        Assert.Equal("Admin777", policy.UpdatedByAdminId);
        Assert.False(cache.TryGetValue("SlaTier_HIGH", out _));
    }

    [Fact]
    public async Task NewTicket_UsesUpdatedPolicyWithoutChangingExistingDeadline()
    {
        await using var context = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new SlaRecordCreationService(context, NullLogger<SlaRecordCreationService>.Instance, cache);
        var createdAt = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync(new TicketCreatedEvent { TicketId = 1, Urgency = 2, CreatedAtUtc = createdAt }, "event-1", CancellationToken.None);
        var originalDeadline = (await context.TicketSlas.SingleAsync(t => t.TicketId == 1)).DeadlineUtc;

        await CreateController(context, cache).UpdateTier("HIGH", new UpdateTierRequest { DurationMinutes = 120 }, CancellationToken.None);
        await service.CreateAsync(new TicketCreatedEvent { TicketId = 2, Urgency = 2, CreatedAtUtc = createdAt }, "event-2", CancellationToken.None);

        Assert.Equal(originalDeadline, (await context.TicketSlas.SingleAsync(t => t.TicketId == 1)).DeadlineUtc);
        Assert.Equal(createdAt.AddMinutes(120), (await context.TicketSlas.SingleAsync(t => t.TicketId == 2)).DeadlineUtc);
    }
}

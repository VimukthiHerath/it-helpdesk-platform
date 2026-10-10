using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sla.Api.Data;
using System.Security.Claims;

namespace Sla.Api.Controller;

[ApiController]
[Route("api/Sla/admin/tiers")]
[Authorize(Roles = "Administrator")]
public sealed class AdminSlaController : ControllerBase
{
    private readonly SlaDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AdminSlaController> _logger;

    public AdminSlaController(
        SlaDbContext context,
        IMemoryCache memoryCache,
        ILogger<AdminSlaController> logger)
    {
        _context = context;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetTiers(CancellationToken cancellationToken)
    {
        var tiers = await _context.SlaTierPolicies.ToListAsync(cancellationToken);
        return Ok(tiers);
    }

    [HttpPut("{tierName}")]
    public async Task<IActionResult> UpdateTier(
        string tierName,
        [FromBody] UpdateTierRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DurationMinutes <= 0 || request.DurationMinutes > 43200)
        {
            return BadRequest(new { message = "Deadline duration must be a positive integer between 1 and 43200 minutes." });
        }

        var policy = await _context.SlaTierPolicies
            .FirstOrDefaultAsync(p => p.TierName.ToLower() == tierName.ToLower(), cancellationToken);

        if (policy is null)
        {
            return NotFound(new { message = $"SLA tier '{tierName}' not found." });
        }

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        policy.DurationMinutes = request.DurationMinutes;
        policy.LastUpdatedAtUtc = DateTime.UtcNow;
        policy.UpdatedByAdminId = adminId;

        await _context.SaveChangesAsync(cancellationToken);

        var cacheKey = $"SlaTier_{policy.TierName.ToUpper()}";
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation("Admin {AdminId} updated SLA Tier {TierName} to {DurationMinutes} minutes.",
            adminId, policy.TierName, policy.DurationMinutes);

        return Ok(policy);
    }
}

public sealed class UpdateTierRequest
{
    public int DurationMinutes { get; set; }
}

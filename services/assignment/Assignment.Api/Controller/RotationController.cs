using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Assignment.Api.Controller;

[ApiController]
[Route("api/Assignment/[controller]")]
[Authorize(Roles = Roles.Administrator)]
public class RotationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RotationController> _logger;

    public RotationController(ApplicationDbContext context, ILogger<RotationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> AddAgentToRotation([FromBody] AddAgentToRotationDTO request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var existingAgent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == request.UserId);
            var adminId = GetAuthenticatedUserId();

            if (existingAgent != null)
            {
                if (existingAgent.IsActive)
                {
                    return Conflict(new { message = "Agent is already active in the rotation pool." });
                }
                else
                {
                    existingAgent.IsActive = true;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Admin {AdminId} added/reactivated Agent {AgentId} to the rotation pool.", adminId, request.UserId);
                    return Ok(new { message = "Agent reactivated successfully." });
                }
            }

            var maxDisplayOrder = await _context.Agents
                .Select(a => (int?)a.DisplayOrder)
                .MaxAsync();

            var agent = new Agent
            {
                UserId = request.UserId,
                DisplayOrder = (maxDisplayOrder ?? -1) + 1,
                IsActive = true
            };

            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin {AdminId} added/reactivated Agent {AgentId} to the rotation pool.", adminId, request.UserId);

            var response = new AgentDTO
            {
                Id = agent.Id,
                UserId = agent.UserId,
                DisplayOrder = agent.DisplayOrder,
                IsActive = agent.IsActive
            };

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding agent to rotation");
            return Problem("Unable to add the agent to the rotation. Please try again later.");
        }
    }

    [HttpDelete("{agentId}")]
    public async Task<IActionResult> RemoveAgentFromRotation(int agentId)
    {
        try
        {
            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == agentId);
            if (agent == null)
            {
                return NotFound(new { message = "Agent not found in the rotation pool." });
            }

            agent.IsActive = false;

            var activeAgentsCount = await _context.Agents.CountAsync(a => a.IsActive && a.UserId != agentId);
            if (activeAgentsCount == 0)
            {
                _logger.LogWarning("No active agents available for round-robin.");
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Agent removed from the rotation pool." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing agent from rotation");
            return Problem("Unable to remove the agent from the rotation. Please try again later.");
        }
    }

    private int GetAuthenticatedUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (int.TryParse(userIdString, out var userId))
        {
            return userId;
        }
        return 0; // Fallback or handle accordingly
    }
}

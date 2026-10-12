using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Assignment.Api.Controller;

// Auth.Api uses this status endpoint before changing an agent's role or
// deactivating their account. Roster management remains in AssignmentsController.
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

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetAgentRotationStatus(int agentId)
    {
        try
        {
            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == agentId);
            if (agent is null)
            {
                return NotFound(new { message = $"Agent with user ID {agentId} not found." });
            }

            return Ok(new
            {
                userId = agent.UserId,
                isActive = agent.IsActive,
                displayOrder = agent.DisplayOrder
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent {AgentId} status", agentId);
            return Problem("Unable to load agent status. Please try again later.");
        }
    }
}

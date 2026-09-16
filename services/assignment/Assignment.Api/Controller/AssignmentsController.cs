using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Assignment.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssignmentsController> _logger;

    public AssignmentsController(ApplicationDbContext context, ILogger<AssignmentsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // AC1: only tickets assigned to the caller. AC2: most urgent first - lower
    // Urgency value means more urgent (see TicketAssignment), so ascending
    // order is "most urgent first". Ties broken oldest-assigned-first (FIFO).
    [Authorize(Roles = Roles.Agent)]
    [HttpGet("queue")]
    public IActionResult GetQueue()
    {
        try
        {
            var userId = GetAuthenticatedUserId();
            var agent = _context.Agents.FirstOrDefault(a => a.UserId == userId);

            // Caller is a valid Agent-role user but isn't in the rotation table
            // (e.g. not yet onboarded into Assignments' Agents seed) - an empty
            // queue is the honest answer, not an error.
            if (agent is null)
            {
                return Ok(Array.Empty<AssignmentQueueItemDTO>());
            }

            var queue = _context.Assignments
                .Where(a => a.AgentId == agent.Id)
                .OrderBy(a => a.Urgency)
                .ThenBy(a => a.AssignedAtUtc)
                .Select(a => new AssignmentQueueItemDTO
                {
                    TicketId = a.TicketId,
                    Urgency = a.Urgency,
                    AssignedAtUtc = a.AssignedAtUtc,
                })
                .ToList();

            return Ok(queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving assignment queue");
            return Problem("Unable to load your queue. Please try again later.");
        }
    }

    // Administrator manages who's in the round-robin rotation. Assignment.Api
    // takes the raw Auth user id on trust (no cross-service call to verify the
    // user exists or is Agent-role) - same "no cross-service calls" convention
    // as everywhere else in this service.
    [Authorize(Roles = Roles.Administrator)]
    [HttpGet("agents")]
    public IActionResult GetAgents()
    {
        try
        {
            var agents = _context.Agents
                .OrderBy(a => a.DisplayOrder)
                .Select(a => new AgentDTO
                {
                    Id = a.Id,
                    UserId = a.UserId,
                    DisplayOrder = a.DisplayOrder,
                })
                .ToList();

            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving rotation agents");
            return Problem("Unable to load the agent rotation. Please try again later.");
        }
    }

    [Authorize(Roles = Roles.Administrator)]
    [HttpPost("agents")]
    public async Task<IActionResult> AddAgentToRotation([FromBody] AddAgentToRotationDTO request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var alreadyInRotation = await _context.Agents.AnyAsync(a => a.UserId == request.UserId);
            if (alreadyInRotation)
            {
                return Conflict(new { message = "That user is already in the rotation." });
            }

            // Appends to the end of the rotation. No locking against a
            // concurrent add - same single-writer assumption already
            // documented on RoundRobinAssignmentService.
            var maxDisplayOrder = await _context.Agents
                .Select(a => (int?)a.DisplayOrder)
                .MaxAsync();

            var agent = new Agent
            {
                UserId = request.UserId,
                DisplayOrder = (maxDisplayOrder ?? -1) + 1,
            };

            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            var response = new AgentDTO
            {
                Id = agent.Id,
                UserId = agent.UserId,
                DisplayOrder = agent.DisplayOrder,
            };

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding agent to rotation");
            return Problem("Unable to add the agent to the rotation. Please try again later.");
        }
    }

    private int GetAuthenticatedUserId()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.Parse(userId!);
    }
}

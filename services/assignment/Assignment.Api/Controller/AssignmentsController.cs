using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    private int GetAuthenticatedUserId()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.Parse(userId!);
    }
}

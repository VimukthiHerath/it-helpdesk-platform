using Assignment.Api.Authorization;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace Assignment.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssignmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssignmentsController> _logger;
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IConfiguration _configuration;

    public AssignmentsController(
        ApplicationDbContext context,
        ILogger<AssignmentsController> logger,
        IProducer<string, string> kafkaProducer,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
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

    // Supports the admin/agent "all tickets" reassignment view - there was
    // previously no way to see assignments across every agent, only your
    // own (GetQueue). Not one of SCRUM-20's three ACs directly, but the
    // reassign feature is unusable without a way to see who currently has
    // what first.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpGet]
    public IActionResult GetAllAssignments()
    {
        try
        {
            var assignments = _context.Assignments
                .Join(_context.Agents, a => a.AgentId, ag => ag.Id, (a, ag) => new AssignmentListItemDTO
                {
                    TicketId = a.TicketId,
                    AgentUserId = ag.UserId,
                    Urgency = a.Urgency,
                    AssignedAtUtc = a.AssignedAtUtc,
                })
                .OrderBy(a => a.TicketId)
                .ToList();

            return Ok(assignments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all assignments");
            return Problem("Unable to load assignments. Please try again later.");
        }
    }

    // ASSIGN-5 (SCRUM-20): manual override for a wrong or stale round-robin
    // assignment. AC1: takes a new agent (by Auth user id, same identity
    // convention as AddAgentToRotationDTO - not this service's internal
    // Agent.Id). AC2: updates the *existing* TicketAssignment row in place
    // (no new row, no history table - not asked for) and republishes
    // TicketAssigned so any future consumer of that topic sees the new
    // owner. AC3 falls out of AC2 for free: GetQueue filters by AgentId, so
    // once this row's AgentId changes, the old agent's query stops
    // returning it and the new agent's starts - no separate code needed.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpPatch("{ticketId}/reassign")]
    public async Task<IActionResult> ReassignTicket(int ticketId, [FromBody] ReassignTicketDTO request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var assignment = await _context.Assignments.FirstOrDefaultAsync(a => a.TicketId == ticketId);
            if (assignment is null)
            {
                return NotFound(new { message = "No assignment found for that ticket." });
            }

            var newAgent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == request.NewAgentUserId);
            if (newAgent is null)
            {
                return BadRequest(new { message = "That user is not in the agent rotation." });
            }

            if (newAgent.Id == assignment.AgentId)
            {
                return Conflict(new { message = "Ticket is already assigned to that agent." });
            }

            var previousAgentId = assignment.AgentId;
            assignment.AgentId = newAgent.Id;
            // Reflects when the new agent actually received it - keeps their
            // queue's oldest-first tiebreak (see GetQueue) meaningful instead
            // of the ticket jumping in ahead of tickets they've had longer.
            assignment.AssignedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await PublishTicketAssignedAsync(assignment.TicketId, newAgent.UserId, assignment.AssignedAtUtc);

            _logger.LogInformation(
                "Reassigned ticket {TicketId} from agent row {PreviousAgentId} to agent user {NewAgentUserId}",
                ticketId, previousAgentId, newAgent.UserId);

            return Ok(new ReassignTicketResponseDTO
            {
                TicketId = assignment.TicketId,
                AgentUserId = newAgent.UserId,
                Urgency = assignment.Urgency,
                AssignedAtUtc = assignment.AssignedAtUtc,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reassigning ticket {TicketId}", ticketId);
            return Problem("Unable to reassign the ticket. Please try again later.");
        }
    }

    private async Task PublishTicketAssignedAsync(int ticketId, int agentUserId, DateTime assignedAtUtc)
    {
        var ticketAssignedEvent = new TicketAssignedEvent
        {
            TicketId = ticketId,
            AgentUserId = agentUserId,
            AssignedAtUtc = assignedAtUtc,
        };

        var topic = _configuration["Kafka:TicketAssignedTopic"] ?? "ticket-assigned";
        var payload = JsonSerializer.Serialize(ticketAssignedEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await _kafkaProducer.ProduceAsync(topic, new Message<string, string>
        {
            Key = ticketId.ToString(),
            Value = payload,
        });
    }

    private int GetAuthenticatedUserId()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.Parse(userId!);
    }
}

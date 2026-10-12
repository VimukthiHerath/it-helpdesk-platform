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
    private readonly IHttpClientFactory _httpClientFactory;

    public AssignmentsController(
        ApplicationDbContext context,
        ILogger<AssignmentsController> logger,
        IProducer<string, string> kafkaProducer,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _logger = logger;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
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

            // Match on agent.Id if rotation row exists, or directly on userId if assignments store Auth user ID
            var targetAgentId = agent?.Id;

            var queue = _context.Assignments
                .Where(a => (targetAgentId != null && a.AgentId == targetAgentId) || a.AgentId == userId)
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

    // Viewing the rotation is Agent+Administrator (SCRUM-20 needs agents to
    // see who they can reassign a ticket to, not just admins) - but managing
    // it (below) stays Administrator-only. Assignment.Api takes the raw Auth
    // user id on trust (no cross-service call to verify the user exists or
    // is Agent-role) - same "no cross-service calls" convention as
    // everywhere else in this service.
    // Viewing the rotation is Agent+Administrator (SCRUM-20 needs agents to
    // see who they can reassign a ticket to, not just admins) - but managing
    // it (below) stays Administrator-only. Assignment.Api takes the raw Auth
    // user id on trust (no cross-service call to verify the user exists or
    // is Agent-role) - same "no cross-service calls" convention as
    // everywhere else in this service.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpGet("agents")]
    public IActionResult GetAgents()
    {
        try
        {
            var agents = _context.Agents
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayOrder)
                .Select(a => new AgentDTO
                {
                    Id = a.Id,
                    UserId = a.UserId,
                    DisplayOrder = a.DisplayOrder,
                    IsActive = a.IsActive
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
            // Cross-service call to Auth.Api to verify user exists and is an Agent
            var authApiUrl = _configuration["AuthApiUrl"] ?? "http://localhost:5121";
            var client = _httpClientFactory.CreateClient();
            
            // Forward the caller's authorization token
            if (Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var token = authHeader.ToString().Replace("Bearer ", "").Replace("bearer ", "");
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var responseAuth = await client.GetAsync($"{authApiUrl}/api/auth/users");
            if (!responseAuth.IsSuccessStatusCode)
            {
                return Problem("Unable to verify user role with Auth API.");
            }

            var users = await responseAuth.Content.ReadFromJsonAsync<List<AuthUserDto>>();
            var targetUser = users?.FirstOrDefault(u => u.Id == request.UserId);

            if (targetUser == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (targetUser.Role != 2) // 2 = Agent
            {
                return BadRequest(new { message = "Only Agent-role accounts can be added to the rotation." });
            }

            var existingAgent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == request.UserId);
            if (existingAgent != null)
            {
                if (existingAgent.IsActive)
                {
                    return Conflict(new { message = "That user is already in the rotation." });
                }

                // Reactivate agent at the end of the rotation
                var maxOrder = await _context.Agents
                    .Where(a => a.IsActive)
                    .Select(a => (int?)a.DisplayOrder)
                    .MaxAsync();

                existingAgent.IsActive = true;
                existingAgent.DisplayOrder = (maxOrder ?? -1) + 1;
                await _context.SaveChangesAsync();

                var reactivatedResponse = new AgentDTO
                {
                    Id = existingAgent.Id,
                    UserId = existingAgent.UserId,
                    DisplayOrder = existingAgent.DisplayOrder,
                };

                return StatusCode(StatusCodes.Status201Created, reactivatedResponse);
            }

            // Appends to the end of the rotation. No locking against a
            // concurrent add - same single-writer assumption already
            // documented on RoundRobinAssignmentService.
            var maxDisplayOrder = await _context.Agents
                .Where(a => a.IsActive)
                .Select(a => (int?)a.DisplayOrder)
                .MaxAsync();

            var agent = new Agent
            {
                UserId = request.UserId,
                DisplayOrder = (maxDisplayOrder ?? -1) + 1,
                IsActive = true,
            };

            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

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

    [Authorize(Roles = Roles.Administrator)]
    [HttpDelete("agents/{userId}")]
    public async Task<IActionResult> RemoveAgentFromRotation(int userId)
    {
        try
        {
            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive);
            
            if (agent == null)
            {
                return NotFound(new { message = "Agent not found in rotation." });
            }

            // ASSIGN-5: Soft-delete by setting IsActive = false so existing ticket assignments remain intact
            agent.IsActive = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing agent {UserId} from rotation", userId);
            return Problem("Unable to remove the agent from the rotation. Please try again later.");
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
            var assignments = (from a in _context.Assignments
                               join ag in _context.Agents on a.AgentId equals ag.Id into agentJoin
                               from ag in agentJoin.DefaultIfEmpty()
                               orderby a.TicketId
                               select new AssignmentListItemDTO
                               {
                                   TicketId = a.TicketId,
                                   AgentUserId = ag != null ? ag.UserId : a.AgentId,
                                   Urgency = a.Urgency,
                                   AssignedAtUtc = a.AssignedAtUtc,
                               })
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
    //
    // Administrator-only by explicit request: the original story's "agent
    // or admin" framing was narrowed after the fact - reassigning someone
    // else's ticket away from them is an admin call, not something any
    // agent should be able to do to any other agent.
    [Authorize(Roles = Roles.Administrator)]
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

public class AuthUserDto
{
    public int Id { get; set; }
    public int Role { get; set; }
}

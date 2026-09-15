using Confluent.Kafka;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Ticket.Api.Authorization;
using Ticket.Api.Data;
using Ticket.Api.DTO;
using Ticket.Api.Model;
using Ticket.Api.Services;

namespace Ticket.Api.Controller;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TicketController : ControllerBase
{

    private readonly ApplicationDbContext _context;
    private readonly ILogger<TicketController> _logger;
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IConfiguration _configuration;
    private readonly TicketStatusService _statusService;

    public TicketController(
        ApplicationDbContext context,
        ILogger<TicketController> logger,
        IProducer<string, string> kafkaProducer,
        IConfiguration configuration,
        TicketStatusService statusService)
    {
        _context = context;
        _logger = logger;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
        _statusService = statusService;
    }

    // Cross-user visibility: only staff who triage/resolve tickets need to see
    // every user's tickets, not the employees who file them.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpGet]
    public IActionResult GetTickets()
    {
        try
        {
            var tickets = _context.Tickets.ToList();
            return Ok(tickets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tickets");
            return Problem("Unable to load tickets. Please try again later.");
        }
    }

    [Authorize(Roles = Roles.Employee)]
    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] TicketCreateDTO ticketDto)
    {
        // AC1: ValidationProblem()'s default shape (an "errors" dictionary,
        // no top-level "message") doesn't match this API's convention
        // elsewhere of a plain { message } the frontend can show directly.
        // Surface the first failing field's own message instead - it
        // already names the field per TicketCreateDTO's ErrorMessage text.
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                ?? "Please check the ticket details and try again.";

            return BadRequest(new { message = firstError });
        }

        int? createdTicketId = null;

        try
        {
            var userId = GetAuthenticatedUserId();

            var ticket = new Tickets
            {
                Description = ticketDto.Description,
                IssueType = ticketDto.IssueType,
                Urgency = ticketDto.Urgency,
                CreatedBy = userId,
            };

            _context.Tickets.Add(ticket);
            await _context.SaveChangesAsync();
            createdTicketId = ticket.Id;

            var ticketCreatedEvent = new TicketCreatedEvent
            {
                TicketId = ticket.Id,
                Description = ticket.Description,
                IssueType = ticket.IssueType,
                Urgency = ticket.Urgency,
                Status = ticket.Status,
                CreatedBy = ticket.CreatedBy,
                CreatedAtUtc = ticket.CreatedAt
            };

            var topicName = _configuration["Kafka:TicketCreatedTopic"] ?? "ticket-created";
            var eventPayload = JsonSerializer.Serialize(ticketCreatedEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await _kafkaProducer.ProduceAsync(topicName, new Message<string, string>
            {
                Key = ticket.Id.ToString(),
                Value = eventPayload
            });

            _logger.LogInformation("Published TicketCreated event for ticket {TicketId} to topic {Topic}", ticket.Id, topicName);
            return StatusCode(StatusCodes.Status201Created, new { message = "Ticket created successfully", ticketId = ticket.Id });
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Ticket created in DB, but failed to publish TicketCreated event");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "Ticket created, but failed to publish TicketCreated event.",
                    ticketId = createdTicketId
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ticket");
            // AC2: deliberately different wording from a validation message -
            // this tells the user it's not something they typed wrong, it's
            // safe/expected to just retry.
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Something went wrong on our end. Please try again in a moment." });
        }
    }

    private int GetAuthenticatedUserId()
    {
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.Parse(userId!);
    }

    [Authorize(Roles = Roles.Employee)]
    [HttpGet("mine")]
    public IActionResult GetMyTickets()
    {
        try
        {
            var userId = GetAuthenticatedUserId();
            var myTickets = _context.Tickets.Where(t => t.CreatedBy == userId).ToList();
            return Ok(myTickets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving my tickets");
            return Problem("Unable to retrieve my tickets. Please try again later.");
        }
    }

    // SCRUM-17 AC1/AC2/AC3: an agent or admin moves a ticket through its
    // lifecycle. Route is "api/ticket/{id}/status" (singular, matching
    // this controller's existing routes) rather than the story's literal
    // "api/tickets/..." - same kind of minor wording-vs-codebase-convention
    // gap already noted for ASSIGN-2/ASSIGN-4's numbering.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTicketStatusDTO request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { message = "A valid status is required." });
        }

        if (!TicketStatusService.SettableStatuses.Contains(request.NewStatus))
        {
            return BadRequest(new { message = "Status must be In Progress, Resolved, or Closed." });
        }

        try
        {
            var (ticket, error) = await LoadTicketForStatusActionAsync(id);
            if (error is not null)
            {
                return error;
            }

            var updated = await _statusService.ApplyStatusChangeAsync(
                ticket!, request.NewStatus, GetAuthenticatedUserId(), HttpContext.RequestAborted);

            return Ok(new TicketStatusResponseDTO
            {
                TicketId = updated.Id,
                Status = updated.Status,
                UpdatedAt = updated.UpdatedAt!.Value,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for ticket {TicketId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Something went wrong on our end. Please try again in a moment." });
        }
    }

    // AC3, made demonstrable: not asked for explicitly, but "history is
    // retained" is only useful if something can read it back. Same access
    // rule as the update itself - an agent sees only their own tickets'
    // history, an admin sees any ticket's.
    [Authorize(Roles = $"{Roles.Agent},{Roles.Administrator}")]
    [HttpGet("{id}/status-history")]
    public async Task<IActionResult> GetStatusHistory(int id)
    {
        try
        {
            var (ticket, error) = await LoadTicketForStatusActionAsync(id);
            if (error is not null)
            {
                return error;
            }

            var history = await _context.StatusHistory
                .Where(h => h.TicketId == ticket!.Id)
                .OrderBy(h => h.ChangedAtUtc)
                .Select(h => new TicketStatusHistoryItemDTO
                {
                    OldStatus = h.OldStatus,
                    NewStatus = h.NewStatus,
                    ChangedBy = h.ChangedBy,
                    ChangedAtUtc = h.ChangedAtUtc,
                })
                .ToListAsync();

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving status history for ticket {TicketId}", id);
            return Problem("Unable to load status history. Please try again later.");
        }
    }

    // AC2: an agent may only act on tickets currently assigned to them -
    // checked against Ticket.Api's own AssignedTo column, which
    // TicketAssignedConsumer keeps in sync with Assignment.Api (see
    // manual-ticket-reassignment.md) - no cross-service call needed here.
    // An administrator bypasses this check entirely.
    private async Task<(Tickets? Ticket, IActionResult? Error)> LoadTicketForStatusActionAsync(int id)
    {
        var ticket = await _context.Tickets.FindAsync(id);
        if (ticket is null)
        {
            return (null, NotFound(new { message = "Ticket not found." }));
        }

        if (!User.IsInRole(Roles.Administrator) && ticket.AssignedTo != GetAuthenticatedUserId())
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden,
                new { message = "You can only act on tickets assigned to you." }));
        }

        return (ticket, null);
    }
}

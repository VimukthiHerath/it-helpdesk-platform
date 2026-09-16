using Confluent.Kafka;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Ticket.Api.Authorization;
using Ticket.Api.Data;
using Ticket.Api.Model;

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

    public TicketController(
        ApplicationDbContext context,
        ILogger<TicketController> logger,
        IProducer<string, string> kafkaProducer,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
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
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
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
            return Problem("Unable to create ticket. Please try again later.");
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
}

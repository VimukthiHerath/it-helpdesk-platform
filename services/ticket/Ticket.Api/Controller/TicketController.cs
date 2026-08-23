using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Ticket.Api.Data;
using Ticket.Api.Model;

namespace Ticket.Api.Controller;

[ApiController]
[Route("api/[controller]")]
public class TicketController : ControllerBase
{

    private readonly ApplicationDbContext _context;
    private readonly ILogger<TicketController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IConfiguration _configuration;
    
    public TicketController(
        ApplicationDbContext context,
        ILogger<TicketController> logger,
        IHttpClientFactory httpClientFactory,
        IProducer<string, string> kafkaProducer,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _kafkaProducer = kafkaProducer;
        _configuration = configuration;
    }

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
            var authorizationHeader = Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return Unauthorized(new { message = "Bearer token is required." });
            }

            var authClient = _httpClientFactory.CreateClient("AuthApi");
            using var authRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            authRequest.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

            using var authResponse = await authClient.SendAsync(authRequest);
            if (!authResponse.IsSuccessStatusCode)
            {
                if ((int)authResponse.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    return Unauthorized(new { message = "Invalid or expired bearer token." });
                }

                _logger.LogWarning("Auth API rejected user validation with status code {StatusCode}", authResponse.StatusCode);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Unable to validate the bearer token." });
            }

            var authUser = await authResponse.Content.ReadFromJsonAsync<AuthUserResponse>();
            if (authUser?.UserId == null || !int.TryParse(authUser.UserId, out var userId))
            {
                _logger.LogWarning("Auth API returned an invalid user ID");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { message = "Unable to identify the authenticated user." });
            }

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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error communicating with Auth API while creating ticket");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Unable to validate the bearer token." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ticket");
            return Problem("Unable to create ticket. Please try again later.");
        }
    }

    private sealed class AuthUserResponse
    {
        public string? UserId { get; set; }
    }
}
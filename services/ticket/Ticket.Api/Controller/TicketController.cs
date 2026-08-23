using Microsoft.AspNetCore.Mvc;
using Ticket.Api.Data;
using Ticket.Api.Model;

namespace Ticket.Api.Controller;

[ApiController]
[Route("api/[controller]")]
public class TicketController : ControllerBase
{

    private readonly ApplicationDbContext _context;
    private readonly ILogger<TicketController> _logger;
    
    public TicketController(ApplicationDbContext context, ILogger<TicketController> logger)
    {
        _context = context;
        _logger = logger;
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
    public IActionResult CreateTicket([FromBody] TicketCreateDTO ticketDto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var ticket = new Tickets
            {
                Description = ticketDto.Description,
                IssueType = ticketDto.IssueType,
                Urgency = ticketDto.Urgency,
            };

            _context.Tickets.Add(ticket);
            _context.SaveChanges();
            return StatusCode(StatusCodes.Status201Created, new { message = "Ticket created successfully", ticketId = ticket.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ticket");
            return Problem("Unable to create ticket. Please try again later.");
        }
    }
}
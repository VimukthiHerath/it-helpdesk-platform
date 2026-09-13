using System.Text.Json;
using Assignment.Api.Data;
using Assignment.Api.DTO;
using Assignment.Api.Model;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace Assignment.Api.Services;

// The actual round-robin logic (AC1/AC2/AC3), kept separate from the Kafka
// consumer so it can be unit-tested without a broker or a real MySQL instance.
//
// Rotation state is not stored separately - it is derived from the most
// recent row in Assignments, which is who got the previous ticket. This
// assumes a single consumer instance (matches ASSIGN-1's scope); running
// more than one Assignment.Api replica at once could race on who's "next".
public class RoundRobinAssignmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IProducer<string, string> _producer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RoundRobinAssignmentService> _logger;

    public RoundRobinAssignmentService(
        ApplicationDbContext context,
        IProducer<string, string> producer,
        IConfiguration configuration,
        ILogger<RoundRobinAssignmentService> logger)
    {
        _context = context;
        _producer = producer;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task AssignAsync(TicketCreatedEvent ticketEvent, CancellationToken cancellationToken)
    {
        var agents = await _context.Agents
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (agents.Count == 0)
        {
            _logger.LogWarning(
                "No agents configured for round-robin assignment. Ticket {TicketId} was not assigned.",
                ticketEvent.TicketId);
            return;
        }

        var lastAssignment = await _context.Assignments
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var nextAgent = PickNextAgent(agents, lastAssignment);

        var assignment = new TicketAssignment
        {
            TicketId = ticketEvent.TicketId,
            AgentId = nextAgent.Id,
            AssignedAtUtc = DateTime.UtcNow,
        };

        _context.Assignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        await PublishTicketAssignedAsync(ticketEvent.TicketId, nextAgent.UserId, assignment.AssignedAtUtc, cancellationToken);

        _logger.LogInformation(
            "Assigned ticket {TicketId} to agent user {AgentUserId} (rotation slot {DisplayOrder})",
            ticketEvent.TicketId,
            nextAgent.UserId,
            nextAgent.DisplayOrder);
    }

    // AC1 (different agents in a fixed order) + AC2 (wraps after the last one).
    private static Agent PickNextAgent(List<Agent> agents, TicketAssignment? lastAssignment)
    {
        if (lastAssignment is null)
        {
            return agents[0];
        }

        var lastIndex = agents.FindIndex(a => a.Id == lastAssignment.AgentId);
        if (lastIndex == -1)
        {
            // The previously-assigned agent isn't in the current rotation
            // (e.g. removed from the list) - restart from the beginning.
            return agents[0];
        }

        var nextIndex = (lastIndex + 1) % agents.Count;
        return agents[nextIndex];
    }

    private async Task PublishTicketAssignedAsync(int ticketId, int agentUserId, DateTime assignedAtUtc, CancellationToken cancellationToken)
    {
        var ticketAssignedEvent = new TicketAssignedEvent
        {
            TicketId = ticketId,
            AgentUserId = agentUserId,
            AssignedAtUtc = assignedAtUtc,
        };

        var topic = _configuration["Kafka:TicketAssignedTopic"] ?? "ticket-assigned";
        var payload = JsonSerializer.Serialize(ticketAssignedEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = ticketId.ToString(),
            Value = payload,
        }, cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Ticket.Api.Model;

namespace Ticket.Api.Data;

public class TicketDbContext : DbContext
{
    public TicketDbContext(DbContextOptions<TicketDbContext> options) : base(options)
    {
    }

    // Fully qualify the type to resolve ambiguity with Microsoft.Net.Http.Headers.Ticket
    public DbSet<Ticket.Api.Model.Ticket> Tickets { get; set; }
}

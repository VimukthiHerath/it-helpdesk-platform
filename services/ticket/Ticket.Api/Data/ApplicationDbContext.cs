using Microsoft.EntityFrameworkCore;
using Ticket.Api.Model;

namespace Ticket.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Tickets> Tickets { get; set; }
}
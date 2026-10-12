using Microsoft.EntityFrameworkCore;
using Sla.Api.Models;

namespace Sla.Api.Data;

public class SlaDbContext : DbContext
{
    public SlaDbContext(DbContextOptions<SlaDbContext> options) : base(options)
    {
    }

    public DbSet<TicketSla> TicketSlas { get; set; }
    public DbSet<ProcessedEvent> ProcessedEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<TicketSla>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TicketId).IsUnique(); // One active SLA timer per ticket
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EventKey).IsUnique();
        });
    }
}

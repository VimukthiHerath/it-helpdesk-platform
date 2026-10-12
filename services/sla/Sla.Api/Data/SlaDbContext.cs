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
    public DbSet<SlaTierPolicy> SlaTierPolicies { get; set; }

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

        modelBuilder.Entity<SlaTierPolicy>(entity =>
        {
            entity.HasKey(e => e.TierName);
            entity.HasData(
                new SlaTierPolicy { TierName = "LOW", DurationMinutes = 2880, LastUpdatedAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                new SlaTierPolicy { TierName = "MEDIUM", DurationMinutes = 1440, LastUpdatedAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                new SlaTierPolicy { TierName = "HIGH", DurationMinutes = 240, LastUpdatedAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                new SlaTierPolicy { TierName = "CRITICAL", DurationMinutes = 60, LastUpdatedAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) });
        });
    }
}

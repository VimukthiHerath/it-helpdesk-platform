using Microsoft.EntityFrameworkCore;
using Sla.Api.Model;

namespace Sla.Api.Data;

public class SlaDbContext : DbContext
{
    public SlaDbContext(DbContextOptions<SlaDbContext> options) : base(options)
    {
    }

    public DbSet<SlaTierPolicy> SlaTierPolicies { get; set; }
    public DbSet<TicketSla> TicketSlas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SlaTierPolicy>().HasData(
            new SlaTierPolicy { TierName = "LOW", DurationMinutes = 2880, LastUpdatedAtUtc = DateTime.UtcNow },
            new SlaTierPolicy { TierName = "MEDIUM", DurationMinutes = 1440, LastUpdatedAtUtc = DateTime.UtcNow },
            new SlaTierPolicy { TierName = "HIGH", DurationMinutes = 240, LastUpdatedAtUtc = DateTime.UtcNow },
            new SlaTierPolicy { TierName = "CRITICAL", DurationMinutes = 60, LastUpdatedAtUtc = DateTime.UtcNow }
        );
    }
}

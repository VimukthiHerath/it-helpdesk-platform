using Microsoft.EntityFrameworkCore;
using Assignment.Api.Model;

namespace Assignment.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<TicketAssignment> Assignments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>().HasIndex(a => a.DisplayOrder).IsUnique();

        // Seed rotation list: no Auth integration yet (out of scope, see ASSIGN-1),
        // so this is a fixed list of already-existing local Agent-role users.
        // Update per environment until agent management is wired up properly.
        modelBuilder.Entity<Agent>().HasData(
            new Agent { Id = 1, UserId = 3, DisplayOrder = 0 },
            new Agent { Id = 2, UserId = 26, DisplayOrder = 1 }
        );
    }
}

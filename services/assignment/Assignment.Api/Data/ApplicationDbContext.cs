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
    }
}

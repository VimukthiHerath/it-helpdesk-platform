using Microsoft.EntityFrameworkCore;
using Notification.Api.Models;

namespace Notification.Api.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options) { }

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Unique index on EventKey ensures we can never store the same event twice
            entity.HasIndex(e => e.EventKey).IsUnique();
            entity.Property(e => e.EventKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Recipient).HasMaxLength(255).IsRequired();
        });
    }
}

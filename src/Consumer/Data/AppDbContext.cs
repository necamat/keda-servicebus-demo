
using Consumer.Models;
using Microsoft.EntityFrameworkCore;

namespace Consumer.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Data).IsRequired();
        });
    }
}
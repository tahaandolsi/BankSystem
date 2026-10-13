using Microsoft.EntityFrameworkCore;
using BankSystem.Shared.Infrastructure.Extensions;
using MassTransit;

using NotificationEntity =
    BankSystem.Notification.Domain.Entities.Notification;

namespace BankSystem.Notification.Infrastructure.Data;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(
        DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<NotificationEntity> Notifications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddTransactionalOutboxEntities();
    }
}

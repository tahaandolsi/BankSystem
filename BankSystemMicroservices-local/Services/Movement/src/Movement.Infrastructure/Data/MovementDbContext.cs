using Microsoft.EntityFrameworkCore;
using BankSystem.Shared.Infrastructure.Extensions;
using MassTransit;

using MovementEntity =
    BankSystem.Movement.Domain.Entities.Movement;

namespace BankSystem.Movement.Infrastructure.Data;

public class MovementDbContext : DbContext
{
    public MovementDbContext(
        DbContextOptions<MovementDbContext> options)
        : base(options)
    {
    }

    public DbSet<MovementEntity> Movements { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MovementEntity>().HasIndex(x => x.TransactionId).IsUnique();
        modelBuilder.AddTransactionalOutboxEntities();
    }
}

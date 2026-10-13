using Microsoft.EntityFrameworkCore;
using BankSystem.Shared.Infrastructure.Extensions;
using MassTransit;

using TransactionEntity =
    BankSystem.Transaction.Domain.Entities.Transaction;

namespace BankSystem.Transaction.Infrastructure.Data;

public class TransactionDbContext(
    DbContextOptions<TransactionDbContext> options)
    : DbContext(options)
{
    public DbSet<TransactionEntity> Transactions =>
        Set<TransactionEntity>();

    public DbSet<PaymentFinancialResultOutboxMessage> PaymentFinancialResultOutboxMessages =>
        Set<PaymentFinancialResultOutboxMessage>();

    public DbSet<PaymentTransactionAcceptedOutboxMessage> PaymentTransactionAcceptedOutboxMessages =>
        Set<PaymentTransactionAcceptedOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var entity = builder.Entity<TransactionEntity>();

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Amount)
            .HasPrecision(18, 2);

        entity.HasIndex(x => x.IdempotencyKey)
            .IsUnique();

        entity.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter("\"PaymentId\" IS NOT NULL");

        entity.HasIndex(x => x.AuthorizationId)
            .IsUnique()
            .HasFilter("\"AuthorizationId\" IS NOT NULL");

        entity.Property(x => x.Currency)
            .HasMaxLength(3);

        entity.Property(x => x.Description)
            .HasMaxLength(500);

        entity.Property(x => x.Reference)
            .HasMaxLength(100);

        var paymentResultOutbox = builder.Entity<PaymentFinancialResultOutboxMessage>();
        paymentResultOutbox.ToTable("PaymentFinancialResultOutboxMessages");
        paymentResultOutbox.HasKey(x => x.Id);
        paymentResultOutbox.HasIndex(x => x.TransactionId).IsUnique();
        paymentResultOutbox.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LockUntilUtc });
        paymentResultOutbox.Property(x => x.EventType).HasMaxLength(16).IsRequired();
        paymentResultOutbox.Property(x => x.RoutingKey).HasMaxLength(100).IsRequired();
        paymentResultOutbox.Property(x => x.PayloadJson).IsRequired();
        paymentResultOutbox.Property(x => x.Status).HasMaxLength(20).IsRequired();
        paymentResultOutbox.Property(x => x.LastError).HasMaxLength(1000);

        var paymentAcceptedOutbox = builder.Entity<PaymentTransactionAcceptedOutboxMessage>();
        paymentAcceptedOutbox.ToTable("PaymentTransactionAcceptedOutboxMessages");
        paymentAcceptedOutbox.HasKey(x => x.Id);
        paymentAcceptedOutbox.HasIndex(x => x.PaymentId).IsUnique();
        paymentAcceptedOutbox.HasIndex(x => x.TransactionId).IsUnique();
        paymentAcceptedOutbox.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.LockUntilUtc });
        paymentAcceptedOutbox.Property(x => x.PayloadJson).IsRequired();
        paymentAcceptedOutbox.Property(x => x.Status).HasMaxLength(20).IsRequired();
        paymentAcceptedOutbox.Property(x => x.LastError).HasMaxLength(1000);

        builder.AddTransactionalOutboxEntities();
    }
}

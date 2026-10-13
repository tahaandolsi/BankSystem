using NotificationEntity = BankSystem.Notification.Domain.Entities.Notification;
using BankSystem.Notification.Domain.Enums;
using BankSystem.Notification.Infrastructure.Data;
using BankSystem.Notification.Application.Interfaces;
using BankSystem.Shared.Domain.Events.Account;
using BankSystem.Shared.Domain.ValueObjects;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BankSystem.Notification.Infrastructure.Consumers;

public sealed class AccountTransactionAppliedConsumer(NotificationDbContext db, INotificationSender sender)
    : IConsumer<AccountTransactionAppliedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AccountTransactionAppliedIntegrationEvent> context)
    {
        var message = context.Message;
        if (!IsValidMessage(message))
            throw new InvalidOperationException("Account sent an invalid applied transaction event.");

        // A retry can reuse the scoped DbContext after a failed attempt.
        var trackedNotification = db.ChangeTracker
            .Entries<NotificationEntity>()
            .FirstOrDefault(entry => entry.Entity.Id == message.TransactionId);
        if (trackedNotification is not null)
            trackedNotification.State = EntityState.Detached;

        var existingNotification = await db.Notifications.SingleOrDefaultAsync(
            notification => notification.Id == message.TransactionId,
            context.CancellationToken);
        if (existingNotification is not null)
        {
            if (existingNotification.UserId != message.CustomerId.ToString()
                || existingNotification.Type != NotificationType.InApp)
            {
                throw new InvalidOperationException(
                    "The transaction notification identifier is already used by another notification.");
            }

            if (existingNotification.Status is not (NotificationStatus.Sent or NotificationStatus.Delivered))
            {
                throw new InvalidOperationException(
                    "The existing transaction notification is not in a completed delivery state.");
            }

            return;
        }

        var marker = $"Transaction {message.TransactionId}";
        var subject = $"{message.Type} processed";
        var body = $"{marker} was applied to account {message.AccountId}. Balance: {message.BalanceAfter:F2} {message.Currency}.";
        var notification = new NotificationEntity
        {
            // The primary key makes TransactionId a durable idempotency key without a schema change.
            Id = message.TransactionId,
            UserId = message.CustomerId.ToString(),
            Type = NotificationType.InApp,
            Priority = NotificationPriority.Normal,
            Subject = subject,
            Body = body,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.Notifications.Add(notification);

        // Save the notification before sending so concurrent deliveries contend on its primary key.
        await db.SaveChangesAsync(context.CancellationToken);
        await sender.SendAsync(notification.Type, subject, body, context.CancellationToken);

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(context.CancellationToken);
    }

    private static bool IsValidMessage(AccountTransactionAppliedIntegrationEvent message)
    {
        if (message.TransactionId == Guid.Empty
            || message.AccountId == Guid.Empty
            || message.CustomerId == Guid.Empty
            || message.Amount <= 0
            || decimal.Round(message.Amount, Currency.DecimalPlaces) != message.Amount
            || !Currency.IsValidCurrencyCode(message.Currency)
            || message.AppliedAt == default)
        {
            return false;
        }

        if (message.Type is not ("Deposit" or "Withdrawal" or "Transfer"))
            return false;

        return message.Type != "Transfer"
            || (message.ToAccountId is Guid destinationId
                && destinationId != Guid.Empty
                && destinationId != message.AccountId);
    }
}

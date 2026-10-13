using BankSystem.Notification.Application.Interfaces;
using BankSystem.Notification.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace BankSystem.Notification.Infrastructure.Providers;

public sealed class LoggingNotificationSender(
    ILogger<LoggingNotificationSender> log)
    : INotificationSender
{
    public Task SendAsync(
        NotificationType type,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        log.LogInformation(
            "Notification sent through {Type}: {Subject}",
            type,
            subject);

        return Task.CompletedTask;
    }
}
using BankSystem.Notification.Application.Commands;
using BankSystem.Notification.Application.DTOs;
using BankSystem.Notification.Application.Interfaces;
using BankSystem.Notification.Domain.Enums;
using MediatR;

using NotificationEntity =
    BankSystem.Notification.Domain.Entities.Notification;

namespace BankSystem.Notification.Application.Handlers;

public sealed class SendNotificationHandler(
    INotificationRepository repository,
    INotificationSender sender)
    : IRequestHandler<SendNotificationCommand, NotificationDto>
{
    public async Task<NotificationDto> Handle(
        SendNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var notification = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = request.Type,
            Priority = request.Priority,
            Subject = request.Subject,
            Body = request.Body,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(
            notification,
            cancellationToken);

        // Keep the pending notification if sending fails or the service stops mid-send.
        await repository.SaveAsync(cancellationToken);

        await sender.SendAsync(
            notification.Type,
            notification.Subject,
            notification.Body,
            cancellationToken);

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTime.UtcNow;

        await repository.SaveAsync(cancellationToken);

        return new NotificationDto(
            notification.Id,
            notification.UserId,
            notification.Type.ToString(),
            notification.Status.ToString(),
            notification.Subject,
            notification.Body,
            notification.CreatedAt,
            notification.SentAt,
            notification.Error);
    }
}

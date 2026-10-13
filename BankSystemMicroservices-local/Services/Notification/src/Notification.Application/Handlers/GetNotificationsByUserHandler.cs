using BankSystem.Notification.Application.DTOs;
using BankSystem.Notification.Application.Interfaces;
using BankSystem.Notification.Application.Queries;
using MediatR;

namespace BankSystem.Notification.Application.Handlers;

public sealed class GetNotificationsByUserHandler(INotificationRepository repository)
    : IRequestHandler<GetNotificationsByUserQuery, IReadOnlyList<NotificationDto>>
{
    public async Task<IReadOnlyList<NotificationDto>> Handle(
        GetNotificationsByUserQuery request,
        CancellationToken cancellationToken)
    {
        var notifications = await repository.GetByUserIdAsync(
            request.UserId,
            Math.Max(1, request.Page),
            Math.Clamp(request.PageSize, 1, 100),
            cancellationToken);

        return notifications.Select(notification => new NotificationDto(
            notification.Id,
            notification.UserId,
            notification.Type.ToString(),
            notification.Status.ToString(),
            notification.Subject,
            notification.Body,
            notification.CreatedAt,
            notification.SentAt,
            notification.Error)).ToList();
    }
}

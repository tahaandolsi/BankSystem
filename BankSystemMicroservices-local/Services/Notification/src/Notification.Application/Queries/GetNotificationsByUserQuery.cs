using BankSystem.Notification.Application.DTOs;
using MediatR;

namespace BankSystem.Notification.Application.Queries;

public record GetNotificationsByUserQuery(string UserId, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyList<NotificationDto>>;

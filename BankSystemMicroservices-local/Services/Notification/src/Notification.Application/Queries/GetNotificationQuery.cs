using BankSystem.Notification.Application.DTOs;using MediatR;namespace BankSystem.Notification.Application.Queries;public record GetNotificationQuery(Guid Id):IRequest<NotificationDto?>;

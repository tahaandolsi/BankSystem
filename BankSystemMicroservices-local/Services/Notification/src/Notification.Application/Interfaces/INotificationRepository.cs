using NotificationEntity = BankSystem.Notification.Domain.Entities.Notification;

namespace BankSystem.Notification.Application.Interfaces;

public interface INotificationRepository
{
    Task AddAsync(
        NotificationEntity notification,
        CancellationToken cancellationToken);

    Task<NotificationEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationEntity>> GetByUserIdAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task SaveAsync(
        CancellationToken cancellationToken);
}

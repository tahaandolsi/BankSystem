using BankSystem.Notification.Application.Interfaces;
using BankSystem.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using NotificationEntity =
    BankSystem.Notification.Domain.Entities.Notification;

namespace BankSystem.Notification.Infrastructure.Repositories;

public sealed class NotificationRepository(
    NotificationDbContext db) : INotificationRepository
{
    public Task AddAsync(
        NotificationEntity notification,
        CancellationToken cancellationToken)
    {
        return db.Notifications
            .AddAsync(notification, cancellationToken)
            .AsTask();
    }

    public Task<NotificationEntity?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationEntity>> GetByUserIdAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await db.Notifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task SaveAsync(
        CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}

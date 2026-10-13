using BankSystem.Notification.Domain.Enums;
namespace BankSystem.Notification.Domain.Entities; 
public class Notification{public Guid Id{get;set;}public string UserId{get;set;}=string.Empty;
    public NotificationType Type{get;set;}public string Subject{get;set;}=string.Empty;
    public string Body{get;set;}=string.Empty;
    public NotificationPriority Priority{get;set;}=NotificationPriority.Normal;
    public NotificationStatus Status{get;set;}=NotificationStatus.Pending;
    public DateTime CreatedAt{get;set;}=DateTime.UtcNow;public DateTime? SentAt{get;set;}public string? Error{get;set;}}

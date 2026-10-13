using BankSystem.Notification.Domain.Enums; namespace BankSystem.Notification.Application.Interfaces; 
public interface INotificationSender{Task SendAsync(NotificationType type,string subject,string body,CancellationToken ct);}

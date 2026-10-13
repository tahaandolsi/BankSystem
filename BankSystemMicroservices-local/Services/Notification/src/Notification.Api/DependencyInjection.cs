using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankSystem.Notification.Infrastructure.Data;
using BankSystem.Shared.WebApiDefaults.Extensions;
namespace BankSystem.Notification.Api;
public static class DependencyInjection {
  public static IServiceCollection AddWebApiServices(this IServiceCollection services, IConfiguration configuration) {
    services.AddWebApiDefaults(configuration, "Notification API");
    services.AddDbContextHealthCheck<NotificationDbContext>();
    return services;
  }
}

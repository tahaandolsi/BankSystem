using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankSystem.Reporting.Infrastructure.Data;
using BankSystem.Shared.WebApiDefaults.Extensions;
namespace BankSystem.Reporting.Api;
public static class DependencyInjection {
  public static IServiceCollection AddWebApiServices(this IServiceCollection services, IConfiguration configuration) {
    services.AddWebApiDefaults(configuration, "Reporting API");
    services.AddDbContextHealthCheck<ReportingDbContext>();
    return services;
  }
}

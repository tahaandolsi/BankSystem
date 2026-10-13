using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankSystem.Movement.Infrastructure.Data;
using BankSystem.Shared.WebApiDefaults.Extensions;
using BankSystem.Shared.WebApiDefaults.Services;
namespace BankSystem.Movement.Api;
public static class DependencyInjection {
  public static IServiceCollection AddWebApiServices(this IServiceCollection services, IConfiguration configuration) {
    services.AddWebApiDefaults(configuration, "Movement API");
    services.AddDbContextHealthCheck<MovementDbContext>();
    services.AddHttpClient(AccountOwnershipVerifier.ClientName, client => {
      client.BaseAddress = new Uri(configuration["AccountApi:BaseUrl"] ?? "https://localhost:7119/");
      client.Timeout = TimeSpan.FromSeconds(10);
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    services.AddScoped<AccountOwnershipVerifier>();
    return services;
  }
}

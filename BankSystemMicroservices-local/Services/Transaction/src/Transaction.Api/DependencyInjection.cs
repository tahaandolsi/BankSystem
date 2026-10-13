using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BankSystem.Transaction.Infrastructure.Data;
using BankSystem.Shared.WebApiDefaults.Extensions;
using BankSystem.Shared.WebApiDefaults.Services;
namespace BankSystem.Transaction.Api;
public static class DependencyInjection {
  public static IServiceCollection AddWebApiServices(this IServiceCollection services, IConfiguration configuration) {
    services.AddWebApiDefaults(configuration, "Transaction API");
    services.AddDbContextHealthCheck<TransactionDbContext>();
    services.AddHttpClient(AccountOwnershipVerifier.ClientName, client => {
      client.BaseAddress = new Uri(configuration["AccountApi:BaseUrl"] ?? "https://localhost:7119/");
      client.Timeout = TimeSpan.FromSeconds(10);
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    services.AddScoped<AccountOwnershipVerifier>();
    return services;
  }
}

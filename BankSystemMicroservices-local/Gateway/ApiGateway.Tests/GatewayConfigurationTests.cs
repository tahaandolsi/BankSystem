using System.Security.Claims;
using System.Text.Json;
using BankSystem.ApiGateway.Configuration;
using BankSystem.ApiGateway.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BankSystem.ApiGateway.Tests;

public class GatewayConfigurationTests
{
    public static IEnumerable<object[]> GatewayModes =>
    [
        ["appsettings.Development.json", "http://localhost:8081/", "https://localhost:5202/"],
        ["appsettings.Docker.json", "http://host.docker.internal:8081/", "http://transaction:8080/"],
    ];

    [Theory]
    [MemberData(nameof(GatewayModes))]
    public void PaymentAndTransactionRoutesAreDistinctAndPointToExpectedServices(
        string settingsFile,
        string paymentAddress,
        string transactionAddress)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, settingsFile)));
        var proxy = document.RootElement.GetProperty("ReverseProxy");
        var routes = proxy.GetProperty("Routes");
        var paymentRoute = routes.GetProperty("payment-api-route");
        var transactionRoute = routes.GetProperty("transaction-api-route");

        Assert.Equal("/api/v1/payments/{**catch-all}",
            paymentRoute.GetProperty("Match").GetProperty("Path").GetString());
        Assert.Equal("payment-cluster", paymentRoute.GetProperty("ClusterId").GetString());
        Assert.Equal("AuthenticatedUsers", paymentRoute.GetProperty("AuthorizationPolicy").GetString());
        Assert.Equal("/api/v1/transactions/{**catch-all}",
            paymentRoute.GetProperty("Transforms")[0].GetProperty("PathPattern").GetString());

        Assert.Equal("/api/v1/transactions/{**catch-all}",
            transactionRoute.GetProperty("Match").GetProperty("Path").GetString());
        Assert.Equal("transaction-cluster", transactionRoute.GetProperty("ClusterId").GetString());

        var clusters = proxy.GetProperty("Clusters");
        Assert.Equal(paymentAddress, clusters.GetProperty("payment-cluster")
            .GetProperty("Destinations").GetProperty("payment-api").GetProperty("Address").GetString());
        Assert.Equal(transactionAddress, clusters.GetProperty("transaction-cluster")
            .GetProperty("Destinations").GetProperty("transaction-api").GetProperty("Address").GetString());
    }

    [Theory]
    [MemberData(nameof(GatewayModes))]
    public async Task PaymentRouteAuthorizationPolicyRejectsAnonymousPrincipal(
        string settingsFile,
        string paymentAddress,
        string transactionAddress)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, settingsFile)));
        var proxy = document.RootElement.GetProperty("ReverseProxy");
        var policyName = proxy.GetProperty("Routes")
            .GetProperty("payment-api-route").GetProperty("AuthorizationPolicy").GetString();
        var clusters = proxy.GetProperty("Clusters");
        Assert.Equal(paymentAddress, clusters.GetProperty("payment-cluster")
            .GetProperty("Destinations").GetProperty("payment-api").GetProperty("Address").GetString());
        Assert.Equal(transactionAddress, clusters.GetProperty("transaction-cluster")
            .GetProperty("Destinations").GetProperty("transaction-api").GetProperty("Address").GetString());

        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureAuthorizationPolicies();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            policyName!);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void GatewayJwtValidationIsRestrictedToSecurityServicesHs256Contract()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Key"] = "synthetic-test-only-key-value-not-a-production-secret",
            ["Authentication:Jwt:Issuer"] = "synthetic-test-issuer",
            ["Authentication:Jwt:Audience"] = "synthetic-test-audience",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        ServiceExtensions.AddAuthentication(services, configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal([SecurityAlgorithms.HmacSha256], options.TokenValidationParameters.ValidAlgorithms);
    }
}
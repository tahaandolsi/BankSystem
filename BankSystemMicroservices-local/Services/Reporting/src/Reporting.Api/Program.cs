using System.Diagnostics.CodeAnalysis;
using BankSystem.Reporting.Application;
using BankSystem.Reporting.Infrastructure;
using BankSystem.ServiceDefaults;
using BankSystem.Shared.WebApiDefaults.Extensions;
using BankSystem.Reporting.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWebApiServices(builder.Configuration);
var app = builder.Build();
app.MapDefaultEndpoints();
app.UseWebApiDefaults("Reporting API");
await app.RunAsync();
[ExcludeFromCodeCoverage] public static partial class Program { }

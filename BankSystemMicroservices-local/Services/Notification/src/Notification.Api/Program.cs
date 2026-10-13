using System.Diagnostics.CodeAnalysis;
using BankSystem.Notification.Api;
using BankSystem.Notification.Application;
using BankSystem.Notification.Infrastructure;
using BankSystem.ServiceDefaults;
using BankSystem.Shared.WebApiDefaults.Extensions;
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWebApiServices(builder.Configuration);
var app = builder.Build();
app.MapDefaultEndpoints();
app.UseWebApiDefaults("Notification API");
await app.RunAsync();
[ExcludeFromCodeCoverage] public static partial class Program { }

using BankSystem.Movement.Api;
using BankSystem.Movement.Application;
using BankSystem.Movement.Infrastructure;
using BankSystem.ServiceDefaults;
using BankSystem.Shared.WebApiDefaults.Extensions;
using System.Diagnostics.CodeAnalysis;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddWebApiServices(builder.Configuration);


var app = builder.Build();

app.MapDefaultEndpoints();
app.UseWebApiDefaults("Movement API");
await app.RunAsync();
[ExcludeFromCodeCoverage] public static partial class Program { }

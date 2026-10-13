var builder = DistributedApplication.CreateBuilder(args);

// Keep stable local ports by updating the endpoints already created from each
// project's launchSettings.json. Using WithEndpoint avoids the duplicate-name
// exception that occurs when WithHttpEndpoint/WithHttpsEndpoint is called again.
var security = builder.AddProject<Projects.Security_Api>("security")
    .WithEndpoint("https", endpoint => endpoint.Port = 7066)
    .WithEndpoint("http", endpoint => endpoint.Port = 5153);

var account = builder.AddProject<Projects.BankSystem_Account_Api>("account")
    .WithEndpoint("https", endpoint => endpoint.Port = 7119)
    .WithEndpoint("http", endpoint => endpoint.Port = 5069);

var transaction = builder.AddProject<Projects.BankSystem_Transaction_Api>("transaction")
    .WithEndpoint("https", endpoint => endpoint.Port = 5202)
    .WithEndpoint("http", endpoint => endpoint.Port = 5201);

var movement = builder.AddProject<Projects.BankSystem_Movement_Api>("movement")
    .WithEndpoint("https", endpoint => endpoint.Port = 5204)
    .WithEndpoint("http", endpoint => endpoint.Port = 5203);

var notification = builder.AddProject<Projects.BankSystem_Notification_Api>("notification")
    .WithEndpoint("https", endpoint => endpoint.Port = 5206)
    .WithEndpoint("http", endpoint => endpoint.Port = 5205);

var reporting = builder.AddProject<Projects.BankSystem_Reporting_Api>("reporting")
    .WithEndpoint("https", endpoint => endpoint.Port = 5208)
    .WithEndpoint("http", endpoint => endpoint.Port = 5207);

var gateway = builder.AddProject<Projects.BankSystem_ApiGateway>("api-gateway")
    .WithEndpoint("https", endpoint => endpoint.Port = 5210)
    .WithEndpoint("http", endpoint => endpoint.Port = 5209);

gateway
    .WaitFor(security)
    .WaitFor(account)
    .WaitFor(transaction)
    .WaitFor(movement)
    .WaitFor(notification)
    .WaitFor(reporting);

await builder.Build().RunAsync();

using Ehcp.Infrastructure;
using Ehcp.Logging;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();
builder.AddEhcpLogging("Ehcp.ApiFunctions");
builder.Services.AddEhcpInfrastructure(builder.Configuration);
using IHost host = builder.Build();
await host.Services.InitializeEhcpInfrastructureAsync(builder.Environment.IsDevelopment());
await host.RunAsync();

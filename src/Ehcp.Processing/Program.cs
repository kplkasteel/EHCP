using Ehcp.Application;
using Ehcp.Infrastructure;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();
builder.Services.AddEhcpInfrastructure(builder.Configuration);
builder.Services.AddEhcpProcessing();
builder.Services.AddEhcpProcessingAdapters(builder.Configuration);
using IHost host = builder.Build();
await host.Services.InitializeEhcpInfrastructureAsync(builder.Environment.IsDevelopment());
await host.RunAsync();

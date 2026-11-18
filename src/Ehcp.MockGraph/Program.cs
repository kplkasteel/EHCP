using Ehcp.Logging;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.AddEhcpLogging("Ehcp.MockGraph");
using IHost host = builder.Build();
await host.RunAsync();

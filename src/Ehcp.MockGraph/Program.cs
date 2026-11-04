using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

using IHost host = FunctionsApplication.CreateBuilder(args).Build();
host.Run();

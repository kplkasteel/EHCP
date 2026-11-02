using Ehcp.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ehcp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEhcpInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        CosmosSettings settings = configuration.GetSection("Cosmos").Get<CosmosSettings>() ?? new();
        if (!settings.IsValid())
            throw new InvalidOperationException(
                "Configure Cosmos endpoint, key, database and distinct mailbox containers. For local development, set Cosmos__Key to the key from the running emulator.");

        services.TryAddSingleton(settings);
        // CosmosClient is thread-safe and owns connection pools; share one per host.
        services.TryAddSingleton(provider =>
        {
            CosmosSettings cosmos = provider.GetRequiredService<CosmosSettings>();
            return new CosmosClient(cosmos.Endpoint, cosmos.Key, new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions
            });
        });
        services.TryAddScoped<IDocumentStore<EmailDocument>, CosmosDocumentStore<EmailDocument>>();
        services.TryAddScoped<IDocumentStore<CaseDocument>, CosmosDocumentStore<CaseDocument>>();
        return services;
    }

    public static async Task InitializeEhcpInfrastructureAsync(
        this IServiceProvider services, bool isDevelopment, CancellationToken cancellationToken = default)
    {
        CosmosSettings settings = services.GetRequiredService<CosmosSettings>();
        if (!settings.Initialize) return;
        if (!isDevelopment)
            throw new InvalidOperationException("Provision production containers through deployment tooling.");

        await CosmosInitializer.InitializeAsync(
            services.GetRequiredService<CosmosClient>(), settings, cancellationToken);
    }
}

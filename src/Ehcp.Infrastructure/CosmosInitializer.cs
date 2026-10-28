using Ehcp.Core;
using Microsoft.Azure.Cosmos;

namespace Ehcp.Infrastructure;

public static class CosmosInitializer
{
    public static async Task InitializeAsync(CosmosClient client, CosmosSettings settings, CancellationToken ct = default)
    {
        var database = (await client.CreateDatabaseIfNotExistsAsync(settings.Database, cancellationToken: ct)).Database;
        foreach (var mailbox in settings.Mailboxes.Values)
        {
            await EnsureAsync(database, mailbox.EmailsContainer, "/id", DocumentRules.EmailTtlSeconds, ct);
            await EnsureAsync(database, mailbox.CasesContainer, "/entityId", null, ct);
        }
    }
    private static async Task EnsureAsync(Database database, string name, string partitionKey, int? ttl, CancellationToken ct)
    {
        var response = await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(name, partitionKey) { DefaultTimeToLive = ttl }, cancellationToken: ct);
        var properties = (await response.Container.ReadContainerAsync(cancellationToken: ct)).Resource;
        if (properties.PartitionKeyPath != partitionKey || properties.DefaultTimeToLive != ttl)
            throw new InvalidOperationException($"Container '{name}' has an incompatible partition key or TTL. Migrate it explicitly.");
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ehcp.Core;
using Microsoft.Azure.Cosmos;

namespace Ehcp.Infrastructure;

public sealed class CosmosDocumentStore<T>(CosmosClient client, CosmosSettings settings) : IDocumentStore<T> where T : class
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private Container Container(string mailboxId)
    {
        // Only configured names are used. Request values never become container names.
        if (!settings.Mailboxes.TryGetValue(mailboxId, out MailboxSettings? mailbox))
            throw new DocumentStoreException(StoreFailure.NotFound);
        return client.GetContainer(settings.Database,
            typeof(T) == typeof(EmailDocument) ? mailbox.EmailsContainer : mailbox.CasesContainer);
    }
    private static string Id(T document) => document switch
    {
        EmailDocument email => email.Id,
        CaseDocument record => record.EntityId,
        _ => throw new InvalidOperationException("Unsupported document type.")
    };
    private static JsonObject Payload(T document)
    {
        JsonObject node = JsonSerializer.SerializeToNode(document, JsonOptions)!.AsObject();
        if (document is EmailDocument email) node["ttl"] = DocumentRules.EmailTtl(email);
        return node;
    }
    private static StoredDocument<T> Result(ItemResponse<JsonObject> response) =>
        new(response.Resource.Deserialize<T>(JsonOptions)!, response.ETag);

    public Task<StoredDocument<T>> ReadAsync(string mailboxId, string id, CancellationToken cancellationToken) =>
        Translate(async () => Result(await Container(mailboxId).ReadItemAsync<JsonObject>(id,
            new PartitionKey(id), cancellationToken: cancellationToken)));

    public Task<StoredDocument<T>> CreateAsync(string mailboxId, T document, CancellationToken cancellationToken) =>
        Translate(async () => Result(await Container(mailboxId).CreateItemAsync(Payload(document),
            new PartitionKey(Id(document)), cancellationToken: cancellationToken)));

    public Task<StoredDocument<T>> ReplaceAsync(string mailboxId, string id, T document, string etag, CancellationToken cancellationToken) =>
        Translate(async () => Result(await Container(mailboxId).ReplaceItemAsync(Payload(document), id,
            new PartitionKey(id), new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken)));

    public async Task DeleteAsync(string mailboxId, string id, string etag, CancellationToken cancellationToken) =>
        await Translate(async () => await Container(mailboxId).DeleteItemAsync<JsonObject>(id,
            new PartitionKey(id), new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken));

    private static async Task<TResult> Translate<TResult>(Func<Task<TResult>> action)
    {
        try { return await action(); }
        catch (CosmosException error)
        {
            throw new DocumentStoreException(error.StatusCode switch
            {
                HttpStatusCode.NotFound => StoreFailure.NotFound,
                HttpStatusCode.Conflict => StoreFailure.Conflict,
                HttpStatusCode.PreconditionFailed => StoreFailure.PreconditionFailed,
                _ => StoreFailure.Unavailable
            }, error.Message, error);
        }
    }
}

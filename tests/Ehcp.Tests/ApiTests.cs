using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ehcp.Tests;

public sealed class ApiTests
{
    [Fact] public async Task MissingTokenCanReadLocalData()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/mailboxes/ehcp-team-shared/emails/example");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact] public async Task CrudUsesLocationAndConcurrencyHeaders()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        const string url = "/api/mailboxes/ehcp-team-shared/emails";
        var created = await client.PostAsJsonAsync(url, Example, CosmosDocumentStore<EmailDocument>.JsonOptions);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var missingTag = await client.PutAsJsonAsync(url + "/example", Example, CosmosDocumentStore<EmailDocument>.JsonOptions);
        Assert.Equal((HttpStatusCode)428, missingTag.StatusCode);
        var update = new HttpRequestMessage(HttpMethod.Put, url + "/example")
        { Content = JsonContent.Create(Example with { ConversationId = "updated" }, options: CosmosDocumentStore<EmailDocument>.JsonOptions) };
        update.Headers.IfMatch.Add(read.Headers.ETag!);
        var replaced = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var stale = new HttpRequestMessage(HttpMethod.Delete, url + "/example");
        stale.Headers.IfMatch.Add(read.Headers.ETag!);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
        var delete = new HttpRequestMessage(HttpMethod.Delete, url + "/example");
        delete.Headers.IfMatch.Add(replaced.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url + "/example")).StatusCode);
    }
    [Fact] public async Task OpenApiIsVersionThreeZeroAndHasAllEightOperations()
    {
        await using var factory = new ApiFactory();
        using var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json"));
        Assert.False(json.RootElement.TryGetProperty("security", out var security) && security.GetArrayLength() > 0);
        if (json.RootElement.TryGetProperty("components", out var components))
            Assert.False(components.TryGetProperty("securitySchemes", out var schemes) && schemes.EnumerateObject().Any());
        Assert.StartsWith("3.0.", json.RootElement.GetProperty("openapi").GetString());
        Assert.Equal(8, json.RootElement.GetProperty("paths").EnumerateObject()
            .Sum(path => path.Value.EnumerateObject().Count(p => new[] { "get", "post", "put", "delete" }.Contains(p.Name))));
    }
    private static readonly EmailDocument Example = new("example", "thread", Direction.inbound, DateTimeOffset.UtcNow, []);
}

internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Cosmos:Key", "test-not-used");
        builder.UseSetting("Cosmos:Initialize", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDocumentStore<EmailDocument>>();
            services.AddSingleton<IDocumentStore<EmailDocument>, MemoryEmails>();
        });
    }
}

internal sealed class MemoryEmails : IDocumentStore<EmailDocument>
{
    private readonly Dictionary<(string, string), StoredDocument<EmailDocument>> values = new();
    public Task<StoredDocument<EmailDocument>> ReadAsync(string mailbox, string id, CancellationToken ct) =>
        Task.FromResult(values.TryGetValue((mailbox, id), out var value) ? value : throw new DocumentStoreException(StoreFailure.NotFound));
    public Task<StoredDocument<EmailDocument>> CreateAsync(string mailbox, EmailDocument document, CancellationToken ct)
    {
        var value = new StoredDocument<EmailDocument>(document, $"\"{Guid.NewGuid()}\"");
        if (!values.TryAdd((mailbox, document.Id), value)) throw new DocumentStoreException(StoreFailure.Conflict);
        return Task.FromResult(value);
    }
    public async Task<StoredDocument<EmailDocument>> ReplaceAsync(string mailbox, string id, EmailDocument document, string etag, CancellationToken ct)
    {
        var current = await ReadAsync(mailbox, id, ct);
        if (current.ETag != etag) throw new DocumentStoreException(StoreFailure.PreconditionFailed);
        return values[(mailbox, id)] = new(document, $"\"{Guid.NewGuid()}\"");
    }
    public async Task DeleteAsync(string mailbox, string id, string etag, CancellationToken ct)
    {
        if ((await ReadAsync(mailbox, id, ct)).ETag != etag) throw new DocumentStoreException(StoreFailure.PreconditionFailed);
        values.Remove((mailbox, id));
    }
}

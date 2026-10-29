using System.Text.Json.Nodes;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Ehcp.IntegrationTests;

public sealed class EmulatorFactAttribute : FactAttribute
{
    public EmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EHCP_TEST_COSMOS_KEY")))
            Skip = "Set EHCP_TEST_COSMOS_KEY and optionally EHCP_TEST_COSMOS_ENDPOINT to run against a trusted local emulator.";
    }
}
public sealed class CosmosTests
{
    [EmulatorFact]
    public async Task CosmosRoundTripIsolationTtlAndConcurrency()
    {
        var settings = new CosmosSettings
        {
            Endpoint = Environment.GetEnvironmentVariable("EHCP_TEST_COSMOS_ENDPOINT") ?? "https://localhost:8081",
            Key = Environment.GetEnvironmentVariable("EHCP_TEST_COSMOS_KEY")!,
            Database = "ehcp-test-" + Guid.NewGuid().ToString("N"),
            Mailboxes = new Dictionary<string, MailboxSettings>
            {
                ["one"] = new() { Role = "One", EmailsContainer = "one-emails", CasesContainer = "one-cases" },
                ["two"] = new() { Role = "Two", EmailsContainer = "two-emails", CasesContainer = "two-cases" }
            }
        };
        using var client = new CosmosClient(settings.Endpoint, settings.Key, new CosmosClientOptions
        { ConnectionMode = ConnectionMode.Gateway, UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions });
        try
        {
            await CosmosInitializer.InitializeAsync(client, settings);
            var emails = new CosmosDocumentStore<EmailDocument>(client, settings);
            var cases = new CosmosDocumentStore<CaseDocument>(client, settings);
            var email = new EmailDocument("email-1", "thread", Direction.inbound, DateTimeOffset.UtcNow, []);
            StoredDocument<EmailDocument> created = await emails.CreateAsync("one", email, default);
            var duplicate = await Assert.ThrowsAsync<DocumentStoreException>(() => emails.CreateAsync("one", email, default));
            Assert.Equal(StoreFailure.Conflict, duplicate.Failure);
            var missing = await Assert.ThrowsAsync<DocumentStoreException>(() => emails.ReadAsync("two", email.Id, default));
            Assert.Equal(StoreFailure.NotFound, missing.Failure);
            StoredDocument<EmailDocument> read = await emails.ReadAsync("one", email.Id, default);
            Assert.Equal("thread", read.Document.ConversationId);
            Container container = client.GetContainer(settings.Database, "one-emails");
            ItemResponse<JsonObject> raw = await container.ReadItemAsync<JsonObject>(email.Id, new PartitionKey(email.Id));
            Assert.Equal(2592000, raw.Resource["ttl"]!.GetValue<int>());
            EmailDocument folded = email with { Children = [new ChildResult(null, ChildStatus.Folded, null, null, null, "case-1", [], [], [])] };
            StoredDocument<EmailDocument> updated = await emails.ReplaceAsync("one", email.Id, folded, read.ETag, default);
            raw = await container.ReadItemAsync<JsonObject>(email.Id, new PartitionKey(email.Id));
            Assert.Equal(-1, raw.Resource["ttl"]!.GetValue<int>());
            var stale = await Assert.ThrowsAsync<DocumentStoreException>(() => emails.DeleteAsync("one", email.Id, created.ETag, default));
            Assert.Equal(StoreFailure.PreconditionFailed, stale.Failure);
            var record = new CaseDocument("case-1", "case-1", "Example Child", "EHC-123", null, new DateOnly(2016, 3, 14), [], [], [], []);
            StoredDocument<CaseDocument> savedCase = await cases.CreateAsync("one", record, default);
            Assert.Equal(record.DateOfBirth, (await cases.ReadAsync("one", "case-1", default)).Document.DateOfBirth);
            await cases.DeleteAsync("one", "case-1", savedCase.ETag, default);
            await emails.DeleteAsync("one", email.Id, updated.ETag, default);
        }
        finally { await client.GetDatabase(settings.Database).DeleteAsync(); }
    }
}

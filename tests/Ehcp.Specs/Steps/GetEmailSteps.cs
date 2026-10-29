using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Cosmos;
using Reqnroll;
using Xunit;

namespace Ehcp.Specs.Steps;

[Binding]
public sealed class GetEmailSteps : IDisposable
{
    private const string MailboxId = "ehcp-team-shared";

    private readonly CosmosSettings settings = new()
    {
        Endpoint = Environment.GetEnvironmentVariable("Cosmos__Endpoint") ?? "https://localhost:8081",
        Key = Environment.GetEnvironmentVariable("Cosmos__Key") ?? new CosmosSettings().Key,
        Database = Environment.GetEnvironmentVariable("Cosmos__Database") ?? "ehcp-local",
        Mailboxes = new() { [MailboxId] = new() { EmailsContainer = "ehcp-team-emails", CasesContainer = "ehcp-team-cases" } }
    };
    private readonly CosmosClient client;
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("Ehcp__BaseUrl") ?? "http://localhost:5100")
    };
    private EmailDocument? seeded;
    private HttpResponseMessage? response;

    public GetEmailSteps() => client = new CosmosClient(settings.Endpoint, settings.Key, new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions
    });

    [Given("the database is setup to contain an email with id {string} and processing status {string}")]
    public async Task GivenTheDatabaseContainsAnEmail(string emailId, string status)
    {
        ChildResult child = new(null, Enum.Parse<ChildStatus>(status), null, null, null,
            status == nameof(ChildStatus.Folded) ? "spec-case" : null, [], [], []);
        seeded = new EmailDocument(emailId, "spec-thread", Direction.inbound, DateTimeOffset.UtcNow, [child]);
        await new CosmosDocumentStore<EmailDocument>(client, settings).CreateAsync(MailboxId, seeded, default);
    }

    [When(@"a request is made to the \/emails\/{string} endpoint")]
    public async Task WhenARequestIsMadeToTheEmailsEndpoint(string emailId) =>
        response = await http.GetAsync($"/api/mailboxes/{MailboxId}/emails/{Uri.EscapeDataString(emailId)}");

    [Then("the email record should be retrieved with processing status {string}")]
    public async Task ThenTheEmailRecordShouldBeRetrieved(string status)
    {
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        EmailResponse? body = await response.Content.ReadFromJsonAsync<EmailResponse>();
        Assert.NotNull(body);
        Assert.Equal(seeded!.Id, body.Id);
        Assert.Equal(status, body.Status);
    }

    [AfterScenario]
    public async Task RemoveSeededRecord()
    {
        if (seeded is null) return;
        Container container = client.GetContainer(settings.Database, settings.Mailboxes[MailboxId].EmailsContainer);
        try { await container.DeleteItemAsync<JsonObject>(seeded.Id, new PartitionKey(seeded.Id)); }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
    }

    public void Dispose()
    {
        http.Dispose();
        client.Dispose();
    }
}

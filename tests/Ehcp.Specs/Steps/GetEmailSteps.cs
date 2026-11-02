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
public sealed partial class GetEmailSteps : IDisposable
{
    private string mailboxId = string.Empty;

    private readonly CosmosSettings settings = new()
    {
        Endpoint = Environment.GetEnvironmentVariable("Cosmos__Endpoint") ?? "https://localhost:8081",
        Key = Environment.GetEnvironmentVariable("Cosmos__Key") ?? new CosmosSettings().Key,
        Database = Environment.GetEnvironmentVariable("Cosmos__Database") ?? "ehcp-local"
    };
    private readonly CosmosClient client;
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("Ehcp__BaseUrl") ?? "http://localhost:5100")
    };
    private EmailDocument? seeded;
    private string? seededMailboxId;
    private HttpResponseMessage? response;

    public GetEmailSteps() => client = new CosmosClient(settings.Endpoint, settings.Key, new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions
    });

    [Given("the mailbox {string} is selected")]
    public void GivenTheMailboxIsSelected(string mailbox)
    {
        mailboxId = mailbox;
        settings.Mailboxes[mailbox] = new() { EmailsContainer = "ehcp-team-emails", CasesContainer = "ehcp-team-cases" };
    }

    [Given("the database is setup to contain an email with id {string} and processing status {string}")]
    public async Task GivenTheDatabaseContainsAnEmail(string emailId, string status)
    {
        // "status" here is the email-level EmailProcessingStatus (Unprocessed/Processing/Processed/Failed),
        // not the per-child ChildStatus; the child itself is left at its default Unprocessed state.
        ChildResult child = new(null, ChildStatus.Unprocessed, null, null, null, null, [], [], []);
        seeded = new EmailDocument(emailId, "spec-thread", Direction.inbound, DateTimeOffset.UtcNow, [child],
            Enum.Parse<EmailProcessingStatus>(status));
        await new CosmosDocumentStore<EmailDocument>(client, settings).CreateAsync(mailboxId, seeded, default);
        seededMailboxId = mailboxId;
    }

    [When(@"a request is made to the \/mailbox\/{string}\/emails\/{string} endpoint")]
    public async Task WhenARequestIsMadeToTheEmailsEndpoint(string mailbox, string emailId) =>
        response = await http.GetAsync($"/api/mailboxes/{Uri.EscapeDataString(mailbox)}/emails/{Uri.EscapeDataString(emailId)}");

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

    [Then("the response should be not found")]
    public void ThenTheResponseShouldBeNotFound()
    {
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [AfterScenario]
    public async Task RemoveSeededRecord()
    {
        if (seeded is null || seededMailboxId is null) return;
        Container container = client.GetContainer(settings.Database, settings.Mailboxes[seededMailboxId].EmailsContainer);
        try { await container.DeleteItemAsync<JsonObject>(seeded.Id, new PartitionKey(seeded.Id)); }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
    }

    public void Dispose()
    {
        http.Dispose();
        client.Dispose();
    }
}

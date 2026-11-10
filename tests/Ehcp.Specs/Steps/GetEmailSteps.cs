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
    // Environment configuration, read once. Defaults target the local emulator and API.
    private static string EnvOr(string name, string fallback) => Environment.GetEnvironmentVariable(name) ?? fallback;

    private static string CosmosEndpoint => EnvOr("Cosmos__Endpoint", "https://localhost:8081");
    private static string CosmosKey => EnvOr("Cosmos__Key", new CosmosSettings().Key);
    private static string CosmosDatabase => EnvOr("Cosmos__Database", " ehcp-team-shared-1");
    private static string EhcpBaseUrl => EnvOr("Ehcp__BaseUrl", "http://localhost:5100");
    private static string AzureWebJobsStorage => EnvOr("AzureWebJobsStorage", "UseDevelopmentStorage=true");
    private static string ProcessingQueueName => EnvOr("ProcessingQueueName", "ehcp-processing");

    private static string MockGraphBaseUrl => EnvOr("MockGraph__BaseUrl", "http://localhost:7101");
    private static string MockExtractionBaseUrl => EnvOr("MockExtraction__BaseUrl", "http://localhost:7102");

    private string _mailboxId = string.Empty;

    private readonly CosmosSettings _settings = new()
    {
        Endpoint = CosmosEndpoint,
        Key = CosmosKey,
        Database = CosmosDatabase
    };
    private readonly CosmosClient _client;
    private readonly HttpClient _http = new() { BaseAddress = new Uri(EhcpBaseUrl) };
    private EmailDocument? _seeded;
    private string? _seededMailboxId;
    private HttpResponseMessage? _response;

    public GetEmailSteps()
    {
        LocalServices.RequireEndpoint(CosmosEndpoint);
        LocalServices.RequireEndpoint(EhcpBaseUrl);
        LocalServices.RequireEndpoint(MockGraphBaseUrl);
        LocalServices.RequireEndpoint(MockExtractionBaseUrl);
        LocalServices.RequireAzurite(AzureWebJobsStorage);
        _client = new CosmosClient(_settings.Endpoint, _settings.Key, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            UseSystemTextJsonSerializerWithOptions = CosmosDocumentStore<EmailDocument>.JsonOptions
        });
    }

    [Given("the mailbox {string} is selected")]
    public void GivenTheMailboxIsSelected(string mailbox)
    {
        _mailboxId = mailbox;
        _settings.Mailboxes[mailbox] = new MailboxSettings
        {
            EmailsContainer = EnvOr($"Cosmos__Mailboxes__{mailbox}__EmailsContainer", "ehcp-team-emails"),
            CasesContainer = EnvOr($"Cosmos__Mailboxes__{mailbox}__CasesContainer", "ehcp-team-cases")
        };
    }

    [Given("the database is setup to contain an email with id {string} and processing status {string}")]
    public async Task GivenTheDatabaseContainsAnEmail(string emailId, string status)
    {
        // "status" here is the email-level EmailProcessingStatus (Unprocessed/Processing/Processed/Failed/Discarded),
        // not the per-child ChildStatus; the child itself is left at its default Unprocessed state.
        ChildResult child = new(null, ChildStatus.Unprocessed, null, null, null, null, [], [], []);
        _seeded = new EmailDocument(emailId, "spec-thread", Direction.inbound, DateTimeOffset.UtcNow, [child],
            Enum.Parse<EmailProcessingStatus>(status));
        await new CosmosDocumentStore<EmailDocument>(_client, _settings).CreateAsync(_mailboxId, _seeded, default);
        _seededMailboxId = _mailboxId;
    }

     [When(@"a request is made to the \/mailbox\/{string}\/cases\/{string} endpoint")]
      public async Task WhenARequestIsMadeToTheMailboxCasesEndpoint(string mailbox, string caseId) =>
        _response = await _http.GetAsync($"/api/mailboxes/{Uri.EscapeDataString(mailbox)}/emails/{Uri.EscapeDataString(caseId)}");


    [When(@"a request is made to the \/mailbox\/{string}\/emails\/{string} endpoint")]
    public async Task WhenARequestIsMadeToTheEmailsEndpoint(string mailbox, string emailId) =>
        _response = await _http.GetAsync($"/api/mailboxes/{Uri.EscapeDataString(mailbox)}/emails/{Uri.EscapeDataString(emailId)}");

    [Then("the email record should be retrieved with processing status {string}")]
    public async Task ThenTheEmailRecordShouldBeRetrieved(string status)
    {
        Assert.NotNull(_response);
        Assert.Equal(HttpStatusCode.OK, _response.StatusCode);
        Assert.NotNull(_response.Headers.ETag);
        var body = await _response.Content.ReadFromJsonAsync<EmailResponse>();
        Assert.NotNull(body);
        Assert.Equal(_seeded!.Id, body.Id);
        Assert.Equal(status, body.Status);
    }

    [Then("the response should be not found")]
    public void ThenTheResponseShouldBeNotFound()
    {
        Assert.NotNull(_response);
        Assert.Equal(HttpStatusCode.NotFound, _response.StatusCode);
    }

    [AfterScenario]
    public async Task RemoveSeededRecord()
    {
        if (_seeded is null || _seededMailboxId is null) return;
        Container container = _client.GetContainer(_settings.Database, _settings.Mailboxes[_seededMailboxId].EmailsContainer);
        try { await container.DeleteItemAsync<JsonObject>(_seeded.Id, new PartitionKey(_seeded.Id)); }
        catch (CosmosException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
    }

    public void Dispose()
    {
        _http.Dispose();
        _client.Dispose();
    }
}


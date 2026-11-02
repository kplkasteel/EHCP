using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Reqnroll;
using Xunit;

namespace Ehcp.Specs.Steps;

// Drives the queue-triggered Ehcp.Processing function (expected to be already running locally via
// `func start`, against the same Azurite instance referenced by AzureWebJobsStorage). These steps do
// not start that host; if nothing is listening on the queue, "wait for processing status" times out.
public sealed partial class GetEmailSteps
{
    private const string DataFolder = "data";

    // Wrapper shapes the running Function's mock adapters actually deserialize
    // (Ehcp.Infrastructure.FixtureServices.GraphFixture / ExtractionFixture).
    private sealed record GraphFixtureFile(string MailboxId, GraphEmail Email);
    private sealed record ExtractionFixtureFile(string MailboxId, string EmailId, EmailExtraction Result);
    private sealed record QueueMessageFile(string MailboxId, string EmailId, string GraphMessageId);

    // Human-authored shape of the committed ai-extract dataset files: richer than EmailExtraction
    // (adds relevance/reliability flags that nothing in Ehcp.Core/Ehcp.Application consumes yet).
    private sealed record AiExtractDatasetFile(bool Relevance, bool Reliability, IReadOnlyList<ExtractedChild> ExtractedChildren);

    // Human-authored shape of the committed graph-email dataset files: a trimmed Microsoft Graph
    // message payload, not the plain GraphEmail record the mock adapter deserializes into.
    private sealed record GraphEmailAddress(string Name, string Address);
    private sealed record GraphEmailRecipient(GraphEmailAddress EmailAddress);
    private sealed record GraphEmailBody(string ContentType, string Content);
    private sealed record GraphEmailDatasetFile(
        string Id, string ConversationId, string Subject, GraphEmailBody Body, GraphEmailRecipient From);

    private EmailExtraction? pendingExtraction;
    private GraphEmail? pendingGraphEmail;
    private readonly List<(string MailboxId, string Id)> seededDatasetDocuments = [];
    private string? processingMailboxId;
    private string? processingEmailId;

    [Given("the database is setup to contain {string}")]
    public async Task GivenTheDatabaseIsSetupToContainDataset(string dataset)
    {
        EmailDocument[] documents = await ReadDatasetAsync<EmailDocument[]>(dataset);
        var store = new CosmosDocumentStore<EmailDocument>(client, settings);
        foreach (EmailDocument document in documents)
        {
            await store.CreateAsync(mailboxId, document, default);
            seededDatasetDocuments.Add((mailboxId, document.Id));
        }
    }

    [Given("we setup mock ai extraction to return dataset {string}")]
    public async Task GivenWeSetupMockAiExtractionToReturnDataset(string dataset)
    {
        AiExtractDatasetFile file = await ReadDatasetAsync<AiExtractDatasetFile>(dataset);
        // Relevance/Reliability are recorded here only as a note: EmailExtraction (Ehcp.Core/Processing.cs)
        // has no fields for them, so they cannot be mapped onto anything the processor reads.
        pendingExtraction = new EmailExtraction(file.ExtractedChildren);
    }

    [Given("we setup mock graph api to return dataset {string}")]
    public async Task GivenWeSetupMockGraphApiToReturnDataset(string dataset)
    {
        GraphEmailDatasetFile file = await ReadDatasetAsync<GraphEmailDatasetFile>(dataset);
        // Direction and ReceivedDateTime cannot be mapped: the dataset file carries no explicit direction
        // field (production infers it by comparing the sender address to the configured mailbox user,
        // which Mock mode has no mailbox-user mapping for) and no receivedDateTime field at all. Both are
        // filled with a fixed placeholder below rather than silently using "now", so a rerun is stable,
        // but note the result will only match an expected-result.json that also expects these placeholders.
        pendingGraphEmail = new GraphEmail(file.Id, file.ConversationId, Direction.inbound,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), file.Subject, file.Body.Content);
    }

    [When("the email record {string} is added to the queue")]
    public async Task WhenTheEmailRecordIsAddedToTheQueue(string dataset)
    {
        QueueMessageFile message = await ReadDatasetAsync<QueueMessageFile>(dataset);
        Assert.NotNull(pendingGraphEmail);
        Assert.NotNull(pendingExtraction);
        if (pendingGraphEmail.Id != message.GraphMessageId)
            throw new InvalidOperationException(
                $"The graph-api dataset's id ('{pendingGraphEmail.Id}') does not match the queue " +
                $"message's graphMessageId ('{message.GraphMessageId}'); the mock will reject the request.");

        await WriteFixtureAsync("Processing__Graph__DataPath",
            new GraphFixtureFile(message.MailboxId, pendingGraphEmail));
        await WriteFixtureAsync("Processing__Extraction__DataPath",
            new ExtractionFixtureFile(message.MailboxId, message.GraphMessageId, pendingExtraction));

        string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
        string queueName = Environment.GetEnvironmentVariable("ProcessingQueueName") ?? "ehcp-processing";
        var queue = new QueueClient(connectionString, queueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queue.CreateIfNotExistsAsync();
        string payload = JsonSerializer.Serialize(
            new ProcessEmailMessage(message.MailboxId, message.EmailId, message.GraphMessageId),
            CosmosDocumentStore<EmailDocument>.JsonOptions);
        await queue.SendMessageAsync(payload);

        processingMailboxId = message.MailboxId;
        processingEmailId = message.EmailId;
    }

    [When("we wait for the database processing status to be {string}")]
    public async Task WhenWeWaitForTheDatabaseProcessingStatusToBe(string status)
    {
        Assert.NotNull(processingMailboxId);
        Assert.NotNull(processingEmailId);
        EmailProcessingStatus expected = Enum.Parse<EmailProcessingStatus>(status);
        var store = new CosmosDocumentStore<EmailDocument>(client, settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        EmailProcessingStatus? last = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                StoredDocument<EmailDocument> stored = await store.ReadAsync(processingMailboxId, processingEmailId, default);
                last = stored.Document.Status;
                if (last == expected) return;
            }
            catch (DocumentStoreException error) when (error.Failure == StoreFailure.NotFound) { }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        Assert.Fail($"Timed out waiting for '{processingEmailId}' to reach status '{status}'. " +
            $"Last observed status: {last?.ToString() ?? "(document not found)"}. " +
            "Is the Ehcp.Processing function host running against the same Azurite instance?");
    }

    [Then("the email record should be retrieved matching {string}")]
    public async Task ThenTheEmailRecordShouldBeRetrievedMatching(string dataset)
    {
        Assert.NotNull(response);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        string actualJson = await response.Content.ReadAsStringAsync();
        JsonNode? actual = JsonNode.Parse(actualJson);
        JsonNode? expected = JsonNode.Parse(await File.ReadAllTextAsync(DatasetPath(dataset)));
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Response did not match '{dataset}'.\nExpected:\n{expected?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}\n" +
            $"Actual:\n{actual?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
    }

    [AfterScenario]
    public async Task RemoveSeededDatasetDocuments()
    {
        var store = new CosmosDocumentStore<EmailDocument>(client, settings);
        foreach ((string seededMailbox, string id) in seededDatasetDocuments)
        {
            try
            {
                StoredDocument<EmailDocument> current = await store.ReadAsync(seededMailbox, id, default);
                await store.DeleteAsync(seededMailbox, id, current.ETag, default);
            }
            catch (DocumentStoreException error) when (error.Failure == StoreFailure.NotFound) { }
        }
        // Note: if matching created a CaseDocument (MatchingService.cs derives its id from a hash of
        // mailboxId + identity), it is not cleaned up here - reproducing that derivation would duplicate
        // production logic in the test project.
    }

    private static string DatasetPath(string dataset) => Path.Combine(AppContext.BaseDirectory, DataFolder, dataset);

    private static async Task<T> ReadDatasetAsync<T>(string dataset)
    {
        await using FileStream stream = File.OpenRead(DatasetPath(dataset));
        return await JsonSerializer.DeserializeAsync<T>(stream, CosmosDocumentStore<EmailDocument>.JsonOptions)
            ?? throw new InvalidOperationException($"Dataset '{dataset}' is empty.");
    }

    private static async Task WriteFixtureAsync<T>(string dataPathEnvVar, T fixture)
    {
        string path = Environment.GetEnvironmentVariable(dataPathEnvVar)
            ?? throw new InvalidOperationException(
                $"{dataPathEnvVar} must be set to an absolute path (see src/Ehcp.Processing/local.settings.example.json) " +
                "so this step can write the fixture the running Function host reads.");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(fixture, CosmosDocumentStore<EmailDocument>.JsonOptions));
    }
}

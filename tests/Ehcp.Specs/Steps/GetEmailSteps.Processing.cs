using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Queues;
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

    private sealed record ExtractionFixtureFile(string MailboxId, string EmailId, EmailExtraction Result);
    private sealed record QueueMessageFile(string MailboxId, string EmailId, string GraphMessageId);

    // Human-authored shape of the committed ai-extract dataset files; reliability remains fixture metadata.
    private sealed record AiExtractDatasetFile(
        bool Relevance, bool Reliability, IReadOnlyList<ExtractedChild> ExtractedChildren, string? Rationale = null);

    // Human-authored shape of the committed graph-email dataset files: a trimmed Microsoft Graph
    // message payload, not the plain GraphEmail record the mock adapter deserializes into.
    private sealed record GraphEmailAddress(string Name, string Address);
    private sealed record GraphEmailRecipient(GraphEmailAddress EmailAddress);
    private sealed record GraphEmailBody(string ContentType, string Content);
    private sealed record GraphEmailDatasetFile(
        string Id, string ConversationId, string Subject, GraphEmailBody Body, GraphEmailRecipient From);

    // Payload POSTed to MockGraph's /api/seed: the Graph message shape GraphAPIService reads.
    private sealed record GraphSeedMessage(
        string Id, string ConversationId, DateTimeOffset ReceivedDateTime, string Subject,
        GraphEmailBody Body, GraphEmailRecipient From);

    private static readonly HttpClient SeedHttp = new();

    private readonly Dictionary<string, EmailDocument> _seededEmails = new();
    private EmailExtraction? _pendingExtraction;
    private GraphEmail? _pendingGraphEmail;
    private GraphSeedMessage? _pendingGraphMessage;
    private readonly List<(string MailboxId, string Id)> _seededDatasetDocuments = [];
    private string? _processingMailboxId;
    private string? _processingEmailId;

    [Given("the database is setup to contain {string}")]
    public async Task GivenTheDatabaseIsSetupToContainDataset(string dataset)
    {
        EmailDocument[] documents = await ReadDatasetAsync<EmailDocument[]>(dataset);
        var store = new CosmosDocumentStore<EmailDocument>(_client, _settings);
        foreach (EmailDocument document in documents)
        {
            await store.CreateAsync(_mailboxId, document, default);
            _seededEmails[document.Id] = document;
            _seededDatasetDocuments.Add((_mailboxId, document.Id));
        }
    }

    [Given("we setup mock ai extraction to return dataset {string}")]
    public async Task GivenWeSetupMockAiExtractionToReturnDataset(string dataset)
    {
        AiExtractDatasetFile file = await ReadDatasetAsync<AiExtractDatasetFile>(dataset);
        _pendingExtraction = new EmailExtraction(
            file.ExtractedChildren, Relevance: file.Relevance, Rationale: file.Rationale);
    }

    [Given("we setup mock graph api to return dataset {string}")]
    public async Task GivenWeSetupMockGraphApiToReturnDataset(string dataset)
    {
        GraphEmailDatasetFile file = await ReadDatasetAsync<GraphEmailDatasetFile>(dataset);
        // The supplied Graph fixture omits receivedDateTime. Resolve it from the seeded
        // application record at enqueue time rather than inventing a fixed timestamp.
        _pendingGraphEmail = new GraphEmail(file.Id, file.ConversationId, Direction.inbound,
            default, file.Subject, file.Body.Content);
        _pendingGraphMessage = new GraphSeedMessage(file.Id, file.ConversationId,
            default, file.Subject, file.Body, file.From);
    }

    [When("the email record {string} is added to the queue")]
    public async Task WhenTheEmailRecordIsAddedToTheQueue(string dataset)
    {
        QueueMessageFile message = await ReadDatasetAsync<QueueMessageFile>(dataset);
        Assert.NotNull(_pendingGraphEmail);
        Assert.NotNull(_pendingExtraction);
        Assert.NotNull(_pendingGraphMessage);
        if (_pendingGraphEmail.Id != message.GraphMessageId)
            throw new InvalidOperationException(
                $"The graph-api dataset's id ('{_pendingGraphEmail.Id}') does not match the queue " +
                $"message's graphMessageId ('{message.GraphMessageId}'); the mock will reject the request.");

        Assert.Equal(_mailboxId, message.MailboxId);
        if (!_seededEmails.TryGetValue(message.EmailId, out EmailDocument? seededEmail))
            throw new InvalidOperationException("Seed the application email before enqueueing this scenario.");
        _pendingGraphEmail = _pendingGraphEmail with
        {
            Direction = seededEmail.Direction,
            ReceivedDateTime = seededEmail.ReceivedDateTime
        };
        _pendingGraphMessage = _pendingGraphMessage with { ReceivedDateTime = seededEmail.ReceivedDateTime };

        await SeedAsync(MockGraphBaseUrl, _pendingGraphMessage);
        var extractionFixture = new ExtractionFixtureFile(message.MailboxId, message.GraphMessageId, _pendingExtraction);
        await SeedAsync(MockExtractionBaseUrl, extractionFixture);

        var queue = new QueueClient(AzureWebJobsStorage, ProcessingQueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        await queue.CreateIfNotExistsAsync();
        string payload = JsonSerializer.Serialize(
            new ProcessEmailMessage(message.MailboxId, message.EmailId, message.GraphMessageId),
            CosmosDocumentStore<EmailDocument>.JsonOptions);
        await queue.SendMessageAsync(payload);

        _processingMailboxId = message.MailboxId;
        _processingEmailId = message.EmailId;
    }

    [When("we wait for the database processing status to be {string}")]
    public async Task WhenWeWaitForTheDatabaseProcessingStatusToBe(string status)
    {
        Assert.NotNull(_processingMailboxId);
        Assert.NotNull(_processingEmailId);
        EmailProcessingStatus expected = Enum.Parse<EmailProcessingStatus>(status);
        var store = new CosmosDocumentStore<EmailDocument>(_client, _settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        EmailProcessingStatus? last = null;
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    StoredDocument<EmailDocument> stored = await store.ReadAsync(_processingMailboxId, _processingEmailId, timeout.Token);
                    last = stored.Document.Status;
                    if (last == expected) return;
                }
                catch (DocumentStoreException error) when (error.Failure == StoreFailure.NotFound) { }
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        Assert.Fail($"Timed out waiting for '{_processingEmailId}' to reach status '{status}'. " +
            $"Last observed status: {last?.ToString() ?? "(document not found)"}. " +
            "Is the Ehcp.Processing function host running against the same Azurite instance?");
    }

    [Then("the email record should be retrieved matching {string}")]
    public async Task ThenTheEmailRecordShouldBeRetrievedMatching(string dataset)
    {
        Assert.NotNull(_response);
        Assert.Equal(System.Net.HttpStatusCode.OK, _response.StatusCode);

        JsonNode? actual = JsonNode.Parse(
            await _response.Content.ReadAsStringAsync());

        JsonNode? expected = JsonNode.Parse(
            await File.ReadAllTextAsync(DatasetPath(dataset)));

        Assert.NotNull(actual);
        Assert.NotNull(expected);
        Assert.NotNull(actual["receivedDateTime"]);
        Assert.NotNull(expected["receivedDateTime"]);

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        DateTimeOffset actualDate = DateTimeOffset.Parse(
            actual["receivedDateTime"]!.GetValue<string>(), culture);

        DateTimeOffset expectedDate = DateTimeOffset.Parse(
            expected["receivedDateTime"]!.GetValue<string>(), culture);

        Assert.Equal(expectedDate, actualDate);

        // Dates match; use the same representation for the JSON comparison.
        actual["receivedDateTime"] =
            expected["receivedDateTime"]!.GetValue<string>();

        var options = new JsonSerializerOptions { WriteIndented = true };

        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Response did not match '{dataset}'.\n" +
            $"Expected:\n{expected.ToJsonString(options)}\n" +
            $"Actual:\n{actual.ToJsonString(options)}");
    }

    [Then("the case record should be retrieved matching {string}")]
    public async Task ThenTheCaseRecordShouldBeRetrievedMatching(string dataset)
    {
        Assert.NotNull(_response);
        Assert.Equal(System.Net.HttpStatusCode.OK, _response.StatusCode);
        string actualJson = await _response.Content.ReadAsStringAsync();
        JsonNode? actual = JsonNode.Parse(actualJson);
        JsonNode? expected = JsonNode.Parse(await File.ReadAllTextAsync(DatasetPath(dataset)));
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Response did not match '{dataset}'.\nExpected:\n{expected?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}\n" +
            $"Actual:\n{actual?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
    }

    [AfterScenario]
    public async Task RemoveSeededDatasetDocuments()
    {
        var store = new CosmosDocumentStore<EmailDocument>(_client, _settings);
        foreach ((string seededMailbox, string id) in _seededDatasetDocuments)
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

    private static async Task SeedAsync(string baseUrl, object payload)
    {
        string json = JsonSerializer.Serialize(
            payload, CosmosDocumentStore<EmailDocument>.JsonOptions);

        using var content = new StringContent(
            json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await SeedHttp.PostAsync(
            $"{baseUrl.TrimEnd('/')}/api/seed", content);

        response.EnsureSuccessStatusCode();
    }

}

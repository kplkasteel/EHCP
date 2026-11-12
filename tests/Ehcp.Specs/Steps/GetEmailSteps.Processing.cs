using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Storage.Queues;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Ehcp.Specs.Hooks;
using Microsoft.Azure.Cosmos;
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
        }
    }

    [Given("the database contains cases {string}")]
    public async Task GivenTheDatabaseContainsCases(string dataset)
    {
        CaseDocument[] documents = await ReadDatasetAsync<CaseDocument[]>(dataset);
        var store = new CosmosDocumentStore<CaseDocument>(_client, _settings);
        foreach (CaseDocument document in documents)
            await store.CreateAsync(_mailboxId, document, default);
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
        _processingMailboxId = message.MailboxId;
        _processingEmailId = message.EmailId;
        await queue.SendMessageAsync(payload);
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
            await RenderDatasetAsync(dataset));

        Assert.NotNull(actual);
        Assert.NotNull(expected);
        AssertSameReceivedDateTime(expected, actual);

        JsonArray expectedChildren = expected["children"]!.AsArray();
        JsonArray actualChildren = actual["children"]!.AsArray();
        Assert.Equal(expectedChildren.Count, actualChildren.Count);
        for (int i = 0; i < expectedChildren.Count; i++)
        {
            JsonNode? expectedId = expectedChildren[i]?["matchedCaseId"];
            if (expectedId is null) continue;

            string alias = expectedId.GetValue<string>();
            string? actualId = actualChildren[i]?["matchedCaseId"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(actualId), $"Expected a case ID for '{alias}'.");
            if (_caseIds.TryGetValue(alias, out string? previousId))
                Assert.Equal(previousId, actualId);
            else
                _caseIds.Add(alias, actualId!);
            expectedChildren[i]!["matchedCaseId"] = actualId;
        }

        ProjectAttributes(expected, actual);
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
        JsonNode? expected = JsonNode.Parse(await RenderDatasetAsync(dataset));
        Assert.NotNull(actual);
        Assert.NotNull(expected);
        ProjectAttributes(expected, actual);
        if (expected["id"] is JsonNode expectedId)
            expected["id"] = ResolveCaseId(expectedId.GetValue<string>());
        if (expected["entityId"] is JsonNode expectedEntityId)
            expected["entityId"] = ResolveCaseId(expectedEntityId.GetValue<string>());

        JsonArray? expectedCommunications = expected["communications"]?.AsArray();
        JsonArray? actualCommunications = actual["communications"]?.AsArray();
        Assert.NotNull(expectedCommunications);
        Assert.NotNull(actualCommunications);
        Assert.Equal(expectedCommunications.Count, actualCommunications.Count);
        for (int i = 0; i < expectedCommunications.Count; i++)
        {
            Assert.NotNull(expectedCommunications[i]);
            Assert.NotNull(actualCommunications[i]);
            AssertSameReceivedDateTime(expectedCommunications[i]!, actualCommunications[i]!);
        }
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Response did not match '{dataset}'.\nExpected:\n{expected?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}\n" +
            $"Actual:\n{actual?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
    }

    private static void AssertSameReceivedDateTime(JsonNode expected, JsonNode actual)
    {
        Assert.NotNull(expected["receivedDateTime"]);
        Assert.NotNull(actual["receivedDateTime"]);
        string expectedValue = expected["receivedDateTime"]!.GetValue<string>();
        string actualValue = actual["receivedDateTime"]!.GetValue<string>();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(DateTimeOffset.Parse(expectedValue, culture), DateTimeOffset.Parse(actualValue, culture));
        actual["receivedDateTime"] = expectedValue;
    }

    [AfterScenario]
    public async Task RemoveScenarioDocuments()
    {
        if (_settings.Database != LocalTestStack.DatabaseName || string.IsNullOrEmpty(LocalTestStack.DatabaseName))
            throw new InvalidOperationException("Refusing to clean up a database not owned by this test run.");

        bool processingWasStarted = _processingEmailId is not null;
        if (processingWasStarted) LocalTestStack.StopProcessing();

        if (processingWasStarted)
        {
            await ClearQueueAsync(ProcessingQueueName);
            await ClearQueueAsync(ProcessingQueueName + "-poison");
        }

        foreach (MailboxSettings mailbox in _settings.Mailboxes.Values)
        {
            await RemoveDocumentsAsync(mailbox.EmailsContainer, "id");
            await RemoveDocumentsAsync(mailbox.CasesContainer, "entityId");
        }

        if (processingWasStarted) await LocalTestStack.StartProcessingAsync();
    }

    private static async Task ClearQueueAsync(string name)
    {
        var queue = new QueueClient(AzureWebJobsStorage, name);
        if (await queue.ExistsAsync()) await queue.ClearMessagesAsync();
    }

    private async Task RemoveDocumentsAsync(string containerName, string partitionKeyProperty)
    {
        Container container = _client.GetContainer(_settings.Database, containerName);
        using FeedIterator<JsonObject> iterator = container.GetItemQueryIterator<JsonObject>(
            "SELECT c.id, c.entityId FROM c");
        var documents = new List<(string Id, string PartitionKey)>();
        while (iterator.HasMoreResults)
        {
            foreach (JsonObject document in await iterator.ReadNextAsync())
                documents.Add((document["id"]!.GetValue<string>(), document[partitionKeyProperty]!.GetValue<string>()));
        }

        foreach ((string id, string partitionKey) in documents)
        {
            try { await container.DeleteItemAsync<JsonObject>(id, new PartitionKey(partitionKey)); }
            catch (CosmosException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
    }

    private static string DatasetPath(string dataset) => Path.Combine(AppContext.BaseDirectory, DataFolder, dataset);

    private async Task<T> ReadDatasetAsync<T>(string dataset)
    {
        string text = await RenderDatasetAsync(dataset);
        return JsonSerializer.Deserialize<T>(text, CosmosDocumentStore<EmailDocument>.JsonOptions)
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

using System.Text.Json;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Functions.Worker;

namespace Ehcp.Processing;

public sealed class ProcessEmailFunction(IEmailProcessor processor)
{
    [Function("ProcessEmail")]
    public Task RunAsync(
        [QueueTrigger("%ProcessingQueueName%", Connection = "AzureWebJobsStorage")] string payload,
        CancellationToken ct)
    {
        ProcessEmailMessage message = JsonSerializer.Deserialize<ProcessEmailMessage>(
            payload, CosmosDocumentStore<EmailDocument>.JsonOptions)
            ?? throw new DocumentValidationException("Queue message is empty.");
        return processor.ProcessAsync(message, ct);
    }
}

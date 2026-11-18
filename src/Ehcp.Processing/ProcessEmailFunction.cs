using System.Text.Json;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Ehcp.Processing;

public sealed class ProcessEmailFunction(
    IEmailProcessor processor,
    ILogger<ProcessEmailFunction> logger)
{
    [Function("ProcessEmail")]
    public async Task RunAsync(
        [QueueTrigger("%ProcessingQueueName%", Connection = "AzureWebJobsStorage")] string payload,
        CancellationToken ct)
    {
        // Do not log the queue payload; it can contain sensitive identifiers.
        ProcessEmailMessage message = JsonSerializer.Deserialize<ProcessEmailMessage>(
            payload, CosmosDocumentStore<EmailDocument>.JsonOptions)
            ?? throw new DocumentValidationException("Queue message is empty.");

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["MailboxId"] = message.MailboxId,
            ["EmailId"] = message.EmailId
        }))
        {
            logger.LogInformation("Processing queue message");
            try
            {
                await processor.ProcessAsync(message, ct);
                logger.LogInformation("Processing queue message completed");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Preserve the exception so the Azure Queue trigger can retry/poison it.
                logger.LogError(ex, "Processing queue message failed; retry is handled by the Functions runtime");
                throw;
            }
        }
    }
}

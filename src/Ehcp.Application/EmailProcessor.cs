using Ehcp.Core;

namespace Ehcp.Application;

public sealed class EmailProcessor(
    IGraphAPIService graph,
    IEmailEntityExtractionService extractor,
    IMatchingService matching,
    IDocumentStore<EmailDocument> emails) : IEmailProcessor
{
    public async Task ProcessAsync(ProcessEmailMessage message, CancellationToken ct)
    {
        DocumentRules.ValidateId(message.MailboxId);
        DocumentRules.ValidateId(message.EmailId);
        if (string.IsNullOrWhiteSpace(message.GraphMessageId))
            throw new DocumentValidationException("graphMessageId is required.");
            //JL COMMENT/QUESTION: Do we know what happens if thrown here? Can we add a wrapper to log the exception and then put the queue message into a poison queue? We could add a test to check this behaviour.
            //PK Answer: This fails before we open the email record. When a Rake test fails on an exception,
            // we can see it in the test results. I'd start for instance with Serilog to record which step
            // failed and use a correlation ID to follow the retries. With ILogger in the code, we can keep
            // Serilog for local tests and send logs to Application Insights when we move to Azure.
            // We could then use AI to spot recurring errors. I'd also test that the message reaches the poison queue.

        
        StoredDocument<EmailDocument> current = await ReadOrCreateAsync(message, ct);
        if (current.Document.Status == EmailProcessingStatus.Processed) return;
        current = await emails.ReplaceAsync(message.MailboxId, message.EmailId,
            current.Document with { Status = EmailProcessingStatus.Processing }, current.ETag, ct);
        try
        {
            GraphEmail source = await graph.GetEmailAsync(message.MailboxId, message.GraphMessageId, ct);
            if (source.Id != message.GraphMessageId)
                throw new DocumentValidationException("Graph returned a different message."); //JL COMMENT/QUESTION: Do we know what happens if thrown here? Can we add a wrapper to log the exception and then put the queue message into a poison queue? We could add a test to check this behaviour.
                //PK Answer: Here we try to mark the email as Failed and rethrow. I'd check that the wrong email never reaches extraction or matching.

            EmailExtraction extraction = await extractor.ExtractAsync(message.MailboxId, source, ct);
            
            EmailDocument result = current.Document with //JL COMMENT/QUESTION: We should check if extraction children returned anything first and exit early if not. This is not an exception, it should be an early return. Lets add a test for this.
            //PK Answer: An empty list already skips matching and saves the email as Processed. I'd keep a missing list as a retryable error, pending agreement.
            {
                ConversationId = source.ConversationId,
                Direction = source.Direction,
                ReceivedDateTime = source.ReceivedDateTime,
                Children = extraction.Children?.Select(child => new ChildResult(
                    child.Name, ChildStatus.Unprocessed, child.NhsNumber, child.CaseId, child.DateOfBirth,
                    null, [], child.Relationships, child.Attributes)).ToArray()
                    ?? throw new DocumentValidationException("Extraction children are required.") 
            };
            // Validate the whole extraction before any matching can write cases.
            DocumentRules.Validate(result);
            var children = new List<ChildResult>();
            foreach (ExtractedChild child in extraction.Children)
                children.Add(await matching.MatchAsync(message.MailboxId, result, source, extraction, child, ct));
            result = result with { Children = children, Status = EmailProcessingStatus.Processed };
            DocumentRules.Validate(result);
            await emails.ReplaceAsync(message.MailboxId, message.EmailId, result, current.ETag, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            try
            {
                await emails.ReplaceAsync(message.MailboxId, message.EmailId,
                    current.Document with { Status = EmailProcessingStatus.Failed }, current.ETag, ct);
            }
            catch (DocumentStoreException error) when (error.Failure == StoreFailure.PreconditionFailed) { }
            // Let the queue runtime retry and ultimately poison the message. //JL COMMENT/QUESTION: is this setup in config?
            //PK Answer: Yes. The processing host.json sets five attempts and a five-second retry delay. I'd verify poison-queue delivery in a local test.
            throw;
        }
    }

    private async Task<StoredDocument<EmailDocument>> ReadOrCreateAsync(ProcessEmailMessage message, CancellationToken ct)
    {
        try { return await emails.ReadAsync(message.MailboxId, message.EmailId, ct); }
        catch (DocumentStoreException error) when (error.Failure == StoreFailure.NotFound)
        {
            var placeholder = new EmailDocument(message.EmailId, message.EmailId, Direction.inbound,
                DateTimeOffset.UtcNow, []);
            try { return await emails.CreateAsync(message.MailboxId, placeholder, ct); }
            catch (DocumentStoreException conflict) when (conflict.Failure == StoreFailure.Conflict)
            {
                return await emails.ReadAsync(message.MailboxId, message.EmailId, ct);
            }
        }
    }
}

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

        StoredDocument<EmailDocument> current = await ReadOrCreateAsync(message, ct);
        if (current.Document.Status == EmailProcessingStatus.Processed) return;
        current = await emails.ReplaceAsync(message.MailboxId, message.EmailId,
            current.Document with { Status = EmailProcessingStatus.Processing }, current.ETag, ct);
        try
        {
            GraphEmail source = await graph.GetEmailAsync(message.MailboxId, message.GraphMessageId, ct);
            if (source.Id != message.GraphMessageId)
                throw new DocumentValidationException("Graph returned a different message.");
            EmailExtraction extraction = await extractor.ExtractAsync(message.MailboxId, source, ct);
            EmailDocument result = current.Document with
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
            // Let the queue runtime retry and ultimately poison the message.
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

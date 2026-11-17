using Ehcp.Core;
using Microsoft.Extensions.Logging;

namespace Ehcp.Application;

public sealed class EmailProcessor(
    IGraphAPIService graph,
    IEmailEntityExtractionService extractor,
    IMatchingService matching,
    IDocumentStore<EmailDocument> emails,
    ILogger<EmailProcessor> logger) : IEmailProcessor
{
    public async Task ProcessAsync(ProcessEmailMessage message, CancellationToken ct)
    {
        DocumentRules.ValidateId(message.MailboxId);
        DocumentRules.ValidateId(message.EmailId);
        if (string.IsNullOrWhiteSpace(message.GraphMessageId))
            throw new DocumentValidationException("graphMessageId is required.");

        StoredDocument<EmailDocument> current = await ReadOrCreateAsync(message, ct);
        if (current.Document.Status is EmailProcessingStatus.Processed or EmailProcessingStatus.Discarded)
        {
            logger.LogDebug("Email already has terminal status {ProcessingStatus}; skipping", current.Document.Status);
            return;
        }
        current = await emails.ReplaceAsync(message.MailboxId, message.EmailId,
            current.Document with { Status = EmailProcessingStatus.Processing, Rationale = null }, current.ETag, ct);
        try
        {
            logger.LogDebug("Retrieving Graph message");
            GraphEmail source = await graph.GetEmailAsync(message.MailboxId, message.GraphMessageId, ct);
            if (source.Id != message.GraphMessageId)
                throw new DocumentValidationException("Graph returned a different message.");

            logger.LogDebug("Running extraction");
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

            // Validate the whole extraction before matching can write cases.
            DocumentRules.Validate(result);
            logger.LogInformation("Extraction completed: {ChildCount} children, relevant={IsRelevant}",
                extraction.Children.Count, extraction.Relevance);

            if (extraction.Relevance == false)
            {
                result = result with
                {
                    Children = [],
                    Status = EmailProcessingStatus.Discarded,
                    Rationale = string.IsNullOrWhiteSpace(extraction.Rationale)
                        ? "Email was marked as not relevant."
                        : extraction.Rationale
                };
                DocumentRules.Validate(result);
                await emails.ReplaceAsync(message.MailboxId, message.EmailId, result, current.ETag, ct);
                logger.LogInformation("Email discarded: extraction flagged it as irrelevant");
                return;
            }
            if (extraction.Children.Count == 0)
            {
                result = result with
                {
                    Status = EmailProcessingStatus.Discarded,
                    Rationale = "No child information was extracted from the email."
                };
                DocumentRules.Validate(result);
                await emails.ReplaceAsync(message.MailboxId, message.EmailId, result, current.ETag, ct);
                logger.LogInformation("Email discarded: no child extracted");
                return;
            }

            var children = new List<ChildResult>();
            foreach (ExtractedChild child in extraction.Children)
            {
                ChildResult childResult = await matching.MatchAsync(
                    message.MailboxId, result, source, extraction, child, ct);
                children.Add(childResult);
                // Log status and score summary only; no names, NHS numbers or DOB.
                logger.LogInformation(
                    "Child matching completed: status={ChildStatus}, possibleMatches={CandidateCount}, highestScore={HighestScore}",
                    childResult.Status,
                    childResult.PossibleCaseMatches.Count,
                    childResult.PossibleCaseMatches.Count > 0
                        ? childResult.PossibleCaseMatches.Max(candidate => candidate.Score)
                        : null);
            }
            result = result with { Children = children, Status = EmailProcessingStatus.Processed };
            DocumentRules.Validate(result);
            await emails.ReplaceAsync(message.MailboxId, message.EmailId, result, current.ETag, ct);
            logger.LogInformation("Email persisted as Processed");
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The outer ProcessEmailFunction logs the exception once with the email scope.
            // Preserve the existing Failed state update and queue retry/poison behavior.
            try
            {
                await emails.ReplaceAsync(message.MailboxId, message.EmailId,
                    current.Document with { Status = EmailProcessingStatus.Failed, Rationale = null }, current.ETag, ct);
            }
            catch (DocumentStoreException error) when (error.Failure == StoreFailure.PreconditionFailed) { }
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

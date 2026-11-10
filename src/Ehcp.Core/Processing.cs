namespace Ehcp.Core;

public enum EmailProcessingStatus { Unprocessed, Processing, Processed, Failed, Discarded }

// EmailId is the Cosmos-safe application key; GraphMessageId is the unmodified Graph key.
public sealed record ProcessEmailMessage(string MailboxId, string EmailId, string GraphMessageId);

public sealed record GraphEmail(
    string Id, string ConversationId, Direction Direction, DateTimeOffset ReceivedDateTime,
    string Subject, string Body);

public sealed record ExtractedChild(
    string? Name, string? NhsNumber, string? CaseId, DateOnly? DateOfBirth,
    IReadOnlyList<Relationship> Relationships, IReadOnlyList<CaseAttribute> Attributes);

public sealed record EmailExtraction(
    IReadOnlyList<ExtractedChild> Children,
    string? Summary = null,
    bool? Relevance = null,
    string? Rationale = null);

public interface IGraphAPIService
{
    Task<GraphEmail> GetEmailAsync(string mailboxId, string graphMessageId, CancellationToken ct);
}

public interface IEmailEntityExtractionService
{
    Task<EmailExtraction> ExtractAsync(string mailboxId, GraphEmail email, CancellationToken ct);
}

public interface ICaseLookup
{
    Task<IReadOnlyList<CaseDocument>> FindCandidatesAsync(string mailboxId, ExtractedChild child, CancellationToken ct);
}

public interface IMatchingService
{
    Task<ChildResult> MatchAsync(string mailboxId, EmailDocument email, GraphEmail source,
        EmailExtraction extraction, ExtractedChild child, CancellationToken ct);
}

public interface IEmailProcessor
{
    Task ProcessAsync(ProcessEmailMessage message, CancellationToken ct);
}

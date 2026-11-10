using System.Text.Json.Serialization;

namespace Ehcp.Core;

public enum ConfidenceBand
{
    VeryLow,
    Low,
    Medium,
    High,
    VeryHigh,
    Confirmed
}

public enum ChildStatus
{
    Unprocessed,
    PendingMatch,
    Folded,
    Conflict
}

public enum Direction
{
    inbound,
    outbound
}

public enum UserStatus
{
    confirmed
}

public sealed record Relationship(
    string Type,
    string Name,
    string? Email,
    string? Phone,
    ConfidenceBand ConfidenceBand,
    UserStatus? UserStatus = null,
    string? SourceEmailId = null);

public sealed record CaseAttribute(
    string Type,
    string Value,
    ConfidenceBand ConfidenceBand,
    UserStatus? UserStatus = null,
    string? SourceEmailId = null);

public sealed record PossibleCaseMatch(string EntityId, ConfidenceBand ConfidenceBand);

public sealed record ChildResult(
    string? Name,
    ChildStatus Status,
    string? NhsNumber,
    string? CaseId,
    DateOnly? DateOfBirth,
    string? MatchedCaseId,
    IReadOnlyList<PossibleCaseMatch> PossibleCaseMatches,
    IReadOnlyList<Relationship> Relationships,
    IReadOnlyList<CaseAttribute> Attributes);

public sealed record EmailDocument(
    string Id,
    string ConversationId,
    Direction Direction,
    DateTimeOffset ReceivedDateTime,
    IReadOnlyList<ChildResult> Children,
    EmailProcessingStatus Status = EmailProcessingStatus.Unprocessed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rationale = null);

public sealed record Communication(
    string EmailId,
    Direction Direction,
    DateTimeOffset ReceivedDateTime,
    string? Subject = null,
    string? Summary = null,
    IReadOnlyList<string>? KeyFacts = null,
    string? DraftReply = null);

public sealed record Conflict(
    string EmailId,
    ConfidenceBand ConfidenceBand,
    DateTimeOffset FlaggedAt,
    string? MatchedOn = null);

public sealed record CaseDocument(
    string Id,
    string EntityId,
    string Name,
    string? CaseId,
    string? NhsNumber,
    DateOnly? DateOfBirth,
    IReadOnlyList<Relationship> Relationships,
    IReadOnlyList<CaseAttribute> Attributes,
    IReadOnlyList<Communication> Communications,
    IReadOnlyList<Conflict> Conflicts);

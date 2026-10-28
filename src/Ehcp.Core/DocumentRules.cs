namespace Ehcp.Core;

public static class DocumentRules
{
    public const int EmailTtlSeconds = 30 * 24 * 60 * 60;

    public static int EmailTtl(EmailDocument email) =>
        email.Children.Any(c => c.Status == ChildStatus.Folded) ? -1 : EmailTtlSeconds;

    public static void ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 512 || id.Any(c => char.IsControl(c) || "/\\?#".Contains(c)))
            throw new DocumentValidationException(
                "Identifiers must be nonempty, at most 512 characters, and Cosmos-safe (no /, \\, ?, or #).");
    }

    public static void Validate(EmailDocument document)
    {
        ValidateId(document.Id);
        Required(document.ConversationId, "conversationId");
        if (!Enum.IsDefined(document.Direction) || document.ReceivedDateTime == default)
            throw new DocumentValidationException("A valid direction and receivedDateTime are required.");
        RequireList(document.Children, "children");
        foreach (var child in document.Children)
        {
            if (!Enum.IsDefined(child.Status)) throw new DocumentValidationException("Invalid child status.");
            RequireList(child.PossibleCaseMatches, "possibleCaseMatches");
            ValidateFields(child.Relationships, child.Attributes);
            if ((child.Status == ChildStatus.Folded) != !string.IsNullOrWhiteSpace(child.MatchedCaseId))
                throw new DocumentValidationException("matchedCaseId must be set exactly when status is Folded.");
            if (child.MatchedCaseId is not null) ValidateId(child.MatchedCaseId);
            if (child.Status != ChildStatus.Conflict && child.PossibleCaseMatches.Count != 0)
                throw new DocumentValidationException("Only Conflict results may have possibleCaseMatches.");
            foreach (var match in child.PossibleCaseMatches)
            {
                ValidateId(match.EntityId);
                Band(match.ConfidenceBand);
            }
        }
    }

    public static void Validate(CaseDocument document)
    {
        ValidateId(document.Id);
        if (document.Id != document.EntityId)
            throw new DocumentValidationException(
                "Case id must equal entityId; caseId is the separate Synergy reference.");
        Required(document.Name, "name");
        ValidateFields(document.Relationships, document.Attributes);
        RequireList(document.Communications, "communications");
        RequireList(document.Conflicts, "conflicts");
        foreach (var item in document.Communications)
        {
            ValidateId(item.EmailId);
            if (!Enum.IsDefined(item.Direction) || item.ReceivedDateTime == default)
                throw new DocumentValidationException("Invalid communication direction or receivedDateTime.");
        }

        foreach (var item in document.Conflicts)
        {
            ValidateId(item.EmailId);
            Band(item.ConfidenceBand);
            if (item.FlaggedAt == default) throw new DocumentValidationException("flaggedAt is required.");
        }
    }

    private static void ValidateFields(IReadOnlyList<Relationship> relationships,
        IReadOnlyList<CaseAttribute> attributes)
    {
        RequireList(relationships, "relationships");
        RequireList(attributes, "attributes");
        foreach (var item in relationships)
        {
            Required(item.Type, "relationship.type");
            Required(item.Name, "relationship.name");
            Band(item.ConfidenceBand);
            Status(item.UserStatus);
        }

        foreach (var item in attributes)
        {
            Required(item.Type, "attribute.type");
            Required(item.Value, "attribute.value");
            Band(item.ConfidenceBand);
            Status(item.UserStatus);
        }
    }

    private static void Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DocumentValidationException($"{field} is required.");
    }

    private static void RequireList<T>(IReadOnlyList<T>? values, string field)
    {
        if (values is null || values.Any(v => v is null))
            throw new DocumentValidationException($"{field} must be an array without null entries.");
    }

    private static void Band(ConfidenceBand band)
    {
        if (!Enum.IsDefined(band)) throw new DocumentValidationException("Invalid confidence band.");
    }

    private static void Status(UserStatus? status)
    {
        if (status is not null && !Enum.IsDefined(status.Value))
            throw new DocumentValidationException("Invalid userStatus.");
    }
}

public sealed class DocumentValidationException(string message) : Exception(message);
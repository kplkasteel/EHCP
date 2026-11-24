using Ehcp.Core;

namespace Ehcp.Application;

// Folds an email into an existing case. Existing facts are preserved; only novel facts are appended, each with its source email.
internal static class CaseMerger
{
    public static CaseDocument Merge(
        CaseDocument document,
        ExtractedChild child,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction) =>
        document with
        {
            NhsNumber = document.NhsNumber ?? child.NhsNumber,
            CaseId = document.CaseId ?? child.CaseId,
            DateOfBirth = document.DateOfBirth ?? child.DateOfBirth,
            Communications = MergeCommunications(document.Communications, email, source, extraction),
            Relationships = MergeRelationships(document.Relationships, child.Relationships, email.Id),
            Attributes = MergeAttributes(document.Attributes, child.Attributes, email.Id)
        };

    private static Communication[] MergeCommunications(
        IEnumerable<Communication> existing,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction)
    {
        var communications = existing.ToList();

        if (communications.All(item => item.EmailId != email.Id))
        {
            communications.Add(new Communication(
                email.Id,
                email.Direction,
                email.ReceivedDateTime,
                source.Subject,
                extraction.Summary));
        }

        return communications
            .OrderByDescending(item => item.ReceivedDateTime)
            .ToArray();
    }

    private static List<Relationship> MergeRelationships(
        IEnumerable<Relationship> existing,
        IEnumerable<Relationship> incoming,
        string emailId)
    {
        var relationships = existing.ToList();

        foreach (Relationship item in incoming)
        {
            bool alreadyKnown = relationships.Any(known =>
                known.Type == item.Type
                && known.Name == item.Name
                && known.Email == item.Email
                && known.Phone == item.Phone);

            if (!alreadyKnown)
            {
                relationships.Add(item with { UserStatus = null, SourceEmailId = emailId });
            }
        }

        return relationships;
    }

    private static List<CaseAttribute> MergeAttributes(
        IEnumerable<CaseAttribute> existing,
        IEnumerable<CaseAttribute> incoming,
        string emailId)
    {
        var attributes = existing.ToList();

        foreach (CaseAttribute item in incoming)
        {
            bool alreadyKnown = attributes.Any(known =>
                known.Type == item.Type
                && known.Value == item.Value);

            if (!alreadyKnown)
            {
                attributes.Add(item with { UserStatus = null, SourceEmailId = emailId });
            }
        }

        return attributes;
    }
}

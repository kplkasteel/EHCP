using Ehcp.Core;

namespace Ehcp.Application;

internal static class CaseConflictFactory
{
    public static Conflict Create(
        string emailId,
        ExtractedChild child,
        CaseDocument candidate,
        DateTimeOffset flaggedAt) =>
        new(emailId,
            PossibleMatchScorer.Score(child, candidate).ConfidenceBand,
            flaggedAt,
            MatchedOn(child, candidate));

    // Names are never listed. Only strong identifiers and a differing date of birth are.
    internal static string? MatchedOn(ExtractedChild child, CaseDocument candidate)
    {
        StrongIdentifiers incoming = StrongIdentifiers.From(child);
        StrongIdentifiers existing = StrongIdentifiers.From(candidate);

        var matched = new List<string>();
        var differing = new List<string>();

        Compare("NHS number", incoming.NhsNumbers, existing.NhsNumbers, matched, differing);
        Compare("Synergy ID", incoming.CaseIds, existing.CaseIds, matched, differing);
        Compare("DfE ID", incoming.DfeIds, existing.DfeIds, matched, differing);
        Compare("Mosaic ID", incoming.MosaicIds, existing.MosaicIds, matched, differing);

        if (child.DateOfBirth is DateOnly dateOfBirth
            && candidate.DateOfBirth is DateOnly existingDateOfBirth
            && dateOfBirth != existingDateOfBirth)
        {
            differing.Add("Date of birth");
        }

        var parts = new List<string>();
        if (matched.Count > 0)
            parts.Add(Sentence(matched, "matches", "match"));
        if (differing.Count > 0)
            parts.Add(Sentence(differing, "does not match", "do not match"));

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static void Compare(
        string label,
        IReadOnlyList<string> incoming,
        IReadOnlyList<string> existing,
        List<string> matched,
        List<string> differing)
    {
        if (incoming.Count == 0 || existing.Count == 0)
            return;

        (incoming.Intersect(existing, StringComparer.Ordinal).Any() ? matched : differing)
            .Add(label);
    }

    private static string Sentence(IReadOnlyList<string> labels, string singularVerb, string pluralVerb) =>
        labels.Count == 1
            ? $"{labels[0]} {singularVerb}"
            : $"{string.Join(", ", labels.Take(labels.Count - 1))} and {labels[^1]} {pluralVerb}";
}

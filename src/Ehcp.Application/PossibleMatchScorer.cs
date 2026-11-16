using Ehcp.Core;

namespace Ehcp.Application;

internal static class PossibleMatchScorer
{
    private static readonly WeightedRule[] Rules =
    [
        new("NHS Number", 10, (child, candidate) =>
            Shares(StrongIdentifiers.From(child).NhsNumbers, StrongIdentifiers.From(candidate).NhsNumbers)),
        new("Synergy Number / Synergy ID", 10, (child, candidate) =>
            Shares(StrongIdentifiers.From(child).CaseIds, StrongIdentifiers.From(candidate).CaseIds)),
        new("DfE UPN", 10, (child, candidate) =>
            Shares(StrongIdentifiers.From(child).DfeIds, StrongIdentifiers.From(candidate).DfeIds)),
        new("Mosaic ID", 10, (child, candidate) =>
            Shares(StrongIdentifiers.From(child).MosaicIds, StrongIdentifiers.From(candidate).MosaicIds)),
        new("DOB", 7, (child, candidate) =>
            child.DateOfBirth is not null && child.DateOfBirth == candidate.DateOfBirth),
        new("Full Name", 5, (child, candidate) =>
            Normalize(child.Name) is { Length: > 0 } name && name == Normalize(candidate.Name)),
        new("First Name / Fuzzy Name", 3, (child, candidate) =>
            FirstNamesMatch(child.Name, candidate.Name)),
        new("Initials", 3, (child, candidate) =>
            Initials(child.Name) is { Length: > 0 } initials && initials == Initials(candidate.Name)),
        new("Parent Name / Parents Name", 2, (child, candidate) =>
            Shares(child.Relationships.Select(item => Normalize(item.Name)),
                candidate.Relationships.Select(item => Normalize(item.Name)))),
        new("Parents Email Address", 2, (child, candidate) =>
            Shares(child.Relationships.Select(item => Normalize(item.Email)),
                candidate.Relationships.Select(item => Normalize(item.Email)))),
        new("Address", 5, (child, candidate) => AttributeShared(child, candidate, "address")),
        new("GP", 2, (child, candidate) => AttributeShared(child, candidate, "gp")),
        new("Parent Needs", 2, (child, candidate) => AttributeShared(child, candidate, "parentNeeds")),
        new("Siblings", 2, (child, candidate) => AttributeShared(child, candidate, "siblings")),
    ];

    public static PossibleCaseMatch Score(ExtractedChild child, CaseDocument candidate)
    {
        double score = Rules
            .Where(rule => rule.Matches(child, candidate))
            .Sum(rule => rule.Weight);
        return new PossibleCaseMatch(candidate.EntityId, BandFor(score), score);
    }

    private static ConfidenceBand BandFor(double score) => score switch
    {
        >= 20 => ConfidenceBand.High,
        >= 10 => ConfidenceBand.Medium,
        _ => ConfidenceBand.Low,
    };

    private static bool AttributeShared(ExtractedChild child, CaseDocument candidate, string type) =>
        Shares(AttributeValues(child.Attributes, type), AttributeValues(candidate.Attributes, type));

    private static IEnumerable<string> AttributeValues(IEnumerable<CaseAttribute> attributes, string type) =>
        attributes
            .Where(item => string.Equals(item.Type.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(item => Normalize(item.Value));

    private static bool Shares(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Select(Normalize)
            .Where(value => value.Length > 0)
            .Intersect(second.Select(Normalize).Where(value => value.Length > 0), StringComparer.Ordinal)
            .Any();

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string Initials(string? name) =>
        new((name?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray());

    private static bool FirstNamesMatch(string? childName, string candidateName)
    {
        string? childFirstName = FirstName(childName);
        string? candidateFirstName = FirstName(candidateName);
        return childFirstName is not null
            && candidateFirstName is not null
            && (childFirstName.StartsWith(candidateFirstName, StringComparison.OrdinalIgnoreCase)
                || candidateFirstName.StartsWith(childFirstName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FirstName(string? name) =>
        name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    private sealed record WeightedRule(
        string Name,
        double Weight,
        Func<ExtractedChild, CaseDocument, bool> Matches);
}

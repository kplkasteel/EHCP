using Ehcp.Core;

namespace Ehcp.Application;

internal static class PossibleMatchScorer
{
    private static readonly ScoringRule[] Rules =
    [
        new(ConfidenceBand.Medium, 10.0, (child, candidate) => FirstNamesMatch(child.Name, candidate.Name)),
        new(ConfidenceBand.Low, 7.0, (_, _) => true),
    ];

    public static PossibleCaseMatch Score(ExtractedChild child, CaseDocument candidate)
    {
        ScoringRule rule = Rules.First(item => item.Applies(child, candidate));
        return new PossibleCaseMatch(candidate.EntityId, rule.Band, rule.Score);
    }

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

    private sealed record ScoringRule(
        ConfidenceBand Band,
        double Score,
        Func<ExtractedChild, CaseDocument, bool> Applies);
}

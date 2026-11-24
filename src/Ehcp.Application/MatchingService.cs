using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ehcp.Core;
using Microsoft.Extensions.Logging;

namespace Ehcp.Application;

public sealed class MatchingService(
    ICaseLookup lookup,
    IDocumentStore<CaseDocument> cases,
    ILogger<MatchingService> logger) : IMatchingService
{
    // Total attempts for an optimistic-concurrency write before the failure is surfaced.
    private const int MaxWriteAttempts = 5;

    public async Task<ChildResult> MatchAsync(
        string mailboxId,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction,
        ExtractedChild child,
        CancellationToken ct)
    {
        var request = new MatchRequest(mailboxId, email, source, extraction, child);
        StrongIdentifiers identifiers = StrongIdentifiers.From(child);

        // Strong identifiers take precedence; otherwise evaluate scored evidence and parent corroboration.
        return identifiers.HasAny
            ? await MatchByStrongIdentifiersAsync(request, identifiers, ct)
            : await MatchByWeakEvidenceAsync(request, ct);
    }

    private async Task<ChildResult> MatchByStrongIdentifiersAsync(
        MatchRequest request,
        StrongIdentifiers identifiers,
        CancellationToken ct)
    {
        IReadOnlyList<CaseDocument> candidates =
            await lookup.FindCandidatesAsync(request.MailboxId, request.Child, ct);

        // Multiple candidates, conflicting identifiers or conflicting dates of birth go to Conflict, never to a guessed fold.
        if (RequiresConflictReview(request.Child, identifiers, candidates))
        {
            await FlagConflictsAsync(request, candidates.Select(candidate => candidate.EntityId), ct);
            return Result(request.Child, ChildStatus.Conflict, candidates: candidates);
        }

        if (candidates.Count == 1)
            return await FoldSingleCandidateAsync(request, candidates[0].EntityId, ct);

        // An unmatched strong identifier does not prove that this is a new child.
        // Check corroborated weak evidence before creating a case.
        IReadOnlyList<CaseDocument> weakCandidates =
            await FindWeakCandidatesAsync(request.MailboxId, request.Child, ct);
        ChildResult? weakMatch = await ResolveWeakMatchesAsync(request, weakCandidates, ct);
        if (weakMatch is not null)
            return weakMatch;

        // Creating a new case requires a name and a strong identifier.
        if (string.IsNullOrWhiteSpace(request.Child.Name))
            return Result(request.Child, ChildStatus.PendingMatch);

        return await CreateCaseAsync(request, identifiers, ct);
    }

    private async Task<ChildResult> MatchByWeakEvidenceAsync(MatchRequest request, CancellationToken ct)
    {
        IReadOnlyList<CaseDocument> candidates =
            await FindWeakCandidatesAsync(request.MailboxId, request.Child, ct);

        return await ResolveWeakMatchesAsync(request, candidates, ct)
            ?? Result(request.Child, ChildStatus.PendingMatch,
                possibleMatches: ScoreCandidates(request.Child, candidates));
    }

    private async Task<ChildResult> FoldSingleCandidateAsync(
        MatchRequest request,
        string entityId,
        CancellationToken ct)
    {
        CaseDocument? rejected = await FoldAsync(request, entityId, ct);
        if (rejected is null)
            return Result(request.Child, ChildStatus.Folded, entityId);

        await FlagConflictsAsync(request, [entityId], ct);
        return Result(request.Child, ChildStatus.Conflict, candidates: [rejected]);
    }

    private async Task<ChildResult> CreateCaseAsync(
        MatchRequest request,
        StrongIdentifiers identifiers,
        CancellationToken ct)
    {
        ExtractedChild child = request.Child;
        string entityId = DeterministicEntityId(request.MailboxId, identifiers);

        var created = new CaseDocument(
            entityId,
            entityId,
            child.Name,
            child.CaseId,
            child.NhsNumber,
            child.DateOfBirth,
            [],
            [],
            [],
            []);

        try
        {
            await cases.CreateAsync(request.MailboxId, created, ct);
        }
        catch (DocumentStoreException error)
            when (error.Failure == StoreFailure.Conflict)
        {
            // A previous attempt already created this case.
        }

        // Create opens the case; folding links the email and adds the facts, so a retry finishes on the same case.
        CaseDocument? rejected = await FoldAsync(request, entityId, ct);

        return rejected is null
            ? Result(child, ChildStatus.Folded, entityId)
            : Result(child, ChildStatus.Conflict, candidates: [rejected]);
    }

    private static bool Contradicts(ExtractedChild child, CaseDocument candidate)
    {
        StrongIdentifiers candidateIdentifiers = StrongIdentifiers.From(candidate);
        return candidateIdentifiers.HasConflicts
            || StrongIdentifiers.From(child).Contradicts(candidateIdentifiers)
            || (child.DateOfBirth is not null
                && candidate.DateOfBirth is not null
                && child.DateOfBirth != candidate.DateOfBirth);
    }

    private static ChildResult Result(
        ExtractedChild child,
        ChildStatus status,
        string? entityId = null,
        IReadOnlyList<CaseDocument>? candidates = null,
        IReadOnlyList<PossibleCaseMatch>? possibleMatches = null) =>
        new(
            child.Name,
            status,
            child.NhsNumber,
            child.CaseId,
            child.DateOfBirth,
            entityId,
            possibleMatches ?? (candidates is null ? [] : RankByScore(child, candidates)),
            [
                .. child.Relationships
                    .Select(item => item with { UserStatus = null })
            ],
            [
                .. child.Attributes
                    .Select(item => item with { UserStatus = null })
            ]);

    private static PossibleCaseMatch[] ScoreCandidates(
        ExtractedChild child, IReadOnlyList<CaseDocument> candidates) =>
        RankByScore(child, candidates)
            .Where(match => match.Score > 0)
            .ToArray();

    private static PossibleCaseMatch[] RankByScore(
        ExtractedChild child, IEnumerable<CaseDocument> candidates) =>
        candidates
            .Select(candidate => PossibleMatchScorer.Score(child, candidate))
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.EntityId, StringComparer.Ordinal)
            .ToArray();

    private async Task<IReadOnlyList<CaseDocument>> FindWeakCandidatesAsync(
        string mailboxId, ExtractedChild child, CancellationToken ct)
    {
        if (child.DateOfBirth is null && string.IsNullOrWhiteSpace(child.Name))
            return [];

        var tasks = new List<Task<IReadOnlyList<CaseDocument>>>();

        if (child.DateOfBirth is DateOnly dob)
        {
            tasks.Add(lookup.FindDateOfBirthCandidatesAsync(mailboxId, dob, ct));
        }

        if (!string.IsNullOrWhiteSpace(child.Name))
        {
            tasks.Add(lookup.FindNameCandidatesAsync(mailboxId, child.Name, ct));
        }

        var candidates = (await Task.WhenAll(tasks))
            .SelectMany(x => x)
            .DistinctBy(x => x.EntityId)
            .ToArray();

        logger.LogInformation(
            "Found {CandidateCount} possible matches for {MailboxId} child {ChildName} ({DateOfBirth})",
            candidates.Length, mailboxId, child.Name, child.DateOfBirth);

        return candidates;
    }

    // Records one conflict per matched case. A retry for the same email leaves the existing entry in place.
    private async Task FlagConflictsAsync(
        MatchRequest request,
        IEnumerable<string> entityIds,
        CancellationToken ct)
    {
        foreach (string entityId in entityIds)
        {
            await UpdateCaseAsync(request.MailboxId, entityId,
                document => FlagConflict(document, request), ct);
        }
    }

    private static CaseEdit FlagConflict(CaseDocument document, MatchRequest request)
    {
        if (document.Conflicts.Any(item => item.EmailId == request.Email.Id))
            return CaseEdit.Unchanged;

        var conflict = CaseConflictFactory.Create(
            request.Email.Id, request.Child, document, DateTimeOffset.UtcNow);

        return CaseEdit.Write(document with
        {
            Conflicts = [.. document.Conflicts, conflict]
        });
    }

    // Returns the existing case, unchanged, when it contradicts the child.
    private Task<CaseDocument?> FoldAsync(MatchRequest request, string entityId, CancellationToken ct)
    {
        return UpdateCaseAsync(request.MailboxId, entityId, document =>
        {
            if (Contradicts(request.Child, document))
                return CaseEdit.Reject(document);

            return CaseEdit.Write(CaseMerger.Merge(
                document, request.Child, request.Email, request.Source, request.Extraction));
        }, ct);
    }

    // Reads, edits and replaces a case under its ETag, reloading and retrying when another write wins the race.
    // Returns the existing case when the edit rejected it, otherwise null.
    private async Task<CaseDocument?> UpdateCaseAsync(
        string mailboxId,
        string entityId,
        Func<CaseDocument, CaseEdit> edit,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            StoredDocument<CaseDocument> stored =
                await cases.ReadAsync(mailboxId, entityId, ct);

            CaseEdit change = edit(stored.Document);

            if (change.Updated is not CaseDocument updated)
                return change.Rejected;

            DocumentRules.Validate(updated);

            try
            {
                await cases.ReplaceAsync(mailboxId, entityId, updated, stored.ETag, ct);
                return null;
            }
            catch (DocumentStoreException error)
                when (error.Failure == StoreFailure.PreconditionFailed
                    && attempt < MaxWriteAttempts)
            {
                // Another write won the race; reload and try again.
            }
        }
    }

    private async Task<ChildResult?> ResolveWeakMatchesAsync(
        MatchRequest request,
        IReadOnlyList<CaseDocument> candidates,
        CancellationToken ct)
    {
        ExtractedChild child = request.Child;

        // A score alone never folds a case; eligibility is decided by IsCorroboratedWeakMatch.
        var eligible = candidates
            .Select(candidate => (Candidate: candidate,
                Match: PossibleMatchScorer.Score(child, candidate)))
            .Where(item => IsCorroboratedWeakMatch(child, item.Candidate, item.Match))
            .OrderByDescending(item => item.Match.Score)
            .ThenBy(item => item.Candidate.EntityId, StringComparer.Ordinal)
            .ToArray();

        if (eligible.Length == 0)
            return null;

        // Never resolve competing plausible cases by entity ID or score alone.
        // Contradictions take precedence over folding; retain their scored evidence.
        if (eligible.Length > 1 || eligible.Any(item => Contradicts(child, item.Candidate)))
            return Result(child, ChildStatus.Conflict,
                possibleMatches: eligible.Select(item => item.Match).ToArray());

        string entityId = eligible[0].Candidate.EntityId;
        CaseDocument? rejected = await FoldAsync(request, entityId, ct);

        return rejected is null
            ? Result(child, ChildStatus.Folded, entityId)
            : Result(child, ChildStatus.Conflict, candidates: [rejected]);
    }

    // A Medium or High score is enough on its own. Lower scores need a matching parent
    // plus either name evidence or an exact date of birth.
    private static bool IsCorroboratedWeakMatch(
        ExtractedChild child,
        CaseDocument candidate,
        PossibleCaseMatch match) =>
        match.ConfidenceBand is ConfidenceBand.Medium or ConfidenceBand.High
        || (PossibleMatchScorer.HasParentMatch(child, candidate)
            && (PossibleMatchScorer.NameScore(child.Name, candidate.Name) > 0
                || (child.DateOfBirth is not null && child.DateOfBirth == candidate.DateOfBirth)));

    private static bool RequiresConflictReview(
        ExtractedChild child,
        StrongIdentifiers identifiers,
        IReadOnlyList<CaseDocument> candidates) =>
        identifiers.HasConflicts
        || candidates.Count > 1
        || candidates.Any(candidate => Contradicts(child, candidate));

    // Deterministic IDs make retries converge even after a case write succeeds but the final email write fails.
    private static string DeterministicEntityId(string mailboxId, StrongIdentifiers identifiers)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { mailboxId, PrimaryIdentity(identifiers) })));

        return "auto-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // This priority order is part of the ID contract: changing it would give existing children new case IDs.
    private static string PrimaryIdentity(StrongIdentifiers identifiers) =>
        identifiers.NhsNumbers.Count > 0 ? "nhs:" + identifiers.NhsNumbers[0]
        : identifiers.CaseIds.Count > 0 ? "case:" + identifiers.CaseIds[0]
        : identifiers.DfeIds.Count > 0 ? "dfe:" + identifiers.DfeIds[0]
        : "mosaic:" + identifiers.MosaicIds[0];

    private sealed record MatchRequest(
        string MailboxId,
        EmailDocument Email,
        GraphEmail Source,
        EmailExtraction Extraction,
        ExtractedChild Child);

    // A document to write, the existing case that rejected the edit, or no change.
    private readonly record struct CaseEdit(CaseDocument? Updated, CaseDocument? Rejected)
    {
        public static CaseEdit Write(CaseDocument updated) => new(updated, null);

        public static CaseEdit Reject(CaseDocument existing) => new(null, existing);

        public static CaseEdit Unchanged => new(null, null);
    }
}
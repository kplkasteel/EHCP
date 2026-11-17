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
    public async Task<ChildResult> MatchAsync(
        string mailboxId,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction,
        ExtractedChild child, //JL COMMENT/QUESTION: Can we prove out this pattern by adding a second module which is for debt complaints. i.e. it extracts a 'Resident' instead of a child. Doesn't need to do anything complex, just to prove the processing pattern can replicate across different use cases and makes sure our code is able to be extended easily (before we get further).
        //PK Answer: Yes, I'd start small and build on it. I'd take one sample debt complaint
        // email and use a mock extractor to return the resident's name and complaint reference.
        // That gives us a simple way to check which parts of the email processing we can reuse
        // and where we've assumed we're always dealing with a child. We can build on that
        // once we've proved the basic flow works.
        CancellationToken ct)
    {
        // Name and DOB alone do not authorize automatic matching or case creation.
        StrongIdentifiers identifiers = StrongIdentifiers.From(child);
        if (!identifiers.HasAny)
        {
            return Result(child, ChildStatus.PendingMatch,
                possibleMatches: await FindPossibleMatchesAsync(mailboxId, child, ct));
        }

        IReadOnlyList<CaseDocument> candidates =
            await lookup.FindCandidatesAsync(mailboxId, child, ct);

        if (identifiers.HasConflicts || candidates.Count > 1
            || candidates.Any(candidate => Contradicts(child, candidate))) //JL COMMENT/QUESTION: Not sure if this is correct, but we can keep an eye on it when we get to writing scenarios for conflicts.
            //PK Answer: Multiple candidates or conflicting identifiers or dates of birth go to Conflict. I'd review a same-NHS-number, different-DOB example with Jamie.
        {
            return Result(child, ChildStatus.Conflict, candidates: candidates);
        }

        if (candidates.Count == 1)
        {
            CaseDocument? conflict = await FoldAsync(
                mailboxId, candidates[0].EntityId,
                email, source, extraction, child, ct);

            return conflict is null
                ? Result(child, ChildStatus.Folded, candidates[0].EntityId)
                : Result(child, ChildStatus.Conflict, candidates: [conflict]);
        }

        // Creating a new case also requires a name. //JL COMMENT/QUESTION: Requires an ID + NAME, but not DOB. Let's write a test for this.
        //PK Answer: With no existing match, a name plus a strong identifier is enough to open a case. DOB is optional.
        if (string.IsNullOrWhiteSpace(child.Name))
        {
            return Result(child, ChildStatus.PendingMatch);
        }

        // Deterministic IDs make retries converge even after a case write succeeds
        // but the final email write fails.
        string identity = identifiers.NhsNumbers.Count > 0 ? "nhs:" + identifiers.NhsNumbers[0]
            : identifiers.CaseIds.Count > 0 ? "case:" + identifiers.CaseIds[0]
            : identifiers.DfeIds.Count > 0 ? "dfe:" + identifiers.DfeIds[0]
            : "mosaic:" + identifiers.MosaicIds[0];

        string entityId = "auto-" + Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new[] { mailboxId, identity }))))
            .ToLowerInvariant();

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
            await cases.CreateAsync(mailboxId, created, ct);
        }
        catch (DocumentStoreException error)
            when (error.Failure == StoreFailure.Conflict)
        {
            // A previous attempt may already have created this case.
        }
        //JL COMMENT/QUESTION: What is fold doing here that create isn't doing above in the try catch?
        //PK Answer: Create opens the case. Fold links the email and adds the extracted facts. On a retry it can finish that work on the same case.
        CaseDocument? createdConflict = await FoldAsync(
            mailboxId, entityId, email, source, extraction, child, ct);

        return createdConflict is null
            ? Result(child, ChildStatus.Folded, entityId)
            : Result(child, ChildStatus.Conflict, candidates: [createdConflict]);
    }

    private async Task<CaseDocument?> FoldAsync(
        string mailboxId,
        string entityId,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction,
        ExtractedChild child,
        CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            StoredDocument<CaseDocument> stored =
                await cases.ReadAsync(mailboxId, entityId, ct);

            CaseDocument document = stored.Document;

            if (Contradicts(child, document))
            {
                return document;
            }

            List<Communication> communications = [.. document.Communications];

            if (communications.All(item => item.EmailId != email.Id))
            {
                communications.Add(new Communication(
                    email.Id,
                    email.Direction,
                    email.ReceivedDateTime,
                    source.Subject,
                    extraction.Summary));
            }

            // Preserve existing facts; append novel facts with provenance.
            var relationships = document.Relationships.ToList();

            foreach (Relationship item in child.Relationships)
            {
                if (!relationships.Any(existing =>
                    existing.Type == item.Type
                    && existing.Name == item.Name
                    && existing.Email == item.Email
                    && existing.Phone == item.Phone))
                {
                    relationships.Add(item with
                    {
                        UserStatus = null,
                        SourceEmailId = email.Id
                    });
                }
            }

            List<CaseAttribute> attributes = [.. document.Attributes];

            foreach (CaseAttribute item in child.Attributes)
            {
                if (!attributes.Any(existing =>
                    existing.Type == item.Type
                    && existing.Value == item.Value))
                {
                    attributes.Add(item with
                    {
                        UserStatus = null,
                        SourceEmailId = email.Id
                    });
                }
            }

            CaseDocument updated = document with
            {
                NhsNumber = document.NhsNumber ?? child.NhsNumber,
                CaseId = document.CaseId ?? child.CaseId,
                DateOfBirth = document.DateOfBirth ?? child.DateOfBirth,
                Communications = communications
                    .OrderByDescending(item => item.ReceivedDateTime)
                    .ToArray(),
                Relationships = relationships,
                Attributes = attributes
            };

            DocumentRules.Validate(updated);

            try
            {
                await cases.ReplaceAsync(
                    mailboxId, entityId, updated, stored.ETag, ct);

                return null;
            }
            catch (DocumentStoreException error)
                when (error.Failure == StoreFailure.PreconditionFailed
                    && attempt < 4)
            {
                // Reload and retry when another operation updates the case.
            }
        }
    }

    private async Task<IReadOnlyList<PossibleCaseMatch>> FindPossibleMatchesAsync(
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
            $"Found {candidates.Length} possible matches for {mailboxId} child {child.Name} ({child.DateOfBirth})");

        return candidates
            .Select(candidate => PossibleMatchScorer.Score(child, candidate))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.EntityId, StringComparer.Ordinal)
            .ToArray();
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
            possibleMatches ?? candidates?
                .Select(candidate => PossibleMatchScorer.Score(child, candidate))
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.EntityId, StringComparer.Ordinal)
                .ToArray() ?? [],
            [
                .. child.Relationships
                    .Select(item => item with { UserStatus = null })
            ],
            [
                .. child.Attributes
                    .Select(item => item with { UserStatus = null })
            ]);
}

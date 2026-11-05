using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Application;

public sealed class MatchingService(
    ICaseLookup lookup,
    IDocumentStore<CaseDocument> cases) : IMatchingService
{
    public async Task<ChildResult> MatchAsync(
        string mailboxId,
        EmailDocument email,
        GraphEmail source,
        EmailExtraction extraction,
        ExtractedChild child, //JL COMMENT/QUESTION: Can we prove out this pattern by adding a second module which is for debt complaints. i.e. it extracts a 'Resident' instead of a child. Doesn't need to do anything complex, just to prove the processing pattern can replicate across different use cases and makes sure our code is able to be extended easlily (before we get further).
        CancellationToken ct)
    {
        // Name and DOB alone do not authorize automatic matching or case creation.
        if (string.IsNullOrWhiteSpace(child.NhsNumber)
            && string.IsNullOrWhiteSpace(child.CaseId))
        {
            return Result(child, ChildStatus.PendingMatch);
        }

        IReadOnlyList<CaseDocument> candidates =
            await lookup.FindCandidatesAsync(mailboxId, child, ct);

        if (candidates.Count > 1 
            || candidates.Any(candidate => Contradicts(child, candidate))) //JL COMMENT/QUESTION: Not sure this is correct but we can keep an eye on it when we get to writing scenarios for conflicts.
        {
            return Result(child, ChildStatus.Conflict, candidates: candidates);
        }

        if (candidates.Count == 1)
        {
            await FoldAsync(
                mailboxId, candidates[0].EntityId,
                email, source, extraction, child, ct);

            return Result(child, ChildStatus.Folded, candidates[0].EntityId);
        }

        // Creating a new case also requires a name and date of birth. //JL COMMENT/QUESTION: Requires a ID + NAME, but not DOB. Lets write a test for this.
        if (string.IsNullOrWhiteSpace(child.Name) || child.DateOfBirth is null)
        {
            return Result(child, ChildStatus.PendingMatch);
        }

        // Deterministic IDs make retries converge even after a case write succeeds
        // but the final email write fails.
        string identity = !string.IsNullOrWhiteSpace(child.NhsNumber)
            ? "nhs:" + Normalize(child.NhsNumber)
            : "case:" + Normalize(child.CaseId!);

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
        //JL COMMENT/QUESTION: What is fold doing here that create isnt doing above in the try catch?
        await FoldAsync(
            mailboxId, entityId, email, source, extraction, child, ct);

        return Result(child, ChildStatus.Folded, entityId);
    }

    private async Task FoldAsync(
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
                throw new DocumentValidationException(
                    "Case identity changed during matching; retry for review.");
            }

            var communications = document.Communications.ToList();

            if (!communications.Any(item => item.EmailId == email.Id))
            {
                communications.Add(new(
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

            var attributes = document.Attributes.ToList();

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

                return;
            }
            catch (DocumentStoreException error)
                when (error.Failure == StoreFailure.PreconditionFailed
                    && attempt < 4)
            {
                // Reload and retry when another operation updates the case.
            }
        }
    }

    private static bool Contradicts(
        ExtractedChild child,
        CaseDocument candidate) =>
        Different(child.NhsNumber, candidate.NhsNumber)
        || Different(child.CaseId, candidate.CaseId)
        || (child.DateOfBirth is not null
            && candidate.DateOfBirth is not null
            && child.DateOfBirth != candidate.DateOfBirth);

    private static bool Different(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && Normalize(first) != Normalize(second);

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();

    private static ChildResult Result(
        ExtractedChild child,
        ChildStatus status,
        string? entityId = null,
        IReadOnlyList<CaseDocument>? candidates = null) =>
        new(
            child.Name,
            status,
            child.NhsNumber,
            child.CaseId,
            child.DateOfBirth,
            entityId,
            candidates?
                .Select(candidate => new PossibleCaseMatch(
                    candidate.EntityId, ConfidenceBand.High))
                .ToArray() ?? [],
            child.Relationships
                .Select(item => item with { UserStatus = null })
                .ToArray(),
            child.Attributes
                .Select(item => item with { UserStatus = null })
                .ToArray());
}
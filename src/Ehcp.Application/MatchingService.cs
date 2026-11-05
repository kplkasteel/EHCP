using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Application;

public sealed class MatchingService(
    ICaseLookup lookup, IDocumentStore<CaseDocument> cases) : IMatchingService
{
    public async Task<ChildResult> MatchAsync(string mailboxId, EmailDocument email, GraphEmail source,
        EmailExtraction extraction, ExtractedChild child, CancellationToken ct)
    {
        IReadOnlyList<CaseDocument> candidates = await lookup.FindCandidatesAsync(mailboxId, child, ct);
        if (candidates.Count > 1 || candidates.Any(candidate => Contradicts(child, candidate)))
            return Result(child, ChildStatus.Conflict, candidates: candidates);
        if (candidates.Count == 1)
        {
            await FoldAsync(mailboxId, candidates[0].EntityId, email, source, extraction, child, ct);
            return Result(child, ChildStatus.Folded, candidates[0].EntityId);
        }
        // A name and DOB alone do not authorize automatic case creation.
        // Existing candidates are still matched above; a new case needs a strong identifier too.
        if (string.IsNullOrWhiteSpace(child.Name) || child.DateOfBirth is null
            || (string.IsNullOrWhiteSpace(child.NhsNumber) && string.IsNullOrWhiteSpace(child.CaseId)))
            return Result(child, ChildStatus.PendingMatch);

        // Deterministic IDs make retries converge even after a case write succeeded
        // but the final email write failed. Mailboxes still use separate containers.
        string identity = !string.IsNullOrWhiteSpace(child.NhsNumber) ? "nhs:" + Normalize(child.NhsNumber)
            : !string.IsNullOrWhiteSpace(child.CaseId) ? "case:" + Normalize(child.CaseId)
            : "name-dob:" + Normalize(child.Name) + ":" + child.DateOfBirth.Value.ToString("yyyy-MM-dd");
        string entityId = "auto-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { mailboxId, identity })))).ToLowerInvariant();
        var created = new CaseDocument(entityId, entityId, child.Name, child.CaseId, child.NhsNumber,
            child.DateOfBirth, [], [], [], []);
        try { await cases.CreateAsync(mailboxId, created, ct); }
        catch (DocumentStoreException error) when (error.Failure == StoreFailure.Conflict) { }
        await FoldAsync(mailboxId, entityId, email, source, extraction, child, ct);
        return Result(child, ChildStatus.Folded, entityId);
    }

    private async Task FoldAsync(string mailboxId, string entityId, EmailDocument email,
        GraphEmail source, EmailExtraction extraction, ExtractedChild child, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            StoredDocument<CaseDocument> stored = await cases.ReadAsync(mailboxId, entityId, ct);
            CaseDocument document = stored.Document;
            if (Contradicts(child, document))
                throw new DocumentValidationException("Case identity changed during matching; retry for review.");
            var communications = document.Communications.ToList();
            if (!communications.Any(item => item.EmailId == email.Id))
                communications.Add(new(email.Id, email.Direction, email.ReceivedDateTime, source.Subject, extraction.Summary));
            // Preserve existing (including officer-confirmed) facts; append novel facts with provenance.
            var relationships = document.Relationships.ToList();
            foreach (Relationship item in child.Relationships)
                if (!relationships.Any(existing => existing.Type == item.Type && existing.Name == item.Name
                    && existing.Email == item.Email && existing.Phone == item.Phone))
                    relationships.Add(item with { UserStatus = null, SourceEmailId = email.Id });
            var attributes = document.Attributes.ToList();
            foreach (CaseAttribute item in child.Attributes)
                if (!attributes.Any(existing => existing.Type == item.Type && existing.Value == item.Value))
                    attributes.Add(item with { UserStatus = null, SourceEmailId = email.Id });
            CaseDocument updated = document with
            {
                NhsNumber = document.NhsNumber ?? child.NhsNumber,
                CaseId = document.CaseId ?? child.CaseId,
                DateOfBirth = document.DateOfBirth ?? child.DateOfBirth,
                Communications = communications.OrderByDescending(item => item.ReceivedDateTime).ToArray(),
                Relationships = relationships, Attributes = attributes
            };
            DocumentRules.Validate(updated);
            try
            {
                await cases.ReplaceAsync(mailboxId, entityId, updated, stored.ETag, ct);
                return;
            }
            catch (DocumentStoreException error) when (error.Failure == StoreFailure.PreconditionFailed && attempt < 4) { }
        }
    }

    private static bool Contradicts(ExtractedChild child, CaseDocument candidate) =>
        Different(child.NhsNumber, candidate.NhsNumber) || Different(child.CaseId, candidate.CaseId)
        || child.DateOfBirth is not null && candidate.DateOfBirth is not null && child.DateOfBirth != candidate.DateOfBirth;

    private static bool Different(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) && Normalize(first) != Normalize(second);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static ChildResult Result(ExtractedChild child, ChildStatus status, string? entityId = null,
        IReadOnlyList<CaseDocument>? candidates = null) =>
        new(child.Name, status, child.NhsNumber, child.CaseId, child.DateOfBirth, entityId,
            candidates?.Select(candidate => new PossibleCaseMatch(candidate.EntityId, ConfidenceBand.High)).ToArray() ?? [],
            child.Relationships.Select(item => item with { UserStatus = null }).ToArray(),
            child.Attributes.Select(item => item with { UserStatus = null }).ToArray());
}


using System.Globalization;
using Ehcp.Application;
using Ehcp.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ehcp.Tests;

// No emulator or HTTP service is required for these unit tests; the Reqnroll
// outlines exercise the actual Cosmos lookup, queue and Function hosts.
public sealed class FuzzyMatchingTests
{
    private const string Mailbox = "ehcp-team-shared-1";

    private static CaseDocument[] Cases() =>
    [
        new("spec-case-fred-bloggs", "spec-case-fred-bloggs", "Fred Bloggs", "S-1234567",
            "123 456 7890", new DateOnly(2020, 3, 1), [],
            [
                new("dfeId", "D-20001", ConfidenceBand.High),
                new("mosaicId", "M-10001", ConfidenceBand.High),
                new("synergyId", "S-1234567", ConfidenceBand.High)
            ], [], []),
        new("spec-case-jones-smith", "spec-case-jones-smith", "Jones Smith", "S-7654321",
            "456 789 0123", new DateOnly(2020, 3, 1), [],
            [
                new("dfeId", "D-20002", ConfidenceBand.High),
                new("mosaicId", "M-10002", ConfidenceBand.High),
                new("synergyId", "S-7654321", ConfidenceBand.High)
            ], [], [])
    ];

    // The expected values are intentional constants, not calculated using the
    // production scoring class. This catches accidental changes to its weights.
    [Theory]
    [InlineData("Freddy", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Fredd", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Fred", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Frederick", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Jones", "2020-03-01", "spec-case-jones-smith:Medium:10;spec-case-fred-bloggs:Low:7", "spec-case-jones-smith")]
    [InlineData("Martha", "2020-03-01", "spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7", null)]
    [InlineData(null, "2020-03-01", "spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7", null)]
    [InlineData("F. Bloggs", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("J. Smith", "2020-03-01", "spec-case-jones-smith:Medium:10;spec-case-fred-bloggs:Low:7", "spec-case-jones-smith")]
    [InlineData("Freddy", null, "spec-case-fred-bloggs:Low:3", null)]
    [InlineData("Fredd", null, "spec-case-fred-bloggs:Low:3", null)]
    [InlineData("Jones", null, "spec-case-jones-smith:Low:3", null)]
    [InlineData("Fred Bloggs", null, "spec-case-fred-bloggs:Low:5", null)]
    [InlineData("Jones Smith", null, "spec-case-jones-smith:Low:5", null)]
    [InlineData("Fred Bloggs", "2020-03-01", "spec-case-fred-bloggs:Medium:12;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Jones Smith", "2020-03-01", "spec-case-jones-smith:Medium:12;spec-case-fred-bloggs:Low:7", "spec-case-jones-smith")]
    // Case-insensitive full-name evidence is still exactly 5, never 5+3+3.
    [InlineData("fReD bLoGgS", null, "spec-case-fred-bloggs:Low:5", null)]
    [InlineData("fReD bLoGgS", "2020-03-01", "spec-case-fred-bloggs:Medium:12;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    // Matching just the first name is fuzzy evidence, not an exact full-name match.
    [InlineData("Fred Harper", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData("Freddy Bloggs", "2020-03-01", "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7", "spec-case-fred-bloggs")]
    [InlineData(null, null, "", null)]
    [InlineData("Freddy", "2021-03-01", "spec-case-fred-bloggs:Low:3", null)]
    public async Task FuzzyEvidenceHasExpectedScoresAndMatchingDecision(
        string? name, string? dob, string expected, string? expectedCaseId)
    {
        // Check the scoring independently: folded responses intentionally omit candidates.
        DateOnly? parsedDob = dob is null ? null : DateOnly.ParseExact(dob, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var child = new ExtractedChild(name, null, null, parsedDob, [], []);
        var scores = Cases()
            .Select(candidate => PossibleMatchScorer.Score(child, candidate))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.EntityId, StringComparer.Ordinal);
        Assert.Equal(expected, Matches(scores));

        ChildResult result = await MatchAsync(name, dob);
        if (expectedCaseId is not null)
        {
            Assert.Equal(ChildStatus.Folded, result.Status);
            Assert.Equal(expectedCaseId, result.MatchedCaseId);
            Assert.Empty(result.PossibleCaseMatches);
        }
        else
        {
            Assert.Equal(ChildStatus.PendingMatch, result.Status);
            Assert.Null(result.MatchedCaseId);
            Assert.Equal(expected, Matches(result));
        }
    }

    // Strong identifiers score 10 independently. Contradictory dates of birth
    // and cross-case strong IDs must return Conflict, not auto-fold.
    [Theory]
    [InlineData("Alice", "2021-03-01", "123 456 7890", null, null, null, null, "spec-case-fred-bloggs:Medium:10")]
    [InlineData("Alice", "2021-03-01", null, null, "D-20001", null, null, "spec-case-fred-bloggs:Medium:10")]
    [InlineData("Alice", "2021-03-01", null, null, null, "M-10001", null, "spec-case-fred-bloggs:Medium:10")]
    [InlineData("Alice", "2021-03-01", null, null, null, null, "S-1234567", "spec-case-fred-bloggs:Medium:10")]
    [InlineData("Alice", "2021-03-01", null, "S-1234567", null, null, null, "spec-case-fred-bloggs:Medium:10")]
    [InlineData("Alice", null, "123 456 7890", null, "D-20002", null, null, "spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Medium:10")]
    [InlineData("Alice", "2021-03-01", "123 456 7890", null, "D-20001", null, null, "spec-case-fred-bloggs:High:20")]
    [InlineData("Alice", "2020-03-01", "123 456 7890", null, "D-20002", null, null, "spec-case-fred-bloggs:Medium:17;spec-case-jones-smith:Medium:17")]
    // Name and DOB remain independent from strong IDs, but the name group never stacks.
    [InlineData("Fred Bloggs", "2021-03-01", "123 456 7890", null, null, null, null, "spec-case-fred-bloggs:Medium:15")]
    [InlineData("Fred Bloggs", "2020-03-01", "123 456 7890", null, "D-20002", null, null, "spec-case-fred-bloggs:High:22;spec-case-jones-smith:Medium:17")]
    public async Task ConflictingStrongIdentifiersProduceScoredMatches(
        string name, string? dob, string? nhs, string? caseId,
        string? dfe, string? mosaic, string? synergy, string expected)
    {
        ChildResult result = await MatchAsync(name, dob, nhs, caseId, dfe, mosaic, synergy);
        Assert.Equal(ChildStatus.Conflict, result.Status);
        Assert.Null(result.MatchedCaseId);
        Assert.Equal(expected, Matches(result));
    }

    [Theory]
    [InlineData("Fred Bloggs", "2020-03-01", "123 456 7890", null, null, null, null, "spec-case-fred-bloggs")]
    [InlineData("Fred Bloggs", "2020-03-01", null, null, "D-20001", null, null, "spec-case-fred-bloggs")]
    [InlineData("Fred Bloggs", "2020-03-01", null, null, null, "M-10001", null, "spec-case-fred-bloggs")]
    [InlineData("Fred Bloggs", "2020-03-01", null, null, null, null, "S-1234567", "spec-case-fred-bloggs")]
    [InlineData("Fred Bloggs", "2020-03-01", null, "S-1234567", null, null, null, "spec-case-fred-bloggs")]
    [InlineData("Jones Smith", null, "456 789 0123", null, null, null, null, "spec-case-jones-smith")]
    [InlineData("Jones Smith", null, null, null, "D-20002", null, null, "spec-case-jones-smith")]
    public async Task MatchingStrongIdentifierFoldsTheExistingCase(
        string name, string? dob, string? nhs, string? caseId,
        string? dfe, string? mosaic, string? synergy, string expectedCaseId)
    {
        ChildResult result = await MatchAsync(name, dob, nhs, caseId, dfe, mosaic, synergy);
        Assert.Equal(ChildStatus.Folded, result.Status);
        Assert.Equal(expectedCaseId, result.MatchedCaseId);
        Assert.Empty(result.PossibleCaseMatches);
    }

    [Fact]
    public async Task ConflictingStrongIdentifierIsRecordedOnceOnTheMatchedCase()
    {
        CaseDocument[] documents = Cases();
        var store = new InMemoryCaseStore(documents);
        var matcher = new MatchingService(new FixtureLookup(documents), store, NullLogger<MatchingService>.Instance);
        var child = new ExtractedChild("Fred Bloggs", "999 999 9999", null, new DateOnly(2020, 3, 1), [],
            [new("dfeId", "D-20001", ConfidenceBand.High)]);
        var email = new EmailDocument("spec-email", "spec-thread", Direction.inbound,
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), []);
        var graph = new GraphEmail("graph-id", "spec-thread", Direction.inbound,
            email.ReceivedDateTime, "test", "test");

        ChildResult first = await matcher.MatchAsync(Mailbox, email, graph, new EmailExtraction([child]), child, default);
        ChildResult retry = await matcher.MatchAsync(Mailbox, email, graph, new EmailExtraction([child]), child, default);

        Assert.Equal(ChildStatus.Conflict, first.Status);
        Assert.Equal(ChildStatus.Conflict, retry.Status);
        CaseDocument flagged = (await store.ReadAsync(Mailbox, "spec-case-fred-bloggs", default)).Document;
        Conflict conflict = Assert.Single(flagged.Conflicts);
        Assert.Equal("spec-email", conflict.EmailId);
        Assert.Equal(ConfidenceBand.High, conflict.ConfidenceBand);
        Assert.Equal("DfE ID matches; NHS number does not match", conflict.MatchedOn);
        Assert.NotEqual(default, conflict.FlaggedAt);
        Assert.Empty((await store.ReadAsync(Mailbox, "spec-case-jones-smith", default)).Document.Conflicts);
    }

    private static async Task<ChildResult> MatchAsync(
        string? name, string? dob, string? nhs = null, string? caseId = null,
        string? dfe = null, string? mosaic = null, string? synergy = null)
    {
        var attributes = new List<CaseAttribute>();
        if (dfe is not null) attributes.Add(new("dfeId", dfe, ConfidenceBand.High));
        if (mosaic is not null) attributes.Add(new("mosaicId", mosaic, ConfidenceBand.High));
        if (synergy is not null) attributes.Add(new("synergyId", synergy, ConfidenceBand.High));
        DateOnly? parsedDob = dob is null ? null : DateOnly.ParseExact(dob, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var child = new ExtractedChild(name, nhs, caseId, parsedDob, [], attributes);
        var documents = Cases();
        var lookup = new FixtureLookup(documents);
        var store = new InMemoryCaseStore(documents);
        var matcher = new MatchingService(lookup, store, NullLogger<MatchingService>.Instance);
        var email = new EmailDocument("spec-email", "spec-thread", Direction.inbound,
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), []);
        var graph = new GraphEmail("graph-id", "spec-thread", Direction.inbound,
            email.ReceivedDateTime, "test", "test");
        return await matcher.MatchAsync(Mailbox, email, graph, new EmailExtraction([child]), child, default);
    }

    private static string Matches(ChildResult result) => Matches(result.PossibleCaseMatches);

    private static string Matches(IEnumerable<PossibleCaseMatch> matches) => string.Join(";", matches.Select(match =>
        $"{match.EntityId}:{match.ConfidenceBand}:{match.Score?.ToString("0", CultureInfo.InvariantCulture)}"));

    private sealed class FixtureLookup(CaseDocument[] cases) : ICaseLookup
    {
        public Task<IReadOnlyList<CaseDocument>> FindCandidatesAsync(
            string mailboxId, ExtractedChild child, CancellationToken ct)
        {
            StrongIdentifiers source = StrongIdentifiers.From(child);
            return Task.FromResult<IReadOnlyList<CaseDocument>>(cases.Where(candidate =>
            {
                StrongIdentifiers target = StrongIdentifiers.From(candidate);
                return Shared(source.NhsNumbers, target.NhsNumbers)
                    || Shared(source.SynergyId, target.SynergyId)
                    || Shared(source.DfeIds, target.DfeIds)
                    || Shared(source.MosaicIds, target.MosaicIds)
                    || (child.DateOfBirth != null && child.DateOfBirth == candidate.DateOfBirth
                        && string.Equals(child.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
            }).ToArray());
        }

        public Task<IReadOnlyList<CaseDocument>> FindDateOfBirthCandidatesAsync(
            string mailboxId, DateOnly dateOfBirth, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CaseDocument>>(cases.Where(c => c.DateOfBirth == dateOfBirth).ToArray());

        public Task<IReadOnlyList<CaseDocument>> FindNameCandidatesAsync(
            string mailboxId, string name, CancellationToken ct)
        {
            string normalized = name.Trim().ToUpperInvariant();
            string firstName = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            string prefix = firstName[..Math.Min(3, firstName.Length)];
            return Task.FromResult<IReadOnlyList<CaseDocument>>(cases.Where(candidate =>
                candidate.Name.ToUpperInvariant() == normalized
                || candidate.Name.ToUpperInvariant().StartsWith(prefix, StringComparison.Ordinal)).ToArray());
        }

        private static bool Shared(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
            first.Intersect(second, StringComparer.Ordinal).Any();
    }

    private sealed class InMemoryCaseStore(CaseDocument[] cases) : IDocumentStore<CaseDocument>
    {
        private readonly Dictionary<string, CaseDocument> records = cases.ToDictionary(c => c.EntityId);

        public Task<StoredDocument<CaseDocument>> ReadAsync(string mailboxId, string id, CancellationToken ct) =>
            Task.FromResult(records.TryGetValue(id, out CaseDocument? doc)
                ? new StoredDocument<CaseDocument>(doc, "etag")
                : throw new DocumentStoreException(StoreFailure.NotFound));

        public Task<StoredDocument<CaseDocument>> CreateAsync(string mailboxId, CaseDocument doc, CancellationToken ct)
        {
            if (!records.TryAdd(doc.EntityId, doc)) throw new DocumentStoreException(StoreFailure.Conflict);
            return Task.FromResult(new StoredDocument<CaseDocument>(doc, "etag"));
        }

        public Task<StoredDocument<CaseDocument>> ReplaceAsync(
            string mailboxId, string id, CaseDocument doc, string etag, CancellationToken ct)
        {
            if (!records.ContainsKey(id)) throw new DocumentStoreException(StoreFailure.NotFound);
            records[id] = doc;
            return Task.FromResult(new StoredDocument<CaseDocument>(doc, "etag"));
        }

        public Task DeleteAsync(string mailboxId, string id, string etag, CancellationToken ct)
        {
            records.Remove(id);
            return Task.CompletedTask;
        }
    }
}

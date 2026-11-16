using Ehcp.Core;
using Ehcp.Infrastructure;
using Xunit;

namespace Ehcp.Tests;

public sealed class RulesTests
{
    private static EmailDocument Email(params ChildResult[] children) => new("email-1", "thread-1", Direction.inbound, DateTimeOffset.UtcNow, children);
    private static ChildResult Child(ChildStatus status, string? match = null) => new(null, status, null, null, null, match, [], [], []);
    [Fact] public void UnmatchedEmailExpiresAfterThirtyDays() => Assert.Equal(2592000, DocumentRules.EmailTtl(Email(Child(ChildStatus.PendingMatch))));
    [Fact] public void AnyFoldedChildKeepsWholeEmail() => Assert.Equal(-1, DocumentRules.EmailTtl(Email(Child(ChildStatus.PendingMatch), Child(ChildStatus.Folded, "case-1"))));
    [Fact] public void FoldedChildRequiresInternalCaseId() => Assert.Throws<DocumentValidationException>(() => DocumentRules.Validate(Email(Child(ChildStatus.Folded))));
    [Fact] public void NonFoldedChildCannotHaveMatchedCaseId() => Assert.Throws<DocumentValidationException>(() => DocumentRules.Validate(Email(Child(ChildStatus.PendingMatch, "case-1"))));
    [Fact] public void NullNestedEntryIsRejected() => Assert.Throws<DocumentValidationException>(() => DocumentRules.Validate(Email(null!)));
    [Theory] [InlineData("")] [InlineData("a/b")] [InlineData("a?b")] [InlineData("a#b")]
    public void InvalidIdsAreRejected(string id) => Assert.Throws<DocumentValidationException>(() => DocumentRules.ValidateId(id));
    [Fact] public void SynergyReferenceIsNotTheInternalId()
    {
        var record = new CaseDocument("case-1", "case-1", "Example Child", "EHC-2024-0456", null, null, [], [], [], []);
        DocumentRules.Validate(record);
        Assert.Throws<DocumentValidationException>(() => DocumentRules.Validate(record with { EntityId = "EHC-2024-0456" }));
    }
}

namespace Ehcp.Core;

public sealed class StrongIdentifiers
{
    private StrongIdentifiers(string? nhsNumber, string? synergyId, IReadOnlyList<CaseAttribute> attributes)
    {
        NhsNumbers = Values(nhsNumber, [], "nhsNumber");
        SynergyId = Values(synergyId, attributes, "synergyId");
        DfeIds = Values(null, attributes, "dfeId");
        MosaicIds = Values(null, attributes, "mosaicId");
    }

    public IReadOnlyList<string> NhsNumbers { get; }
    public IReadOnlyList<string> SynergyId { get; }
    public IReadOnlyList<string> DfeIds { get; }
    public IReadOnlyList<string> MosaicIds { get; }

    public bool HasAny => NhsNumbers.Count + SynergyId.Count + DfeIds.Count + MosaicIds.Count > 0;
    public bool HasConflicts => NhsNumbers.Count > 1 || SynergyId.Count > 1 || DfeIds.Count > 1 || MosaicIds.Count > 1;

    public static StrongIdentifiers From(ExtractedChild child) =>
        new(child.NhsNumber, child.CaseId, child.Attributes);

    public static StrongIdentifiers From(CaseDocument document) =>
        new(document.NhsNumber, document.CaseId, document.Attributes);

    public bool Contradicts(StrongIdentifiers other) =>
        Different(NhsNumbers, other.NhsNumbers)
        || Different(SynergyId, other.SynergyId)
        || Different(DfeIds, other.DfeIds)
        || Different(MosaicIds, other.MosaicIds);

    private static bool Different(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count > 0 && second.Count > 0 && !first.SequenceEqual(second);

    private static string[] Values(string? primary, IReadOnlyList<CaseAttribute> attributes, string type) =>
    [
        .. attributes
            .Where(attribute => string.Equals(attribute.Type.Trim(), type, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => attribute.Value)
            .Prepend(primary)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];
}

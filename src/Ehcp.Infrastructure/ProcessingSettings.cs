namespace Ehcp.Infrastructure;

public sealed class GraphSettings
{
    public string BaseUrl { get; set; } = "http://localhost:7101";
    public Dictionary<string, string> Mailboxes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ExtractionSettings
{
    public string Endpoint { get; set; } = "http://localhost:7102/api/extract";
}

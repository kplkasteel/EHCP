namespace Ehcp.Infrastructure;

public sealed class GraphSettings
{
    public string Mode { get; set; } = "Live";
    public string DataPath { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public Dictionary<string, string> Mailboxes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ExtractionSettings
{
    public string Mode { get; set; } = "Live";
    public string DataPath { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

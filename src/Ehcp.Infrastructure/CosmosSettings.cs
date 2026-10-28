namespace Ehcp.Infrastructure;

public sealed class CosmosSettings
{
    public string Endpoint { get; set; } = "https://localhost:8081";
    public string Key { get; set; } = "";
    public string Database { get; set; } = "ehcp-local";
    public bool Initialize { get; set; }
    public Dictionary<string, MailboxSettings> Mailboxes { get; set; } = new(StringComparer.Ordinal);
    public bool IsValid() => Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && !string.IsNullOrWhiteSpace(Key)
        && !string.IsNullOrWhiteSpace(Database) && Mailboxes.Count > 0
        && Mailboxes.All(m => !string.IsNullOrWhiteSpace(m.Key)
            // FUTURE AUTH: && !string.IsNullOrWhiteSpace(m.Value.Role)
            && !string.IsNullOrWhiteSpace(m.Value.EmailsContainer)
            && !string.IsNullOrWhiteSpace(m.Value.CasesContainer))
        && Mailboxes.Values.SelectMany(m => new[] { m.EmailsContainer, m.CasesContainer })
            .Distinct(StringComparer.Ordinal).Count() == Mailboxes.Count * 2;
}
public sealed class MailboxSettings
{
    public string Role { get; set; } = "";
    public string EmailsContainer { get; set; } = "";
    public string CasesContainer { get; set; } = "";
}

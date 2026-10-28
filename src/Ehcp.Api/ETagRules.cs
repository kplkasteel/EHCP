namespace Ehcp.Api;

public static class ETagRules
{
    public static bool IsSpecific(string? value) => value is { Length: > 2 }
        && value[0] == '"' && value[^1] == '"'
        && !value[1..^1].Any(c => c == '"' || char.IsControl(c));
}

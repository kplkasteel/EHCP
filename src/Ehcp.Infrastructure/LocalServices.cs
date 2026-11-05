namespace Ehcp.Infrastructure;

public static class LocalServices
{
    public static void RequireEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || !uri.IsLoopback
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Only HTTP(S) loopback endpoints are supported by this local prototype.");
    }

    public static void RequireAzurite(string? connectionString)
    {
        if (!string.Equals(connectionString?.Trim(), "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Set AzureWebJobsStorage to UseDevelopmentStorage=true for local Azurite.");
    }
}

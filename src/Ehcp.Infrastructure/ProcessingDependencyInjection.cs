using Ehcp.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ehcp.Infrastructure;

public static class ProcessingDependencyInjection
{
    public static IServiceCollection AddEhcpProcessingAdapters(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        GraphSettings graph = configuration.GetSection("Processing:Graph").Get<GraphSettings>() ?? new();
        ExtractionSettings extraction = configuration.GetSection("Processing:Extraction").Get<ExtractionSettings>() ?? new();
        services.AddSingleton(graph);
        services.AddSingleton(extraction);
        services.AddScoped<ICaseLookup, CosmosCaseLookup>();

        if (IsMock(graph.Mode, graph.DataPath, isDevelopment))
            services.AddScoped<IGraphAPIService, MockGraphAPIService>();
        else
        {
            if (!Guid.TryParse(graph.TenantId, out _) || !Guid.TryParse(graph.ClientId, out _)
                || string.IsNullOrWhiteSpace(graph.ClientSecret) || graph.Mailboxes.Count == 0
                || graph.Mailboxes.Values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Configure Processing:Graph tenant, client credentials and mailbox user mappings.");
            services.AddHttpClient<IGraphAPIService, GraphAPIService>();
        }

        if (IsMock(extraction.Mode, extraction.DataPath, isDevelopment))
            services.AddScoped<IEmailEntityExtractionService, MockEhcpExtractionService>();
        else
        {
            if (!Uri.TryCreate(extraction.Endpoint, UriKind.Absolute, out Uri? endpoint)
                || endpoint.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Configure an HTTPS Processing:Extraction:Endpoint.");
            services.AddHttpClient<IEmailEntityExtractionService, HttpEmailEntityExtractionService>();
        }
        return services;
    }

    private static bool IsMock(string mode, string path, bool isDevelopment)
    {
        if (mode == "Live") return false;
        if (mode != "Mock") throw new InvalidOperationException("Processing adapter mode must be Live or Mock.");
        if (!isDevelopment) throw new InvalidOperationException("Fixture services are restricted to Development.");
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new InvalidOperationException("Mock DataPath must be an existing absolute file path.");
        return true;
    }
}

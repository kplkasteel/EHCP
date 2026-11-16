using Ehcp.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ehcp.Infrastructure;

public static class ProcessingDependencyInjection
{
    public static IServiceCollection AddEhcpProcessingAdapters(
        this IServiceCollection services, IConfiguration configuration)
    {
        GraphSettings graph = configuration.GetSection("Processing:Graph").Get<GraphSettings>() ?? new();
        ExtractionSettings extraction = configuration.GetSection("Processing:Extraction").Get<ExtractionSettings>() ?? new();
        if (graph.Mailboxes.Count == 0 || graph.Mailboxes.Values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Configure Processing:Graph:Mailboxes for mock email direction detection.");
        services.AddSingleton(graph);
        services.AddSingleton(extraction);
        services.AddScoped<ICaseLookup, CosmosCaseLookup>();
        services.AddHttpClient<IGraphAPIService, GraphAPIService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddHttpClient<IEmailEntityExtractionService, HttpEmailEntityExtractionService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        return services;
    }
}

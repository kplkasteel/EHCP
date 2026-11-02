using System.Net.Http.Json;
using Ehcp.Core;

namespace Ehcp.Infrastructure;

// Adapter for the configured EHCP extraction HTTP service. It does not assume an AI vendor.
public sealed class HttpEmailEntityExtractionService(HttpClient http, ExtractionSettings settings)
    : IEmailEntityExtractionService
{
    public async Task<EmailExtraction> ExtractAsync(string mailboxId, GraphEmail email, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = JsonContent.Create(new { mailboxId, email },
                options: CosmosDocumentStore<EmailDocument>.JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Add("X-Api-Key", settings.ApiKey);
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmailExtraction>(
            CosmosDocumentStore<EmailDocument>.JsonOptions, ct)
            ?? throw new DocumentValidationException("Extraction response is empty.");
    }
}

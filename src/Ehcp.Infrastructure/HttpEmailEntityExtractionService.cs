using System.Net.Http.Json;
using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Infrastructure;

// HTTP adapter for the local mock extraction service.
public sealed class HttpEmailEntityExtractionService(HttpClient http, ExtractionSettings settings)
    : IEmailEntityExtractionService
{
    public async Task<EmailExtraction> ExtractAsync(string mailboxId, GraphEmail email, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { mailboxId, email },
                    CosmosDocumentStore<EmailDocument>.JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmailExtraction>(
                   CosmosDocumentStore<EmailDocument>.JsonOptions, ct)
               ?? throw new DocumentValidationException("Extraction response is empty.");
    }
}
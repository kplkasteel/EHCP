using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Infrastructure;

public sealed class GraphAPIService(HttpClient http, GraphSettings settings) : IGraphAPIService
{
    public async Task<GraphEmail> GetEmailAsync(string mailboxId, string graphMessageId, CancellationToken ct)
    {
        if (!settings.Mailboxes.TryGetValue(mailboxId, out string? userId))
            throw new DocumentValidationException("No Graph user is configured for this mailbox.");
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post,
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(settings.TenantId)}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            })
        };
        using HttpResponseMessage tokenResponse = await http.SendAsync(tokenRequest, ct);
        tokenResponse.EnsureSuccessStatusCode();
        using JsonDocument token = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Graph token response is empty.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userId)}/messages/{Uri.EscapeDataString(graphMessageId)}?$select=id,conversationId,receivedDateTime,subject,body,isDraft,parentFolderId,from");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            token.RootElement.GetProperty("access_token").GetString());
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Graph message response is empty.");
        JsonElement message = json.RootElement;
        // Inbound/outbound comes from whether this mailbox sent the message.
        string? sender = message.TryGetProperty("from", out JsonElement from)
            ? from.GetProperty("emailAddress").GetProperty("address").GetString() : null;
        Direction direction = string.Equals(sender, userId, StringComparison.OrdinalIgnoreCase)
            ? Direction.outbound : Direction.inbound;
        return new GraphEmail(message.GetProperty("id").GetString()!,
            message.GetProperty("conversationId").GetString()!, direction,
            message.GetProperty("receivedDateTime").GetDateTimeOffset(),
            message.GetProperty("subject").GetString() ?? "",
            message.GetProperty("body").GetProperty("content").GetString() ?? "");
    }
}

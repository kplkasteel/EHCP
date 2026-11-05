using System.Net.Http.Json;
using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Infrastructure;

public sealed class GraphAPIService(HttpClient http, GraphSettings settings) : IGraphAPIService
{
    public async Task<GraphEmail> GetEmailAsync(string mailboxId, string graphMessageId, CancellationToken ct)
    {
        if (!settings.Mailboxes.TryGetValue(mailboxId, out string? userId))
            throw new DocumentValidationException("No mock mailbox address is configured for this mailbox.");
        using HttpResponseMessage response = await http.GetAsync(
            $"{settings.BaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(userId)}/messages/{Uri.EscapeDataString(graphMessageId)}", ct);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Mock Graph response is empty.");
        return ParseMessage(json.RootElement, userId);
    }

    private static GraphEmail ParseMessage(JsonElement message, string userId)
    {
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


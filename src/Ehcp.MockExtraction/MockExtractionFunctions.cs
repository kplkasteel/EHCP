using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ehcp.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ehcp.MockExtraction;

public sealed record ExtractionSeed(string MailboxId, string EmailId, EmailExtraction Result);
public sealed record ExtractionRequest(string MailboxId, GraphEmail Email);

public sealed class MockExtractionFunctions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static ExtractionSeed? seed;

    [Function("SeedExtraction")]
    public async Task<HttpResponseData> SeedAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/seed")] HttpRequestData req)
    {
        ExtractionSeed? value = await ReadAsync<ExtractionSeed>(req);
        if (value is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        seed = value;
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("Extract")]
    public async Task<HttpResponseData> ExtractAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/extract")] HttpRequestData req)
    {
        ExtractionRequest? request = await ReadAsync<ExtractionRequest>(req);
        if (request is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        ExtractionSeed? current = seed;
        if (current is null || current.MailboxId != request.MailboxId || current.EmailId != request.Email.Id)
            return req.CreateResponse(HttpStatusCode.NotFound);
        return await JsonAsync(req, current.Result);
    }

    private static async Task<T?> ReadAsync<T>(HttpRequestData req)
    {
        string body = await new StreamReader(req.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, Options);
    }

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, T value)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(value, Options));
        return response;
    }
}

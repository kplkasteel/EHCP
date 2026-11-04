using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ehcp.MockGraph;

public sealed class MockGraphFunctions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static string? seededId;
    private static string? seededMessageJson;

    [Function("SeedGraphMessage")]
    public async Task<HttpResponseData> SeedAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/seed")] HttpRequestData req)
    {
        string body = await new StreamReader(req.Body).ReadToEndAsync();
        string? id = JsonNode.Parse(body)?["id"]?.GetValue<string>();
        if (id is null) return req.CreateResponse(HttpStatusCode.BadRequest);
        seededId = id;
        seededMessageJson = body;
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("GraphToken")]
    public async Task<HttpResponseData> TokenAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{tenant}/oauth2/v2.0/token")] HttpRequestData req,
        string tenant)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync("{\"token_type\":\"Bearer\",\"expires_in\":3600,\"access_token\":\"mock\"}");
        return response;
    }

    [Function("GetGraphMessage")]
    public async Task<HttpResponseData> GetMessageAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1.0/users/{user}/messages/{id}")] HttpRequestData req,
        string user, string id)
    {
        if (seededId is null || seededMessageJson is null || seededId != id)
            return req.CreateResponse(HttpStatusCode.NotFound);
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(seededMessageJson);
        return response;
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Ehcp.ApiFunctions;

// Read-only endpoints used by the Specs.
public sealed class RecordFunctions(
    IDocumentStore<CaseDocument> cases,
    IDocumentStore<EmailDocument> emails,
    ILogger<RecordFunctions> logger)
{
    [Function("GetCase")]
    public async Task<HttpResponseData> GetCaseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/mailboxes/{mailboxId}/cases/{entityId}")] HttpRequestData req,
        string mailboxId, string entityId, CancellationToken ct)
    {
        try
        {
            DocumentRules.ValidateId(entityId);
            StoredDocument<CaseDocument> stored = await cases.ReadAsync(mailboxId, entityId, ct);
            CaseDocument document = stored.Document with
            {
                Communications = stored.Document.Communications.OrderByDescending(c => c.ReceivedDateTime).ToArray()
            };
            return await OkAsync(req, document, stored.ETag);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "GetCase");
            return await ErrorAsync(req, ex);
        }
    }

    [Function("GetEmail")]
    public async Task<HttpResponseData> GetEmailAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/mailboxes/{mailboxId}/emails/{emailId}")] HttpRequestData req,
        string mailboxId, string emailId, CancellationToken ct)
    {
        try
        {
            DocumentRules.ValidateId(emailId);
            StoredDocument<EmailDocument> stored = await emails.ReadAsync(mailboxId, emailId, ct);
            return await OkAsync(req, stored.Document, stored.ETag);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "GetEmail");
            return await ErrorAsync(req, ex);
        }
    }

    private void LogFailure(Exception exception, string operation)
    {
        // No document bodies, child names, or request identifiers are logged.
        switch (exception)
        {
            case DocumentStoreException { Failure: StoreFailure.NotFound }:
                logger.LogDebug("{Operation}: record not found", operation);
                break;
            case DocumentValidationException:
                logger.LogWarning("{Operation}: invalid record identifier", operation);
                break;
            default:
                logger.LogError(exception, "{Operation} failed", operation);
                break;
        }
    }

    private static async Task<HttpResponseData> OkAsync<T>(HttpRequestData req, T value, string etag)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("ETag", etag);

        // Enforce the public GetCase response contract at the HTTP boundary.
        // Older Cosmos records may contain "caseId": null, but the API omits that
        // property when no external case identifier exists. Other nulls must remain.
        // Keep this explicit instead of enabling global WhenWritingNull.
        //JsonNode? body = JsonSerializer.SerializeToNode(value, CosmosDocumentStore<EmailDocument>.JsonOptions);
        //if (value is CaseDocument caseDocument && caseDocument.CaseId is null && body is JsonObject caseJson)
        //    caseJson.Remove("caseId");

        //await response.WriteStringAsync(body?.ToJsonString(CosmosDocumentStore<EmailDocument>.JsonOptions) ?? "null");
        await response.WriteStringAsync(JsonSerializer.Serialize(value, CosmosDocumentStore<EmailDocument>.JsonOptions));

        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData req, Exception ex)
    {
        (HttpStatusCode status, string code, string message) = ex switch
        {
            DocumentValidationException e => (HttpStatusCode.BadRequest, "VALIDATION_ERROR", e.Message),
            DocumentStoreException { Failure: StoreFailure.NotFound } =>
                (HttpStatusCode.NotFound, "NOT_FOUND", "No record found for the supplied id."),
            DocumentStoreException => (HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "Document storage is temporarily unavailable."),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
        };
        HttpResponseData response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { code, message }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return response;
    }
}

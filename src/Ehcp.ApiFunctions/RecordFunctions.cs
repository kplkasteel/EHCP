using System.Net;
using System.Text.Json;
using Ehcp.Core;
using Ehcp.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Ehcp.ApiFunctions;

// Read-only endpoints used by the Specs.
public sealed class RecordFunctions(IDocumentStore<CaseDocument> cases, IDocumentStore<EmailDocument> emails)
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
            return await ErrorAsync(req, ex);
        }
    }

    private static async Task<HttpResponseData> OkAsync<T>(HttpRequestData req, T value, string etag)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        response.Headers.Add("ETag", etag);
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
        // Do not log payloads or identifiers; the exception type is enough to diagnose the failure.
        if ((int)status >= 500) Console.Error.WriteLine($"Request failed ({ex.GetType().Name}).");
        HttpResponseData response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { code, message }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return response;
    }
}

using Ehcp.Core;
using Microsoft.AspNetCore.Diagnostics;

namespace Ehcp.Api;

public sealed record ApiError(string Code, string Message);

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            DocumentValidationException e => (400, "VALIDATION_ERROR", e.Message),
            DocumentStoreException { Failure: StoreFailure.NotFound } => (404, "NOT_FOUND", "No record found for the supplied id."),
            DocumentStoreException { Failure: StoreFailure.Conflict } => (409, "CONFLICT", "A record with this id already exists."),
            DocumentStoreException { Failure: StoreFailure.PreconditionFailed } => (412, "PRECONDITION_FAILED", "The record has changed. Read it again before retrying."),
            DocumentStoreException => (503, "UNAVAILABLE", "Document storage is temporarily unavailable."),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")
        };
        // Do not log payloads, credentials, identifiers or SDK exception text containing personal data.
        if (status >= 500) logger.LogError("Request failed ({ErrorType}); trace {TraceId}", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ApiError(code, message), ct);
        return true;
    }
}

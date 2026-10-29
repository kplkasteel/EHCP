using Ehcp.Core;
// using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ehcp.Api.Controllers;

[ApiController]
// [Authorize(Policy = "Mailbox")]
[Route("api/mailboxes/{mailboxId}/emails")]
[Produces("application/json")]
// [ProducesResponseType<ApiError>(401)]
// [ProducesResponseType<ApiError>(403)]
[ProducesResponseType<ApiError>(400)]
[ProducesResponseType<ApiError>(503)]
public sealed class EmailsController(IDocumentStore<EmailDocument> store) : ControllerBase
{
    [HttpGet("{emailId}")]
    [ProducesResponseType<EmailDocument>(200)]
    [ProducesResponseType<ApiError>(404)]
    public async Task<ActionResult<EmailDocument>> Get(string mailboxId, string emailId, CancellationToken ct)
    {
        DocumentRules.ValidateId(emailId);
        StoredDocument<EmailDocument> stored = await store.ReadAsync(mailboxId, emailId, ct);
        EmailDocument document = stored.Document;
        
        Response.Headers.ETag = stored.ETag;
        return Ok(document);
    }

    [HttpPost]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType<EmailDocument>(201)]
    [ProducesResponseType<ApiError>(409)]
    public async Task<ActionResult<EmailDocument>> Create(string mailboxId, [FromBody] EmailDocument document, CancellationToken ct)
    {
        DocumentRules.Validate(document);
        
        StoredDocument<EmailDocument> stored = await store.CreateAsync(mailboxId, document, ct);
        Response.Headers.ETag = stored.ETag;
        return CreatedAtAction(nameof(Get), new { mailboxId, emailId = document.Id }, stored.Document);
    }

    [HttpPut("{emailId}")]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType<EmailDocument>(200)]
    [ProducesResponseType<ApiError>(404)]
    [ProducesResponseType<ApiError>(412)]
    [ProducesResponseType<ApiError>(428)]
    public async Task<ActionResult<EmailDocument>> Replace(string mailboxId, string emailId,
        [FromBody] EmailDocument document, [FromHeader(Name = "If-Match")] string? etag, CancellationToken ct)
    {
        DocumentRules.Validate(document);
        if (emailId != document.Id) throw new DocumentValidationException("Route and body identifiers must match.");
        if (!ETagRules.IsSpecific(etag)) return StatusCode(428, new ApiError("PRECONDITION_REQUIRED", "Supply the ETag from GET in If-Match."));
        
        StoredDocument<EmailDocument> stored = await store.ReplaceAsync(mailboxId, emailId, document, etag!, ct);
        Response.Headers.ETag = stored.ETag;
        return Ok(stored.Document);
    }

    [HttpDelete("{emailId}")]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ApiError>(404)]
    [ProducesResponseType<ApiError>(412)]
    [ProducesResponseType<ApiError>(428)]
    public async Task<IActionResult> Delete(string mailboxId, string emailId,
        [FromHeader(Name = "If-Match")] string? etag, CancellationToken ct)
    {
        DocumentRules.ValidateId(emailId);
        if (!ETagRules.IsSpecific(etag)) return StatusCode(428, new ApiError("PRECONDITION_REQUIRED", "Supply the ETag from GET in If-Match."));
        await store.DeleteAsync(mailboxId, emailId, etag!, ct);
        return NoContent();
    }
}

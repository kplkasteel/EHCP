using Ehcp.Core;
// using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ehcp.Api.Controllers;

[ApiController]
// [Authorize(Policy = "Mailbox")]
[Route("api/mailboxes/{mailboxId}/cases")]
[Produces("application/json")]
// [ProducesResponseType<ApiError>(401)]
// [ProducesResponseType<ApiError>(403)]
[ProducesResponseType<ApiError>(400)]
[ProducesResponseType<ApiError>(503)]
public sealed class CasesController(IDocumentStore<CaseDocument> store) : ControllerBase
{
    [HttpGet("{entityId}")]
    [ProducesResponseType<CaseDocument>(200)]
    [ProducesResponseType<ApiError>(404)]
    public async Task<ActionResult<CaseDocument>> Get(string mailboxId, string entityId, CancellationToken ct)
    {
        DocumentRules.ValidateId(entityId);
        var stored = await store.ReadAsync(mailboxId, entityId, ct);
        var document = stored.Document;
        document = document with { Communications = document.Communications.OrderByDescending(c => c.ReceivedDateTime).ToArray() };
        Response.Headers.ETag = stored.ETag;
        return Ok(document);
    }

    [HttpPost]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType<CaseDocument>(201)]
    [ProducesResponseType<ApiError>(409)]
    public async Task<ActionResult<CaseDocument>> Create(string mailboxId, [FromBody] CaseDocument document, CancellationToken ct)
    {
        DocumentRules.Validate(document);
        document = document with { Communications = document.Communications.OrderByDescending(c => c.ReceivedDateTime).ToArray() };
        var stored = await store.CreateAsync(mailboxId, document, ct);
        Response.Headers.ETag = stored.ETag;
        return CreatedAtAction(nameof(Get), new { mailboxId, entityId = document.EntityId }, stored.Document);
    }

    [HttpPut("{entityId}")]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType<CaseDocument>(200)]
    [ProducesResponseType<ApiError>(404)]
    [ProducesResponseType<ApiError>(412)]
    [ProducesResponseType<ApiError>(428)]
    public async Task<ActionResult<CaseDocument>> Replace(string mailboxId, string entityId,
        [FromBody] CaseDocument document, [FromHeader(Name = "If-Match")] string? etag, CancellationToken ct)
    {
        DocumentRules.Validate(document);
        if (entityId != document.EntityId) throw new DocumentValidationException("Route and body identifiers must match.");
        if (!ETagRules.IsSpecific(etag)) return StatusCode(428, new ApiError("PRECONDITION_REQUIRED", "Supply the ETag from GET in If-Match."));
        document = document with { Communications = document.Communications.OrderByDescending(c => c.ReceivedDateTime).ToArray() };
        var stored = await store.ReplaceAsync(mailboxId, entityId, document, etag!, ct);
        Response.Headers.ETag = stored.ETag;
        return Ok(stored.Document);
    }

    [HttpDelete("{entityId}")]
    // [Authorize(Policy = "Writer")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ApiError>(404)]
    [ProducesResponseType<ApiError>(412)]
    [ProducesResponseType<ApiError>(428)]
    public async Task<IActionResult> Delete(string mailboxId, string entityId,
        [FromHeader(Name = "If-Match")] string? etag, CancellationToken ct)
    {
        DocumentRules.ValidateId(entityId);
        if (!ETagRules.IsSpecific(etag)) return StatusCode(428, new ApiError("PRECONDITION_REQUIRED", "Supply the ETag from GET in If-Match."));
        await store.DeleteAsync(mailboxId, entityId, etag!, ct);
        return NoContent();
    }
}

# EHCP API — .NET 10

ASP.NET Core Web API with eight CRUD operations, generated OpenAPI 3.0, Cosmos DB for NoSQL, environment configuration, authentication/authorization code retained as comments for future development. This is a source solution to build and verify locally, not a deployed service.

## Quick start (PowerShell, Windows Cosmos emulator)

1. Install the .NET 10 SDK and Azure Cosmos DB emulator for NoSQL. Start the emulator and trust its local TLS certificate. This application uses HTTPS Gateway mode for Cosmos; it does not bypass certificate validation. The emulator must support item TTL.
2. Open `Ehcp.slnx` in an IDE supporting .NET 10, or use the commands below from this directory. Use the emulator key from its local Data Explorer; no key is committed.

```powershell
$env:Cosmos__Endpoint = 'https://localhost:8081'
$env:Cosmos__Key = '<your-local-emulator-key>'
$env:Cosmos__Database = 'ehcp-local'
$env:Cosmos__Initialize = 'true'
$env:ASPNETCORE_ENVIRONMENT = 'Development'

dotnet restore Ehcp.slnx
dotnet build Ehcp.slnx
dotnet test tests/Ehcp.Tests

dotnet run --project src/Ehcp.Api
```

All endpoints currently accept requests without a token. No authentication services, middleware or Entra metadata discovery are configured. API base URL: `http://localhost:5100`; OpenAPI JSON: `http://localhost:5100/openapi/v1.json`. Import this URL into Postman or an OpenAPI client. An interactive Swagger UI is not included. The API's HTTP listener is for loopback development; production must terminate TLS at its configured gateway/host.

```powershell
$headers = @{}
$base = 'http://localhost:5100/api/mailboxes/ehcp-team-shared'
$email = @{
    id = 'example-email-1'
    conversationId = 'example-thread-1'
    direction = 'inbound'
    receivedDateTime = '2026-10-28T10:00:00Z'
    children = @()
} | ConvertTo-Json -Depth 10

Invoke-RestMethod "$base/emails" -Method Post -Headers $headers -ContentType 'application/json' -Body $email
$response = Invoke-WebRequest "$base/emails/example-email-1" -Headers $headers
$headers['If-Match'] = [string]$response.Headers.ETag
Invoke-RestMethod "$base/emails/example-email-1" -Method Put -Headers $headers -ContentType 'application/json' -Body $email
# PUT produces a new ETag: read it before DELETE.
$response = Invoke-WebRequest "$base/emails/example-email-1" -Headers $headers
$headers['If-Match'] = [string]$response.Headers.ETag
Invoke-RestMethod "$base/emails/example-email-1" -Method Delete -Headers $headers
```

## Endpoint contract

All routes start with `/api/mailboxes/{mailboxId}`.

| Method | Suffix | Successful result |
| --- | --- | --- |
| POST | `/emails` | 201, email, Location and ETag |
| GET | `/emails/{emailId}` | 200, email and ETag |
| PUT | `/emails/{emailId}` | 200, full replacement and new ETag |
| DELETE | `/emails/{emailId}` | 204 |
| POST | `/cases` | 201, case, Location and ETag |
| GET | `/cases/{entityId}` | 200, case and ETag |
| PUT | `/cases/{entityId}` | 200, full replacement and new ETag |
| DELETE | `/cases/{entityId}` | 204 |

GET provides the quoted ETag; send it unchanged as `If-Match` for PUT/DELETE. Missing, wildcard, multiple and weak ETags are rejected with 428. A stale ETag returns 412. Create never upserts (duplicates return 409); PUT never creates a missing document (404). DELETE hard-deletes the selected document without cascading. There are no collection/list endpoints or PATCH operations in this version.

Errors use `{ "code": "...", "message": "..." }`: 400 validation, 404 missing, 409 duplicate, 412 stale version, 428 missing version, 503 storage unavailable, 500 unexpected failure. Invalid JSON and model-binding failures return 400. Infrastructure exception details are not returned or logged.

JSON enums are strings (integer enums rejected). Write contracts require every non-optional constructor field, including explicit `null` for nullable fields and `[]` for empty arrays. Example case: `docs/example-case.json`. PUT is a complete replacement, not an officer field-edit workflow. Case communications are returned newest-first.

## Configuration

ASP.NET Core configuration loads JSON, development user secrets, environment variables, then command-line overrides. Use `__` for nested environment settings. No `.env` loader is installed: a `.env` file alone will not set process variables.

| Variable | Purpose |
| --- | --- |
| `Cosmos__Endpoint` | Cosmos NoSQL HTTPS endpoint |
| `Cosmos__Key` | Account/emulator key; required, never committed |
| `Cosmos__Database` | Database name |
| `Cosmos__Initialize` | Create/validate database and containers; only allowed in Development |
| `Cosmos__Mailboxes__ehcp-team-shared__Role` | Future mailbox app-role value; currently unused |
| `Cosmos__Mailboxes__ehcp-team-shared__EmailsContainer` | Email container name |
| `Cosmos__Mailboxes__ehcp-team-shared__CasesContainer` | Case container name |
| `AllowedHosts` | Allowed API hostnames; default is localhost |

Additional mailboxes are configured the same way with different dictionary keys. Each mailbox must have two distinct containers, and no containers may be shared between mailboxes. The supplied mailbox is a local example; replace/remove it in `appsettings.json` when configuring production. JSON dictionary entries are merged, so adding environment entries does not remove the sample entry.

## Authentication status and future re-enablement

Authentication and authorization are commented out, including the JWT package reference, startup validation and registration, middleware, controller attributes, mailbox authorization helper, and OpenAPI bearer declarations. No Entra settings, credentials or tokens are needed. Existing mailbox Role values are retained as unused future configuration. Unknown mailboxes return 404; known mailboxes are accessible without a role check. Container selection still uses configured names only.

The local launch profile binds to `http://localhost:5100`; Cosmos defaults to `https://localhost:8081`. The running API needs only the local Cosmos emulator. Package restore requires NuGet access unless packages are already cached. Cosmos emulator credentials remain necessary and are separate from API authentication.

To enable authentication in future:

1. Uncomment the marked JWT package reference and auth `using` directives.
2. Uncomment the `FUTURE AUTH` startup block in `Program.cs`, plus `UseAuthentication` and `UseAuthorization`.
3. Uncomment `MailboxAuthorization.cs`, controller authorization attributes and 401/403 metadata, the mailbox role configuration validation, and OpenAPI security declarations.
4. Set `Authentication__Mode=Entra`, `Entra__TenantId` and `Entra__Audience`. Provision `StandardUser`, configured mailbox roles and `DataWriter` for writes. The preserved alternative `LocalJwt` mode is Development-only.
5. Restore/adapt the commented auth test methods and token-enabled factory in `FutureAuthentication.cs` and the mailbox authorization unit test. Replace current anonymous-access expectations and update the OpenAPI tests to expect bearer security.

The commented code is retained for review; it is not an active security layer. The gateway, Entra registration and Application Insights are not provisioned.

## Cosmos model and isolation

| Container | Partition key | TTL |
| --- | --- | --- |
| Mailbox emails | `/id` | 2,592,000 seconds (30 days); item `ttl=-1` when any child is Folded |
| Mailbox cases | `/entityId` | Disabled |

`id == entityId` for cases. `caseId` remains the external Synergy reference. `matchedCaseId` and candidate `entityId` refer to the internal case key. Email IDs must be Cosmos-safe: the API rejects `/`, `\`, `?`, `#`, control characters, blanks and overly long IDs. If Graph supplies incompatible IDs, agree an ingestion-wide reversible encoding before integrating; this solution does not silently rewrite the identifier contract.

The adapter uses point reads and a singleton Cosmos client. It stores the prototype JSON at the top level, with server-calculated email `ttl`; Cosmos metadata is excluded from API responses. Initializer checks existing partition paths/TTL and fails on mismatches instead of rewriting them. Cosmos TTL is relative to the last modification (`_ts`), so replacing a pending email restarts its TTL; this is not a fixed received-date expiry policy. TTL deletion is asynchronous.

## Boundaries and integration decisions

- The supplied material is an OpenAPI/read-model prototype, not executable ingestion or processing code. Its read endpoints and identifiers are retained; CRUD is added per the request. It labels itself 3.1 but uses 3.0-style `nullable`; generated output explicitly targets 3.0.
- This is an ASP.NET Core service, not an Azure Functions project. The prototype's product title alone was not treated as a hosting requirement.
- Core contains framework-independent models/rules and a storage port. Infrastructure implements Cosmos persistence. API handles HTTP, authorization, composition and validation. No extra service layer, mediator or interface-per-class is introduced without business behaviour to justify it.
- This is trusted document CRUD. It does not run AI extraction, verify cross-document links, fold emails into cases, merge records, resolve conflicts, generate drafts, or provide multi-document transactions. A Folded result must carry a matched case ID but the referenced case's existence is not checked atomically.
- `sourceEmailId` is preserved on reads and accepted as supplied on full replacement. This CRUD surface does **not** enforce the prototype's officer-edit provenance rule. A dedicated edit command needs stable relationship/attribute IDs and provenance rules before it is exposed to officer UI clients. When authentication is enabled, restrict `DataWriter` to trusted integrations until that workflow is implemented.
- Archive state is not defined in the supplied schema. DELETE is hard-delete; archive filtering/workflows are not implemented. No automatic cascade is performed.
- Confirm actual mailbox roles, container names, Graph ID format, TTL semantics and the separate design v1.4 document before production integration. That document was mentioned in the paste but was not attached.

## Verification

```powershell
dotnet test tests/Ehcp.Tests
$env:EHCP_TEST_COSMOS_KEY = '<your-local-emulator-key>'
$env:EHCP_TEST_COSMOS_ENDPOINT = 'https://localhost:8081'
dotnet test tests/Ehcp.IntegrationTests
```

Unit/API tests cover identifier and child-state validation, TTL selection, anonymous HTTP access, token-free CRUD/concurrency and OpenAPI operation count/version with no bearer requirement. Emulator tests create a uniquely named temporary database, check round-trip serialization, duplicate handling, partition isolation, item TTL and stale ETags, then delete only that database. They explicitly report skipped if the emulator key is absent. Use a dedicated local emulator: tests provision and delete their temporary database.

**Delivery verification:** JSON/XML and archive structure checked; C# source reviewed. The authoring workspace had no .NET SDK or emulator, so restore, compilation, test execution and live OpenAPI generation have not been verified. The included GitHub Actions workflow runs build and unit/API tests when placed at the repository root. Run the emulator tests locally before merging/integration. Package references are pinned; review dependency updates and generate a lockfile after the first successful restore.

## Reference documentation

- [ASP.NET Core OpenAPI generation and version selection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0)
- [JWT bearer validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [Cosmos .NET SDK practices and JSON dependencies](https://learn.microsoft.com/en-us/azure/cosmos-db/best-practice-dotnet)

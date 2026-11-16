# EHCP — .NET 10

Azure Functions (isolated worker) solution for EHCP email processing and case lookup, backed by Queue Storage and Cosmos DB for NoSQL. Four function apps: `Ehcp.Processing` (queue trigger), `Ehcp.ApiFunctions` (read-only HTTP API), and `Ehcp.MockGraph` and `Ehcp.MockExtraction` (test doubles). Deployed to Azure with Bicep and `rake deploy`.

## Quick start (PowerShell, Windows Cosmos emulator)

1. Install the .NET 10 SDK, Azure Functions Core Tools v4, and the Azure Cosmos DB emulator for NoSQL. Start the emulator and trust its local TLS certificate.
2. Use the emulator key from its local Data Explorer; no key is committed.

With Ruby and Rake installed, `rake up` sets the local variables, builds, and starts the API (`5100`), the Processing host and the mock Graph (`7101`) and mock extraction (`7102`) hosts in the foreground; Ctrl+C stops them. In another terminal, `rake specs` runs the Reqnroll specs in `tests/Ehcp.Specs` against them. `rake go` runs unit tests, starts the services, runs the specs and stops the services. `rake -T` lists the individual steps.

To run the API alone:

```powershell
Set-Location src/Ehcp.ApiFunctions
func start --dotnet-isolated --port 5100
```

Copy `local.settings.example.json` to `local.settings.json` in the same folder and fill in `Cosmos__Key`. `local.settings.json` is git-ignored.

## Azure sandbox

- `rake deploy` deploys the Bicep in `infra/main.bicep` into the existing resource group (`EHCP_RESOURCE_GROUP`, default `jamie-lupton-sandbox-rg`), then packs and zip-deploys the four apps. Zips are kept in `artifacts/deploy/`.
- `rake publish_apps[api]` republishes one app without touching infrastructure. Omit the argument to publish all four.
- `rake test_remote` runs the specs against Azure. `rake test_remote_wip` runs only `@wip` scenarios. Remote runs delete all documents in the sandbox email and case containers, one scenario at a time.

## API endpoints

Read-only. No authentication is configured.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/mailboxes/{mailboxId}/emails/{emailId}` | 200, email and ETag |
| GET | `/api/mailboxes/{mailboxId}/cases/{entityId}` | 200, case and ETag |

Case communications are returned newest-first. Enums are strings.

Errors use `{ "code": "...", "message": "..." }`: 400 invalid identifier, 404 not found, 503 storage unavailable, 500 unexpected failure. Infrastructure exception details are not returned or logged.

## Configuration

Function apps read `appsettings.json`, then environment variables (`__` separates nested keys).

| Variable | Purpose |
| --- | --- |
| `Cosmos__Endpoint` | Cosmos NoSQL HTTPS endpoint |
| `Cosmos__Key` | Account or emulator key; required, never committed |
| `Cosmos__Database` | Database name |
| `Cosmos__Initialize` | Create and validate containers; only allowed in Development |
| `Cosmos__Mailboxes__ehcp-team-shared-1__EmailsContainer` | Email container name (local settings only; set in `appsettings.json` on Azure) |
| `Cosmos__Mailboxes__ehcp-team-shared-1__CasesContainer` | Case container name (as above) |
| `AzureWebJobsStorage` | Queue Storage connection string |
| `ProcessingQueueName` | Processing queue name |

Each mailbox must have two distinct containers, and no containers may be shared between mailboxes. Mailbox keys containing hyphens cannot be set as App Service app settings, so their container names are read from `appsettings.json`.

## Cosmos model

| Container | Partition key | TTL |
| --- | --- | --- |
| Mailbox emails | `/id` | 2,592,000 seconds (30 days); item `ttl=-1` when any child is Folded |
| Mailbox cases | `/entityId` | Disabled |

`id == entityId` for cases. `caseId` is the external Synergy reference. `matchedCaseId` and candidate `entityId` refer to the internal case key. Email IDs must be Cosmos-safe: no `/`, `\`, `?`, `#`, control characters, blanks, or IDs over 512 characters. Cosmos TTL is relative to the last modification, so replacing a pending email restarts its TTL. TTL deletion is asynchronous.

## Queue-triggered email processing

The processing host implements the queue → Graph → extraction → matching → Cosmos flow. Graph and extraction can use real HTTP adapters or configured JSON fixtures in Development, while matching and Cosmos stay real. Email-level status tracks Unprocessed, Processing, Processed or Failed independently of child outcomes.

See [local setup and adapter contracts](docs/processing/README.md) and [architecture, matching and retry behavior](docs/dependencies.md).

## Verification

```powershell
rake unit
$env:EHCP_TEST_COSMOS_KEY = '<your-local-emulator-key>'
$env:EHCP_TEST_COSMOS_ENDPOINT = 'https://localhost:8081'
dotnet test tests/Ehcp.IntegrationTests
```

Unit tests cover identifier and child-state validation, TTL selection, and ETag validation. Emulator tests create a uniquely named temporary database, check round-trip serialization, partition isolation, item TTL and stale ETags, then delete only that database. They report skipped if the emulator key is absent. The Reqnroll specs cover the read endpoints and the processing flow.

The included GitHub Actions workflow runs build and unit tests on push and pull request.

## Boundaries

- `Ehcp.ApiFunctions` exposes read-only endpoints only. Write operations were removed with the ASP.NET Core API and are not provided by any hosted service.
- Core holds framework-independent models, rules and the storage port. Infrastructure implements Cosmos persistence and configuration.
- Cross-container writes are retryable but not atomic. See `docs/dependencies.md`.
- Confirm actual mailbox names, container names, Graph ID format and TTL semantics before production integration.

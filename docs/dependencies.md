# Email processing architecture

The application API and the queue-triggered Processing Function share Cosmos
configuration and containers. The Function runs Graph retrieval, extraction,
matching and persistence through constructor-injected Core contracts.

| Project | References / responsibility |
| --- | --- |
| Core | Models and service contracts; no infrastructure frameworks |
| Application | Core; processing orchestration and matching policy |
| Infrastructure | Core; Cosmos, Graph, extraction HTTP and fixture adapters |
| Processing | Application, Infrastructure; Azure Functions queue host |
| Api | Core, Infrastructure; existing application endpoints |

Both hosts call AddEhcpInfrastructure. The Function also calls AddEhcpProcessing
and AddEhcpProcessingAdapters. CosmosClient is singleton; document stores,
matching, processor and fixture services use invocation/request scopes. Live
HTTP adapters use HttpClientFactory. Application does not reference either
host or Infrastructure.

## Processing and retry

Queue JSON contains mailboxId, emailId and graphMessageId. emailId is the
Cosmos-safe application key; graphMessageId is the original Graph ID and is
URL-escaped for retrieval. Do not reuse an emailId for a different message.

The processor reads or creates the email, saves Processing, retrieves Graph,
extracts and validates all children, runs matching, then saves Processed.
Email status and child status are separate: a Processed email can contain a
PendingMatch child. Existing email JSON without status defaults to Unprocessed.
Failures save Failed and propagate to the queue runtime. Cancellation propagates.
After five failed deliveries the runtime moves the message to the -poison queue.

ETags protect replacements. Deterministic case keys and communication
deduplication by emailId make retries converge after a partial write. Already
Processed emails skip repeat deliveries. Case and email writes are not atomic:
a case can contain the communication before the final email write succeeds.
Retry the original payload to complete it. Queue delivery is at least once.

## Initial matching policy

The diagram does not specify matching/creation rules. MatchingService isolates
this explicit initial policy:

* Search only the selected mailbox's cases.
* Require an NHS number, external Synergy case reference, DfE ID or Mosaic ID
  before searching. Find candidates by these identifiers or exact name plus DOB.
  Text identity comparisons trim outer whitespace and ignore case; no fuzzy matching.
* Multiple candidates, conflicting values for one identifier, or contradictory
  NHS number, Synergy reference, DfE ID, Mosaic ID or DOB,
  produce Conflict with possibleCaseMatches.
* A unique noncontradictory candidate is Folded.
* With no candidate, a name plus a strong identifier creates a case and folds the email.
  Otherwise the child remains PendingMatch.
* Preserve existing facts, fill missing identifiers, and append novel relationships
  and attributes with sourceEmailId. AI output cannot grant officer-confirmed userStatus.

This is a deterministic initial policy, not an asserted statutory EHCP rule.

## Adapter selection

Processing:Graph:Mode and Processing:Extraction:Mode independently select Live
or Mock. MockGraphAPIService and MockEhcpExtractionService verify the mailbox
and message identity in their fixture files. Matching and Cosmos remain real.
Mock mode requires Development and an existing absolute DataPath.

Set environment variables before starting the host. Changing the environment
of a spec process does not reconfigure an already running Function.

See [processing/README.md](processing/README.md) for setup and live contracts.

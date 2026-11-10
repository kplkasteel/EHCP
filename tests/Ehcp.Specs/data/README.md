# Specs data fixtures

Naming: `scenario-<n>-<slug>-<role>.json`. Roles: `data`, `queue`, `graph-email`, `ai-extract`, `expected-result`.

| Scenario | Feature | Covers | Fixtures |
|---|---|---|---|
| 1 | GetEmail | Single child extracted, insufficient for a case: status `Processed`, child `PendingMatch` | `scenario-1-single-child-*` |
| 2 | GetEmail | Single child extracted, sufficient information for a case: status `Processed`, child `Folded` | `scenario-2-single-child-*` |
| 3 | GetEmail | No child extracted, discarded status with rationale recorded. | `scenario-3-no-child-*` |
| 4 | GetEmail | Irrelevant out-of-office email is discarded with its rationale. | `scenario-4-out-of-office-*` |

Scenarios without fixtures use inline values in the feature file.

Adding a scenario: copy the five role files for the next number, keep the shared ids in step (`emailId`, `conversationId`, Graph message id, `receivedDateTime`), and add a row above.

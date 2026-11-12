# Specs data fixtures

Naming: `scenario-<n>-<slug>-<role>.json`. Roles: `data`, `queue`, `graph-email`, `ai-extract`, `expected-result`.

| Scenario | Feature | Covers | Fixtures |
|---|---|---|---|
| 1 | GetEmail | Single child extracted, insufficient for a case: status `Processed`, child `PendingMatch` | `scenario-1-single-child-*` |
| 2 | GetEmail (Scenario Outline) | Single child with enough information is folded into a new case. Runs once per Examples row. | `case-creation-*` (tokenised) |
| 3 | GetEmail | No child extracted, discarded status with rationale recorded. | `scenario-3-no-child-*` |
| 4 | GetEmail | Irrelevant out-of-office email is discarded with its rationale. | `scenario-4-out-of-office-*` |
| 5 | GetEmail | `@wip`. Child with no strong ID but a name and DOB match lists scored possible case matches. Seeds cases from `scenario-5-fuzzy-name-possible-matches-cases.json`. Scores (10 - DOB + Fuzzy name, 7 - DOB only). | `scenario-5-fuzzy-name-possible-matches-*` |

Scenarios without fixtures use inline values in the feature file.

## Case creation tokens

`case-creation-*` files are templates. Values come from the Outline's `the scenario values are` and `the child identifiers are` tables.

| Token | Form | Source |
|---|---|---|
| `{{emailId}}`, `{{conversationId}}`, `{{parentName}}`, `{{parentEmail}}` | text, inside quotes in the file | scenario values |
| `{{childName:json}}`, `{{nhsNumber:json}}`, `{{dateOfBirth:json}}` | JSON literal, `null` when blank | child identifiers |
| `{{attributes}}` | JSON array with `confidenceBand` | other identifiers, ai-extract input |
| `{{expectedAttributes}}` | JSON array with `type` and `value` only | other identifiers, expected output |
| `{{graphMessageId}}`, `{{caseAlias}}`, `{{subject}}`, `{{childLabel}}`, `{{identifierLines}}` | derived | — |

Adding an identifier means a table row plus an Examples column. Attributes are compared on `type` and `value` only. An unknown token fails the run.

Adding a scenario: copy the five role files for the next number, keep the shared ids in step (`emailId`, `conversationId`, Graph message id, `receivedDateTime`), and add a row above.

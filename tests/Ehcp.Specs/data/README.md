# Specs data fixtures

Naming: `scenario-<n>-<slug>-<role>.json`. Roles: `data`, `queue`, `graph-email`, `ai-extract`, `expected-result`.

| Scenario | Feature | Covers | Fixtures |
|---|---|---|---|
| 1 | GetEmail | Single child extracted, insufficient for a case: status `Processed`, child `PendingMatch` | `scenario-1-single-child-*` |
| 2 | GetEmail (Scenario Outline) | Single child with enough information is folded into a new case. Runs once per Examples row. | `case-creation-*` (tokenised) |
| 3 | GetEmail | No child extracted, discarded status with rationale recorded. | `scenario-3-no-child-*` |
| 4 | GetEmail | Irrelevant out-of-office email is discarded with its rationale. | `scenario-4-out-of-office-*` |
| 5a | GetEmail (22 examples) | PendingMatch with DOB/fuzzy-name (10), DOB-only (7), initials+dob (10), exact-name-only (5), exact-name+DOB (12), fuzzy-name-only (3), no matches, ties and ordering. | `scenario-5-fuzzy-name-possible-matches-*` |
| 5b | GetEmail (17 examples) | NHS, DfE, Mosaic, Synergy attribute and Synergy caseId; Folded and Conflict paths, including competing identifiers and additive scores. | same `scenario-5-*` templates |

Scenarios without fixtures use inline values in the feature file.

## Case creation tokens

`case-creation-*` files are templates. Values come from the Outline's `the scenario values are` and `the child identifiers are` tables.

| Token | Form | Source |
|---|---|---|
| `{{emailId}}`, `{{conversationId}}`, `{{parentName}}`, `{{parentEmail}}` | text, inside quotes in the file | scenario values |
| `{{childName:json}}`, `{{nhsNumber:json}}`, `{{caseId:json}}`, `{{dateOfBirth:json}}` | JSON literal, `null` when blank | child identifiers |
| `{{attributes}}` | JSON array with `confidenceBand` | other identifiers, ai-extract input |
| `{{expectedAttributes}}` | JSON array with `type` and `value` only | other identifiers, expected output |
| `{{graphMessageId}}`, `{{caseAlias}}`, `{{subject}}`, `{{childLabel}}`, `{{identifierLines}}` | derived | — |

Adding an identifier means a table row plus an Examples column. Attributes are compared on `type` and `value` only. An unknown token fails the run.

Scenario 5 has **two outlines** (39 examples total). Both use the same dataset files.

The `{{expectedMatches}}` token is taken from `the scenario values are` via the
`expectedMatches` Examples column. Each entry is `entityId:ConfidenceBand:score`,
semicolon-separated and written in **descending score order**, with equal scores sorted
by `entityId`. For example: `spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7`.
An empty cell asserts `possibleCaseMatches: []`. The renderer fails early if the
scenario forgets the `expectedMatches` table row.

`{{expectedStatus}}` defaults to `PendingMatch` in the fuzzy outline and is set from the
`expectedStatus` Examples column in the strong-identifier outline (`Folded` or `Conflict`).
`{{expectedCaseId:json}}` is `null` by default and set to the known internal entityId for
`Folded` rows; it is checked via the existing matched-case alias handling.

The input's `caseId` is the direct Synergy identifier; `synergyId`, `dfeId` and
`mosaicId` are **attributes** generated from the table via `{{attributes}}`.
Those same attributes are projected into the expected response via `{{expectedAttributes}}`.
Seeded cases have all four strong identifiers, so each identifier is checked end-to-end.

The scoring rules use **one, non-additive name evidence group**:
exact full name=5, first/fuzzy name=3, or initials=3. Only the best applicable
name rule contributes. Thus exact full name alone scores **5 (Low)**, and
exact full name + DOB scores **12 (Medium)**. DOB=7 and each independent strong
identifier (NHS, Synergy, DfE, Mosaic) adds 10. A fuzzy name + DOB still scores
**10 (Medium)**; initials + DOB scores **10 (Medium)**. Name overlaps must
never be counted more than once. The examples contain fixed expected scores,
not values computed by calling the production scoring function.
However strong IDs trigger case folding or conflict handling, not a pending fuzzy match.
No production weights were changed to make the fixtures pass.

Adding a scenario: copy the five role files for the next number, keep the shared ids in step (`emailId`, `conversationId`, Graph message id, `receivedDateTime`), and add a row above.

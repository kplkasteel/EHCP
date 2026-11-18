# Specs data fixtures

Naming: `scenario-<n>-<slug>-<role>.json`. Roles: `data`, `queue`, `graph-email`, `ai-extract`, `expected-result`, and `cases` (seeded case documents) for scenarios 5 and 7–9.

| Scenario | Feature | Covers | Fixtures |
|---|---|---|---|
| 1 | GetEmail | Single child extracted, insufficient for a case: status `Processed`, child `PendingMatch` | `scenario-1-single-child-*` |
| 2 | GetEmail (Scenario Outline) | Single child with enough information is folded into a new case. Runs once per Examples row. | `case-creation-*` (tokenised) |
| 3 | GetEmail | No child extracted, discarded status with rationale recorded. | `scenario-3-no-child-*` |
| 4 | GetEmail | Irrelevant out-of-office email is discarded with its rationale. | `scenario-4-out-of-office-*` |
| 5a | GetEmail (22 examples) | PendingMatch with DOB/fuzzy-name (10), DOB-only (7), initials+dob (10), exact-name-only (5), exact-name+DOB (12), fuzzy-name-only (3), no matches, ties and ordering. | `scenario-5-fuzzy-name-possible-matches-*` |
| 5b | GetEmail (17 examples) | NHS, DfE, Mosaic, Synergy attribute and Synergy caseId; Folded and Conflict paths, including competing identifiers and additive scores. | same `scenario-5-*` templates |
| 7 | GetEmail (5 examples) | US-0004: single Low possible match from a fuzzy name plus the child's extracted parent name and/or email; child `PendingMatch`, no matched case. | `scenario-7-low-match-relationships-*` |
| 8 | GetEmail (6 examples) | US-0004: single Low possible match where the child's NHS, Mosaic, DfE, Synergy or caseId differs from the case. Fails until the matching change lands (today it creates a new case). | `scenario-8-low-match-identifier-mismatch-*` |
| 9 | GetEmail (4 examples) | US-0004: single Low possible match on date of birth only, with and without a matching parent name or email. | `scenario-9-low-match-dob-*` |

Scenarios without fixtures use inline values in the feature file.

## Case creation tokens

`case-creation-*` files are templates. Values come from the Outline's `the scenario values are` and `the child identifiers are` tables.

| Token | Form | Source |
|---|---|---|
| `{{emailId}}`, `{{conversationId}}`, `{{parentName}}`, `{{parentEmail}}` | text, inside quotes in the file | scenario values |
| `{{childName:json}}`, `{{nhsNumber:json}}`, `{{caseId:json}}`, `{{dateOfBirth:json}}` | JSON literal, `null` when blank | child identifiers |
| `{{attributes}}` | JSON array with `confidenceBand` | other identifiers, ai-extract input |
| `{{expectedAttributes}}` | JSON array with `type` and `value` only | other identifiers, expected output |
| `{{childRelationships}}` | JSON array: one `parent` relationship, or `[]` when blank | `childParentName` / `childParentEmail` scenario values, ai-extract input |
| `{{expectedRelationships}}` | the same relationship as the API returns it (`confidenceBand: VeryLow`, `userStatus` and `sourceEmailId` null), or `[]` | `childParentName` / `childParentEmail` scenario values, expected output |
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

## Scenarios 7–9: single low-confidence match (US-0004)

Each scenario seeds its own two cases in `scenario-<n>-*-cases.json`: the case the child should match and
a second case that must not appear, so each example asserts exactly one `Low` possible match (score below 10),
child `PendingMatch` and no matched case. They do not reuse the scenario-5 cases.

| Scenario | Case that should match | Case that must not match |
|---|---|---|
| 7 | `spec-case-oliver-hughes`: Oliver Hughes, DOB 2018-06-12, parent Grace Hughes (grace.hughes@example.com) | `spec-case-amelia-khan`: Amelia Khan, DOB 2018-06-12, parent Sara Khan |
| 8 | `spec-case-isla-patel`: Isla Patel, DOB 2017-09-23, NHS 333 444 5556, S-3334445, D-40001, M-30001, parent Raj Patel (raj.patel@example.com) | `spec-case-ivy-chen`: Ivy Chen, DOB 2017-09-23, parent Lin Chen |
| 9 | `spec-case-noah-williams`: Noah Williams, DOB 2019-05-05, parent Emma Williams (emma.williams@example.com) | `spec-case-zara-ahmed`: Zara Ahmed, DOB 2016-01-20, parent Omar Ahmed |

`parentName` / `parentEmail` are the email sender only and play no part in matching. The child's parent comes from the
`childParentName` / `childParentEmail` Examples columns via `{{childRelationships}}`; matching gives parent name 2 and
parent email 2 when they equal a relationship on the case. Leave both blank for no extracted relationships.
Data, queue and graph-email files are copies of the scenario-5 templates.

Adding a scenario: copy the five role files for the next number, keep the shared ids in step (`emailId`, `conversationId`, Graph message id, `receivedDateTime`), and add a row above.

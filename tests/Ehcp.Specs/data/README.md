# Specs data fixtures: manual verification guide

This folder holds the JSON fixtures the `GetEmail.feature` scenarios load. This README lets you check each example by hand: compare the AI-extracted child with the seeded case, then follow the rule path to the expected result.

Rules are in [`tests/MATCHING-RULES.md`](../../MATCHING-RULES.md). Rule IDs in the tables below (for example `6a`, `7d`) match that file.

## How to verify an example

1. Find the example in the tables below.
2. Read the AI-extracted child. Blank Examples columns are omitted.
3. Compare it with the seeded case(s) listed for that scenario.
4. Follow the rule path from step 2 of the rules file. Each step is a rule ID.
5. Check the result matches the expected status, matched case and possible matches.

Values in the AI column come from the feature file's Examples table, not from the `ai-extract` JSON templates. Blank fields are not shown.

## Scenario summary

| Scenario | Feature section | Examples | Seeded cases | Fixtures |
|---|---|---|---|---|
| Retrieval, cross-mailbox, not found | Retrieve email | 4 | none | none (endpoint only) |
| 1 Single child, not enough for a case | Email with child information extracted but not enough to create a case | 1 | none | `scenario-1-single-child-*` |
| 3 No child extracted | Email with no child information is discarded | 1 | none | `scenario-3-no-child-*` |
| 4 Out-of-office reply | An out-of-office reply is discarded | 1 | none | `scenario-4-out-of-office-*` |
| Creation | Email with child information extracted and enough information should create a case | 6 | none (`case-creation-data.json` is empty) | `case-creation-*` (tokenised) |
| 5 Fuzzy names and date of birth | Email with a fuzzy-matching child folds on a Medium or High score and lists Low possible matches | 23 | Fred Bloggs, Jones Smith | `scenario-5-fuzzy-name-possible-matches-*` |
| 6 Strong identifiers | Strong identifier matching folds a case or returns scored conflicts | 17 | Fred Bloggs, Jones Smith | same as scenario 5 |
| 7 Parent match | Email with a fuzzy-matching child folds on a parent match and stays pending without one | 5 | Oliver Hughes, Amelia Khan | `scenario-7-low-match-relationships-*` |
| 8 Identifier differs | Email with a child whose identifier differs from the matched case is a conflict | 6 | Isla Patel, Ivy Chen | `scenario-8-low-match-identifier-mismatch-*` |
| 9 Date of birth | Email with a child matching on date of birth folds on a parent match and stays pending without one | 4 | Noah Williams, Zara Ahmed | `scenario-9-low-match-dob-*` |
| 10 Identifier conflicts (US-0008) | Email with a child whose identifiers conflict with existing cases lists the matching cases and flags each one | 10 | Ethan Clarke, Mia Roberts, Zoe Ward, Ava Turner | `scenario-10-identifier-conflict-*` |

`scenario-6-no-dob-match-possible-matches-*` exists here but no scenario in `GetEmail.feature` uses it. Treat it as unused until it is either wired in or removed.

## Seeded cases

Scenarios 5 and 6 seed the same two cases. Scenarios 7, 8 and 9 each seed their own pair: a case the child should match and a case that must not match.

### Scenario 5 and 6 cases (`scenario-5-fuzzy-name-possible-matches-cases.json`)

| Case | Name | DOB | NHS | Case ID | DfE | Mosaic | Synergy | Parent |
|---|---|---|---|---|---|---|---|---|
| `spec-case-fred-bloggs` | Fred Bloggs | 2020-03-01 | 123 456 7890 | S-1234567 | D-20001 | M-10001 | S-1234567 | none |
| `spec-case-jones-smith` | Jones Smith | 2020-03-01 | 456 789 0123 | S-7654321 | D-20002 | M-10002 | S-7654321 | none |

### Scenario 7 cases

| Case | Name | DOB | NHS | Case ID | DfE | Mosaic | Synergy | Parent | Role |
|---|---|---|---|---|---|---|---|---|---|
| `spec-case-oliver-hughes` | Oliver Hughes | 2018-06-12 | 222 333 4445 | S-2223334 | D-30001 | M-20001 | S-2223334 | Grace Hughes, grace.hughes@example.com | should match |
| `spec-case-amelia-khan` | Amelia Khan | 2018-06-12 | 222 333 5556 | S-2223335 | D-30002 | M-20002 | S-2223335 | Sara Khan, sara.khan@example.com | must not match |

### Scenario 8 cases

| Case | Name | DOB | NHS | Case ID | DfE | Mosaic | Synergy | Parent | Role |
|---|---|---|---|---|---|---|---|---|---|
| `spec-case-isla-patel` | Isla Patel | 2017-09-23 | 333 444 5556 | S-3334445 | D-40001 | M-30001 | S-3334445 | Raj Patel, raj.patel@example.com | should match |
| `spec-case-ivy-chen` | Ivy Chen | 2017-09-23 | 333 444 6667 | S-3334446 | D-40002 | M-30002 | S-3334446 | Lin Chen, lin.chen@example.com | must not match |

### Scenario 9 cases

| Case | Name | DOB | NHS | Case ID | DfE | Mosaic | Synergy | Parent | Role |
|---|---|---|---|---|---|---|---|---|---|
| `spec-case-noah-williams` | Noah Williams | 2019-05-05 | 444 555 6667 | S-4445556 | D-50001 | M-40001 | S-4445556 | Emma Williams, emma.williams@example.com | should match |
| `spec-case-zara-ahmed` | Zara Ahmed | 2016-01-20 | 444 555 7778 | S-4445557 | D-50002 | M-40002 | S-4445557 | Omar Ahmed, omar.ahmed@example.com | must not match |

### Scenario 10 cases

| Case | Name | DOB | NHS | Case ID | DfE | Mosaic | Synergy | Parent | Role |
|---|---|---|---|---|---|---|---|---|---|
| `spec-case-ethan-clarke` | Ethan Clarke | 2016-04-10 | 555 666 7778 | S-5556667 | D-60001 | M-50001 | S-5556667 | Laura Clarke, laura.clarke@example.com | conflicts in every conflict example |
| `spec-case-mia-roberts` | Mia Roberts | 2015-11-02 | 555 666 8889 | S-5556668 | D-60002 | M-50002 | S-5556668 | Dan Roberts, dan.roberts@example.com | conflicts only when the email carries her DfE ID |
| `spec-case-zoe-ward` | Zoe Ward | 2017-01-30 | 555 666 9990 | S-5556669 | D-60003 | M-50003 | S-5556669 | Kim Ward, kim.ward@example.com | must never get a conflict |
| `spec-case-ava-turner` | Ava Turner | 2016-08-15 | 555 666 1112 | none | none | none | none | Paul Turner, paul.turner@example.com | holds only an NHS number, so a new Synergy ID is not a conflict |

## Retrieval, discard and single child

No seeded cases. Retrieval rows test the endpoint only.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| Retrieve `spec-email-001` | none | Retrieved, status Unprocessed | none |
| Retrieve `spec-email-002` | none | Retrieved, status Processed | none |
| Cross-mailbox `spec-email-005` | none | Not found in shared-2 | none |
| Not present `spec-email-006` | none | Not found | none |
| Scenario 1 single child | Name Alex Example; DOB 2016-03-14 | PendingMatch, no matched case, no possible matches | 3, 3b |
| Scenario 3 no child | none | Discarded: "No child information was extracted from the email." | 1 |
| Scenario 4 out-of-office | none | Discarded: "Out of office reply." | 1 |

## Creation

No seeded cases. Each example has a strong ID and a name, so the flow creates a case.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `spec-c-1` | Name Alex Example; NHS 987 654 3210; DOB 2016-03-14 | Folded to a new case, possible matches none | 3, 3a |
| `spec-c-1` | Name Alex Example; NHS 987 654 3210 | Folded to a new case, possible matches none | 3, 3a |
| `spec-c-2` | Name Alex Example; DfE D-20001 | Folded to a new case, possible matches none | 3, 3a |
| `spec-c-3` | Name Alex; Mosaic M-10001; DfE D-20001 | Folded to a new case, possible matches none | 3, 3a |
| `spec-c-3` | Name Alex; Mosaic M-10001 | Folded to a new case, possible matches none | 3, 3a |
| `spec-c-3` | Name Alex Example; Synergy S-1234567 | Folded to a new case, possible matches none | 3, 3a |

## Scenario 5: fuzzy names and date of birth

Seeded: Fred Bloggs and Jones Smith. Neither has a parent, so the flow takes the no-parent route (rule 6c).

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `scenario-5-fuzzy-name-possible-matches` | Name Freddy; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` (Medium 10 beats Jones Low 7) | 4, 6c, 7d |
| `spec-f-fredd-dob` | Name Fredd; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 6c, 7d |
| `spec-f-fred-dob` | Name Fred; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 6c, 7d |
| `spec-f-frederick-dob` | Name Frederick; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 6c, 7d |
| `spec-f-jones-dob` | Name Jones; DOB 2020-03-01 | Folded to `spec-case-jones-smith` (Medium 10 beats Fred Low 7) | 4, 6c, 7d |
| `spec-f-other-name-dob` | Name Martha; DOB 2020-03-01 | PendingMatch; Fred Low 7, Jones Low 7 | 4, 6c, 7b |
| `spec-f-dob-only` | No name; DOB 2020-03-01 | PendingMatch; Fred Low 7, Jones Low 7 | 4, 6c, 7b |
| `spec-f-fred-initial-dob` | Name F. Bloggs; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 6c, 7d |
| `spec-f-jones-initial-dob` | Name J. Smith; DOB 2020-03-01 | Folded to `spec-case-jones-smith` | 4, 6c, 7d |
| `spec-f-fuzzy-name-only` | Name Freddy | PendingMatch; Fred Low 3 | 4, 6c, 8a |
| `spec-f-fredd-name-only` | Name Fredd | PendingMatch; Fred Low 3 | 4, 6c, 8a |
| `spec-f-jones-name-only` | Name Jones | PendingMatch; Jones Low 3 | 4, 6c, 8a |
| `spec-f-full-name-only` | Name Fred Bloggs | PendingMatch; Fred Low 5 | 4, 6c, 8a |
| `spec-f-jones-full-only` | Name Jones Smith | PendingMatch; Jones Low 5 | 4, 6c, 8a |
| `spec-f-exact-fred-dob` | Name Fred Bloggs; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` (Medium 12 beats Jones Low 7) | 4, 6c, 7d |
| `spec-f-exact-jones-dob` | Name Jones Smith; DOB 2020-03-01 | Folded to `spec-case-jones-smith` (Medium 12 beats Fred Low 7) | 4, 6c, 7d |
| `spec-f-no-evidence` | none | PendingMatch, no possible matches | 3, 3b |
| `spec-f-different-dob` | Name Fredster; DOB 2021-03-01 | PendingMatch; Fred Low 3 | 4, 6c, 8a |
| `spec-f-full-lowercase-only` | Name fReD bLoGgS | PendingMatch; Fred Low 5 | 4, 6c, 8a |
| `spec-f-full-lowercase-dob` | Name fReD bLoGgS; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` (Medium 12 beats Jones Low 7) | 4, 6c, 7d |
| `spec-f-different-surname-dob` | Name Fred Harper; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` (Medium 10 beats Jones Low 7) | 4, 6c, 7d |
| `spec-f-fuzzy-first-name-dob` | Name Freddy Bloggs; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` (Medium 10 beats Jones Low 7) | 4, 6c, 7d |
| `spec-f-different-name-different-dob` | Name Tom; DOB 2019-03-01 | PendingMatch, no possible matches | 3, 3b |

## Scenario 6: strong identifiers

Seeded: Fred Bloggs and Jones Smith. Every example has a strong ID, so it goes through step 5.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `spec-s-nhs-fold` | Name Fred Bloggs; NHS 123 456 7890; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 5, 5b |
| `spec-s-dfe-fold` | Name Fred Bloggs; DfE D-20001; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 5, 5b |
| `spec-s-mosaic-fold` | Name Fred Bloggs; Mosaic M-10001; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 5, 5b |
| `spec-s-synergy-attribute-fold` | Name Fred Bloggs; Synergy S-1234567; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 5, 5b |
| `spec-s-synergy-field-fold` | Name Fred Bloggs; Case ID S-1234567; DOB 2020-03-01 | Folded to `spec-case-fred-bloggs` | 4, 5, 5b |
| `spec-s-nhs-jones-fold` | Name Jones Smith; NHS 456 789 0123 | Folded to `spec-case-jones-smith` | 4, 5, 5b |
| `spec-s-dfe-jones-fold` | Name Jones Smith; DfE D-20002 | Folded to `spec-case-jones-smith` | 4, 5, 5b |
| `spec-s-nhs-conflict` | Name Alice; NHS 123 456 7890; DOB 2021-03-01 | Conflict; Fred Medium 10 | 4, 5, 5a |
| `spec-s-dfe-conflict` | Name Alice; DfE D-20001; DOB 2021-03-01 | Conflict; Fred Medium 10 | 4, 5, 5a |
| `spec-s-mosaic-conflict` | Name Alice; Mosaic M-10001; DOB 2021-03-01 | Conflict; Fred Medium 10 | 4, 5, 5a |
| `spec-s-synergy-attribute-conflict` | Name Alice; Synergy S-1234567; DOB 2021-03-01 | Conflict; Fred Medium 10 | 4, 5, 5a |
| `spec-s-synergy-field-conflict` | Name Alice; Case ID S-1234567; DOB 2021-03-01 | Conflict; Fred Medium 10 | 4, 5, 5a |
| `spec-s-competing-ids` | Name Alice; NHS 123 456 7890 (Fred); DfE D-20002 (Jones) | Conflict; Fred Medium 10, Jones Medium 10 | 4, 5, 5a |
| `spec-s-same-case-two-ids` | Name Alice; NHS 123 456 7890; DfE D-20001; DOB 2021-03-01 | Conflict; Fred High 20 | 4, 5, 5a |
| `spec-s-competing-dob-ids` | Name Alice; NHS 123 456 7890 (Fred); DfE D-20002 (Jones); DOB 2020-03-01 | Conflict; Fred Medium 17, Jones Medium 17 | 4, 5, 5a |
| `spec-s-exact-name-wrong-dob` | Name Fred Bloggs; NHS 123 456 7890; DOB 2021-03-01 | Conflict; Fred Medium 15 | 4, 5, 5a |
| `spec-s-exact-name-competing-ids` | Name Fred Bloggs; NHS 123 456 7890 (Fred); DfE D-20002 (Jones); DOB 2020-03-01 | Conflict; Fred High 22, Jones Medium 17 | 4, 5, 5a |

## Scenario 7: parent match

Seeded: Oliver Hughes (should match), Amelia Khan (must not match). Parent is the child's extracted parent, not the email sender.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `spec-l-name-parent-email` | Name Oli; parent Grace Hughes, grace.hughes@example.com | Folded to `spec-case-oliver-hughes` | 4, 6, 6a |
| `spec-l-name-parent-name` | Name Oliver; parent Grace Hughes, other@example.com | Folded to `spec-case-oliver-hughes` | 4, 6, 6a |
| `spec-l-name-parent-only` | Name Oli; parent Mark Hughes, grace.hughes@example.com | Folded to `spec-case-oliver-hughes` | 4, 6, 6a |
| `spec-l-name-initials-parent` | Name Oliver H; parent Grace Hughes, other@example.com | Folded to `spec-case-oliver-hughes` | 4, 6, 6a |
| `spec-l-name-other-parent` | Name Oli; parent Sara Khan, sara.khan@example.com | PendingMatch; Oliver Low 3. Sara Khan's case (Amelia) does not match on name or DOB, so no fold. | 4, 6, 6c, 8a |

## Scenario 8: identifier differs from the seeded case

Seeded: Isla Patel. Every example has a strong ID that differs from Isla's value of the same type, and the name and parent match Isla.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `spec-l-diff-nhs` | Name Isla; NHS 999 888 7776; parent Raj Patel, raj.patel@example.com | Conflict; Isla Patel Low 7 | 4, 5, 5a |
| `spec-l-diff-mosaic` | Name Isla; Mosaic M-39999; parent Raj Patel, raj.patel@example.com | Conflict; Isla Patel Low 7 | 4, 5, 5a |
| `spec-l-diff-dfe` | Name Isla; DfE D-49999; parent Raj Patel, raj.patel@example.com | Conflict; Isla Patel Low 7 | 4, 5, 5a |
| `spec-l-diff-synergy` | Name Isla; Synergy S-3999999; parent Raj Patel, raj.patel@example.com | Conflict; Isla Patel Low 7 | 4, 5, 5a |
| `spec-l-diff-caseid` | Name Isla; Case ID S-3999999; parent Raj Patel, raj.patel@example.com | Conflict; Isla Patel Low 7 | 4, 5, 5a |
| `spec-l-diff-nhs-parent` | Name Isla; NHS 999 888 7776; parent Raj Patel, other@example.com | Conflict; Isla Patel Low 5 | 4, 5, 5a |

## Scenario 9: date of birth

Seeded: Noah Williams (should match), Zara Ahmed (must not match). Date of birth 2019-05-05 matches Noah.

| Example | AI-extracted child | Expected | Rule path |
|---|---|---|---|
| `spec-l-dob-other-name` | Name Leo; DOB 2019-05-05; no parent | PendingMatch; Noah Low 7 | 4, 6c, 8a |
| `spec-l-dob-no-name` | No name; DOB 2019-05-05; no parent | PendingMatch; Noah Low 7 | 4, 6c, 8a |
| `spec-l-dob-parent-email` | Name Leo; DOB 2019-05-05; parent Kate Brown, emma.williams@example.com | Folded to `spec-case-noah-williams` | 4, 6, 6a |
| `spec-l-dob-parent-name` | Name Leo; DOB 2019-05-05; parent Emma Williams, other@example.com | Folded to `spec-case-noah-williams` | 4, 6, 6a |

## Scenario 10: identifier conflicts (US-0008)

Seeded: Ethan Clarke, Mia Roberts, Zoe Ward and Ava Turner. A strong ID matches a case, but another ID of the same type or the date of birth differs. The child is Conflict, the matching cases are listed, and a conflict is added to every matching case, never to the email. An ID type the case does not hold is not a conflict, so the last example folds.

Every conflict example uses the name Ethan Clarke, parent Laura Clarke, laura.clarke@example.com, and DOB 2016-04-10 unless shown. Zoe Ward gets no conflict in any example.

| Example | AI-extracted child | Expected | Conflict added to case | Rule path |
|---|---|---|---|---|
| `spec-i-nhs-synergy-differs` | NHS 555 666 7778; Synergy S-9999999 | Conflict; Ethan High 26 | Ethan: High, "NHS number matches; Synergy ID does not match" | 4, 5, 5a |
| `spec-i-nhs-dfe-differs` | NHS 555 666 7778; DfE D-69999 | Conflict; Ethan High 26 | Ethan: High, "NHS number matches; DfE ID does not match" | 4, 5, 5a |
| `spec-i-nhs-mosaic-differs` | NHS 555 666 7778; Mosaic M-59999 | Conflict; Ethan High 26 | Ethan: High, "NHS number matches; Mosaic ID does not match" | 4, 5, 5a |
| `spec-i-dfe-nhs-differs` | NHS 999 999 9999; DfE D-60001 | Conflict; Ethan High 26 | Ethan: High, "DfE ID matches; NHS number does not match" | 4, 5, 5a |
| `spec-i-caseid-nhs-differs` | NHS 999 999 9999; Case ID S-5556667 | Conflict; Ethan High 26 | Ethan: High, "Synergy ID matches; NHS number does not match" | 4, 5, 5a |
| `spec-i-mosaic-nhs-differs` | NHS 999 999 9999; Mosaic M-50001 | Conflict; Ethan High 26 | Ethan: High, "Mosaic ID matches; NHS number does not match" | 4, 5, 5a |
| `spec-i-nhs-dob-differs` | NHS 555 666 7778; DOB 2016-04-11 | Conflict; Ethan Medium 19 | Ethan: Medium, "NHS number matches; Date of birth does not match" | 4, 5, 5a |
| `spec-i-nhs-two-ids-differ` | NHS 555 666 7778; Synergy S-9999999; DfE D-69999 | Conflict; Ethan High 26 | Ethan: High, "NHS number matches; Synergy ID and DfE ID do not match" | 4, 5, 5a |
| `spec-i-ids-two-cases` | NHS 555 666 7778; DfE D-60002 (Mia's) | Conflict; Ethan High 26, Mia Medium 10 | Ethan: High, "NHS number matches; DfE ID does not match". Mia: Medium, "DfE ID matches; NHS number and Date of birth do not match" | 4, 5, 5a |
| `spec-i-missing-id-folds` | Name Ava Turner; NHS 555 666 1112; DOB 2016-08-15; Synergy S-5551112; parent Paul Turner, paul.turner@example.com | Folded to `spec-case-ava-turner` | none | 4, 5, 5b |

The conflict check reads each case from the API. `matchedOn` lists the strong IDs that match, then the strong IDs and date of birth that differ, using the names NHS number, Synergy ID, DfE ID, Mosaic ID and Date of birth. Names are never a conflict. The conflict's band is the band of that case's score.

## Token reference

The `case-creation-*` files are templates. The other fixtures are plain JSON except where noted. Values come from the scenario's `the scenario values are` and `the child identifiers are` tables.

| Token | Form | Source |
|---|---|---|
| `{{emailId}}`, `{{conversationId}}`, `{{parentName}}`, `{{parentEmail}}` | text, inside quotes | scenario values |
| `{{childName:json}}`, `{{nhsNumber:json}}`, `{{caseId:json}}`, `{{dateOfBirth:json}}` | JSON literal, `null` when blank | child identifiers |
| `{{attributes}}` | JSON array with `confidenceBand`, for the AI-extract input | other identifiers |
| `{{expectedAttributes}}` | JSON array with `type` and `value` only, for the expected output | other identifiers |
| `{{childRelationships}}` | one `parent` relationship, or `[]` | `childParentName` / `childParentEmail` |
| `{{expectedRelationships}}` | same relationship as the API returns it (`confidenceBand: VeryLow`), or `[]` | `childParentName` / `childParentEmail` |
| `{{expectedMatches}}` | ordered `entityId:ConfidenceBand:score` list, or `[]` when blank | `expectedMatches` column |
| `{{expectedStatus}}` | `PendingMatch` by default | `expectedStatus` column |
| `{{expectedCaseId:json}}` | matched case ID, or `null` | `expectedCaseId` column |
| `{{graphMessageId}}`, `{{caseAlias}}`, `{{subject}}`, `{{childLabel}}`, `{{identifierLines}}` | derived | derived |

`expectedCaseId` takes the value `created` when the product should create a case. The step accepts any non-empty case ID in that case.

Attributes are compared on `type` and `value` only. An unknown token fails the run.

The step `the case "<caseId>" should have conflict "<expected>"` reads the case from the API and checks its `conflicts` list. The expected value is `Band: matchedOn` for exactly one conflict from this email, or blank for no conflicts. It checks the email ID, band and `matchedOn`, and that `flaggedAt` is present, not its value.

## Scores used by the fixtures

The Examples' `expectedMatches` scores were set by hand, not calculated by the production scorer. The fixture scores are:

- Name: exact full name 5, first name or fuzzy name 3, initials 3. Only the best name rule counts.
- Date of birth: 7.
- Each strong identifier: 10 (NHS, Synergy, DfE, Mosaic).
- The child's extracted parent: name 2, email 2.

Resulting bands: Low below 10, Medium 10 to 19, High 20 and above.

## Adding a scenario

1. Copy the five role files (`data`, `queue`, `graph-email`, `ai-extract`, `expected-result`) for the next number.
2. Keep the shared IDs in step: `emailId`, `conversationId`, Graph message ID and `receivedDateTime`.
3. Add a section above and a row to the scenario summary.
4. For any new case, add a rule path to the example row, and check the rule exists in `MATCHING-RULES.md`.

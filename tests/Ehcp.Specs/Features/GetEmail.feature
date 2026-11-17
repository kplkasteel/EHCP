Feature: Retrieve email

Scenario Outline: Retrieve an existing email by id
	Given the mailbox "ehcp-team-shared-1" is selected
	And the database is setup to contain an email with id "<emailId>" and processing status "<status>"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
	Then the email record should be retrieved with processing status "<status>"

Examples:
	| emailId        | status       |
	| spec-email-001 | Unprocessed  |
	| spec-email-002 | Processed    |

Scenario: Email cannot be retrieved across mailboxes
	Given the mailbox "ehcp-team-shared-1" is selected
	And the database is setup to contain an email with id "spec-email-005" and processing status "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"spec-email-005" endpoint
	Then the email record should be retrieved with processing status "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-2"/emails/"spec-email-005" endpoint
	Then the response should be not found


Scenario: Email not present should return not found
	Given the mailbox "ehcp-team-shared-1" is selected
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"spec-email-006" endpoint
	Then the response should be not found


Scenario: Email with child information extracted but not enough to create a case shows the correct state
	Given the mailbox "ehcp-team-shared-1" is selected
	And the database is setup to contain "scenario-1-single-child-data.json"
	And we setup mock ai extraction to return dataset "scenario-1-single-child-ai-extract.json"
	And we setup mock graph api to return dataset "scenario-1-single-child-graph-email.json"
	When the email record "scenario-1-single-child-queue.json" is added to the queue
	And we wait for the database processing status to be "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"scenario-1-single-child" endpoint
	Then the email record should be retrieved matching "scenario-1-single-child-expected-result.json"

Scenario Outline: Email with a fuzzy-matching child lists scored possible case matches
	Given the mailbox "ehcp-team-shared-1" is selected
	And the scenario values are
		| key | value |
		| emailId | <emailId> |
		| conversationId | <conversationId> |
		| parentName | <parentName> |
		| parentEmail | <parentEmail> |
		| expectedMatches | <expectedMatches> |
	And the child identifiers are
		| identifier | value |
		| childName | <childName> |
		| nhsNumber | <nhsNumber> |
		| caseId | <caseId> |
		| dateOfBirth | <dateOfBirth> |
		| mosaicId | <mosaicId> |
		| dfeId | <dfeId> |
		| synergyId | <synergyId> |
	And the database is setup to contain "scenario-5-fuzzy-name-possible-matches-data.json"
	And the database contains cases "scenario-5-fuzzy-name-possible-matches-cases.json"
	And we setup mock ai extraction to return dataset "scenario-5-fuzzy-name-possible-matches-ai-extract.json"
	And we setup mock graph api to return dataset "scenario-5-fuzzy-name-possible-matches-graph-email.json"
	When the email record "scenario-5-fuzzy-name-possible-matches-queue.json" is added to the queue
	And we wait for the database processing status to be "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
	Then the email record should be retrieved matching "scenario-5-fuzzy-name-possible-matches-expected-result.json"

# Expected matches are ordered id:ConfidenceBand:score entries separated by semicolons.
# Leave expectedMatches blank to assert an empty possibleCaseMatches array.
# Only no-strong-ID rows can remain PendingMatch. Strong-ID rows fold or conflict.
	Examples:
		| emailId                                | conversationId  | parentName  | parentEmail             | childName   | nhsNumber | dateOfBirth | mosaicId | dfeId | synergyId | caseId | expectedMatches                                             |
		| scenario-5-fuzzy-name-possible-matches | spec-thread-005 | Sam Example | sam.example@example.com | Freddy      |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-fredd-dob                       | spec-thread-f01 | Sam Example | sam.example@example.com | Fredd       |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-fred-dob                        | spec-thread-f02 | Sam Example | sam.example@example.com | Fred        |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-frederick-dob                   | spec-thread-f03 | Sam Example | sam.example@example.com | Frederick   |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-jones-dob                       | spec-thread-f04 | Sam Example | sam.example@example.com | Jones       |           | 2020-03-01  |          |       |           |        | spec-case-jones-smith:Medium:10;spec-case-fred-bloggs:Low:7 |
		| spec-f-other-name-dob                  | spec-thread-f05 | Sam Example | sam.example@example.com | Martha      |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7     |
		| spec-f-dob-only                        | spec-thread-f06 | Sam Example | sam.example@example.com |             |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7     |
		| spec-f-fred-initial-dob                | spec-thread-f07 | Sam Example | sam.example@example.com | F. Bloggs   |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-jones-initial-dob               | spec-thread-f08 | Sam Example | sam.example@example.com | J. Smith    |           | 2020-03-01  |          |       |           |        | spec-case-jones-smith:Medium:10;spec-case-fred-bloggs:Low:7 |
		| spec-f-fuzzy-name-only                 | spec-thread-f09 | Sam Example | sam.example@example.com | Freddy      |           |             |          |       |           |        | spec-case-fred-bloggs:Low:3                                 |
		| spec-f-fredd-name-only                 | spec-thread-f10 | Sam Example | sam.example@example.com | Fredd       |           |             |          |       |           |        | spec-case-fred-bloggs:Low:3                                 |
		| spec-f-jones-name-only                 | spec-thread-f11 | Sam Example | sam.example@example.com | Jones       |           |             |          |       |           |        | spec-case-jones-smith:Low:3                                 |
		| spec-f-full-name-only                  | spec-thread-f12 | Sam Example | sam.example@example.com | Fred Bloggs |           |             |          |       |           |        | spec-case-fred-bloggs:Low:5                             |
		| spec-f-jones-full-only                 | spec-thread-f13 | Sam Example | sam.example@example.com | Jones Smith |           |             |          |       |           |        | spec-case-jones-smith:Low:5                             |
		| spec-f-exact-fred-dob                  | spec-thread-f14 | Sam Example | sam.example@example.com | Fred Bloggs |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Medium:12;spec-case-jones-smith:Low:7 |
		| spec-f-exact-jones-dob                 | spec-thread-f15 | Sam Example | sam.example@example.com | Jones Smith |           | 2020-03-01  |          |       |           |        | spec-case-jones-smith:Medium:12;spec-case-fred-bloggs:Low:7 |
		| spec-f-no-evidence                     | spec-thread-f16 | Sam Example | sam.example@example.com |             |           |             |          |       |           |        |                                                             |
		| spec-f-different-dob                   | spec-thread-f17 | Sam Example | sam.example@example.com | Freddy      |           | 2021-03-01  |          |       |           |        |                                                             |
		| spec-f-full-lowercase-only | spec-thread-f18 | Sam Example | sam.example@example.com | fReD bLoGgS |  |  |  |  |  |  | spec-case-fred-bloggs:Low:5 |
		| spec-f-full-lowercase-dob | spec-thread-f19 | Sam Example | sam.example@example.com | fReD bLoGgS |  | 2020-03-01 |  |  |  |  | spec-case-fred-bloggs:Medium:12;spec-case-jones-smith:Low:7 |
		| spec-f-different-surname-dob | spec-thread-f20 | Sam Example | sam.example@example.com | Fred Harper |  | 2020-03-01 |  |  |  |  | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |
		| spec-f-fuzzy-first-name-dob | spec-thread-f21 | Sam Example | sam.example@example.com | Freddy Bloggs |  | 2020-03-01 |  |  |  |  | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Low:7 |

Scenario Outline: Strong identifier matching folds a case or returns scored conflicts
	Given the mailbox "ehcp-team-shared-1" is selected
	And the scenario values are
		| key | value |
		| emailId | <emailId> |
		| conversationId | <conversationId> |
		| parentName | <parentName> |
		| parentEmail | <parentEmail> |
		| expectedMatches | <expectedMatches> |
		| expectedStatus | <expectedStatus> |
		| expectedCaseId | <expectedCaseId> |
	And the child identifiers are
		| identifier | value |
		| childName | <childName> |
		| nhsNumber | <nhsNumber> |
		| caseId | <caseId> |
		| dateOfBirth | <dateOfBirth> |
		| mosaicId | <mosaicId> |
		| dfeId | <dfeId> |
		| synergyId | <synergyId> |
	And the database is setup to contain "scenario-5-fuzzy-name-possible-matches-data.json"
	And the database contains cases "scenario-5-fuzzy-name-possible-matches-cases.json"
	And we setup mock ai extraction to return dataset "scenario-5-fuzzy-name-possible-matches-ai-extract.json"
	And we setup mock graph api to return dataset "scenario-5-fuzzy-name-possible-matches-graph-email.json"
	When the email record "scenario-5-fuzzy-name-possible-matches-queue.json" is added to the queue
	And we wait for the database processing status to be "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
	Then the email record should be retrieved matching "scenario-5-fuzzy-name-possible-matches-expected-result.json"
	And the email child should have matched case id "<expectedCaseId>"

# Expected matches are ordered id:ConfidenceBand:score entries separated by semicolons.
# Leave expectedMatches blank to assert an empty possibleCaseMatches array.
# Only no-strong-ID rows can remain PendingMatch. Strong-ID rows fold or conflict.
	Examples:
		| emailId                           | conversationId  | parentName  | parentEmail             | childName   | nhsNumber    | caseId    | dateOfBirth | mosaicId | dfeId   | synergyId | expectedStatus | expectedCaseId        | expectedMatches                                                 |
		| spec-s-nhs-fold                   | spec-thread-s01 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |           | 2020-03-01  |          |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
		| spec-s-dfe-fold                   | spec-thread-s02 | Sam Example | sam.example@example.com | Fred Bloggs |              |           | 2020-03-01  |          | D-20001 |           | Folded         | spec-case-fred-bloggs |                                                                 |
		| spec-s-mosaic-fold                | spec-thread-s03 | Sam Example | sam.example@example.com | Fred Bloggs |              |           | 2020-03-01  | M-10001  |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
		| spec-s-synergy-attribute-fold     | spec-thread-s04 | Sam Example | sam.example@example.com | Fred Bloggs |              |           | 2020-03-01  |          |         | S-1234567 | Folded         | spec-case-fred-bloggs |                                                                 |
		| spec-s-synergy-field-fold         | spec-thread-s05 | Sam Example | sam.example@example.com | Fred Bloggs |              | S-1234567 | 2020-03-01  |          |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
		| spec-s-nhs-jones-fold             | spec-thread-s06 | Sam Example | sam.example@example.com | Jones Smith | 456 789 0123 |           |             |          |         |           | Folded         | spec-case-jones-smith |                                                                 |
		| spec-s-dfe-jones-fold             | spec-thread-s07 | Sam Example | sam.example@example.com | Jones Smith |              |           |             |          | D-20002 |           | Folded         | spec-case-jones-smith |                                                                 |
		| spec-s-nhs-conflict               | spec-thread-s08 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           | 2021-03-01  |          |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
		| spec-s-dfe-conflict               | spec-thread-s09 | Sam Example | sam.example@example.com | Alice       |              |           | 2021-03-01  |          | D-20001 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
		| spec-s-mosaic-conflict            | spec-thread-s10 | Sam Example | sam.example@example.com | Alice       |              |           | 2021-03-01  | M-10001  |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
		| spec-s-synergy-attribute-conflict | spec-thread-s11 | Sam Example | sam.example@example.com | Alice       |              |           | 2021-03-01  |          |         | S-1234567 | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
		| spec-s-synergy-field-conflict     | spec-thread-s12 | Sam Example | sam.example@example.com | Alice       |              | S-1234567 | 2021-03-01  |          |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
		| spec-s-competing-ids              | spec-thread-s13 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           |             |          | D-20002 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Medium:10 |
		| spec-s-same-case-two-ids          | spec-thread-s14 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           | 2021-03-01  |          | D-20001 |           | Conflict       |                       | spec-case-fred-bloggs:High:20                                   |
		| spec-s-competing-dob-ids          | spec-thread-s15 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           | 2020-03-01  |          | D-20002 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:17;spec-case-jones-smith:Medium:17 |
		| spec-s-exact-name-wrong-dob | spec-thread-s16 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |  | 2021-03-01 |  |  |  | Conflict |  | spec-case-fred-bloggs:Medium:15 |
		| spec-s-exact-name-competing-ids | spec-thread-s17 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |  | 2020-03-01 |  | D-20002 |  | Conflict |  | spec-case-fred-bloggs:High:22;spec-case-jones-smith:Medium:17 |

Scenario Outline: Email with child information extracted and enough information should create a case
	Given the mailbox "ehcp-team-shared-1" is selected
	And the scenario values are
		| key            | value            |
		| emailId        | <emailId>        |
		| conversationId | <conversationId> |
		| parentName     | <parentName>     |
		| parentEmail    | <parentEmail>    |
	And the child identifiers are
		| identifier  | value         |
		| childName   | <childName>   |
		| nhsNumber   | <nhsNumber>   |
		| dateOfBirth | <dateOfBirth> |
		| mosaicId    | <mosaicId>    |
		| dfeId       | <dfeId>       |
		| synergyId   | <synergyId>   |
	And the database is setup to contain "case-creation-data.json"
	And we setup mock ai extraction to return dataset "case-creation-ai-extract.json"
	And we setup mock graph api to return dataset "case-creation-graph-email.json"
	When the email record "case-creation-queue.json" is added to the queue
	And we wait for the database processing status to be "Processed"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
	Then the email record should be retrieved matching "case-creation-expected-result.json"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"<emailId>-caseId" endpoint
	Then the case record should be retrieved matching "case-creation-expected-case-result.json"

Examples:
	| emailId  | conversationId | parentName  | parentEmail             | childName    | nhsNumber    | dateOfBirth | mosaicId | dfeId   | synergyId |
	| spec-c-1 | spec-thread-c1 | Sam Example | sam.example@example.com | Alex Example | 987 654 3210 | 2016-03-14  |          |         |           |
	| spec-c-1 | spec-thread-c1 | Sam Example | sam.example@example.com | Alex Example | 987 654 3210 |             |          |         |           |
	| spec-c-2 | spec-thread-c2 | Sam Example | sam.example@example.com | Alex Example |              |             |          | D-20001 |           |
	| spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex         |              |             | M-10001  | D-20001 |           |
	| spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex         |              |             | M-10001  |         |           |
	| spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex Example |              |             |          |         | S-1234567 |

Scenario: Email with no child information is discarded with a rationale
	Given the mailbox "ehcp-team-shared-1" is selected
	And the database is setup to contain "scenario-3-no-child-data.json"
	And we setup mock ai extraction to return dataset "scenario-3-no-child-ai-extract.json"
	And we setup mock graph api to return dataset "scenario-3-no-child-graph-email.json"
	When the email record "scenario-3-no-child-queue.json" is added to the queue
	And we wait for the database processing status to be "Discarded"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"scenario-3-no-child" endpoint
	Then the email record should be retrieved matching "scenario-3-no-child-expected-result.json"

Scenario: An out-of-office reply is discarded with its rationale
	Given the mailbox "ehcp-team-shared-1" is selected
	And the database is setup to contain "scenario-4-out-of-office-data.json"
	And we setup mock ai extraction to return dataset "scenario-4-out-of-office-ai-extract.json"
	And we setup mock graph api to return dataset "scenario-4-out-of-office-graph-email.json"
	When the email record "scenario-4-out-of-office-queue.json" is added to the queue
	And we wait for the database processing status to be "Discarded"
	When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"scenario-4-out-of-office" endpoint
	Then the email record should be retrieved matching "scenario-4-out-of-office-expected-result.json"

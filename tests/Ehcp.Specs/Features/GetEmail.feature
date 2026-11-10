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

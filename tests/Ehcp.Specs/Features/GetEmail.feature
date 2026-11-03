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


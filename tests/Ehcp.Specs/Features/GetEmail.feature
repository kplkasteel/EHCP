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

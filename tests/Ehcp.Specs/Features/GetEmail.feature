Feature: Retrieve email

Scenario Outline: Retrieve an existing email by id
	Given the database is setup to contain an email with id "<emailId>" and processing status "<status>"
	When a request is made to the /emails/"<emailId>" endpoint
	Then the email record should be retrieved with processing status "<status>"

Examples:
	| emailId        | status       |
	| spec-email-001 | Unprocessed  |
	| spec-email-002 | PendingMatch |
	| spec-email-003 | Folded       |
	| spec-email-004 | Conflict     |

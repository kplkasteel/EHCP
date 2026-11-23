Feature: Retrieve email

  Scenario Outline: Retrieve an existing email by id
    Given the mailbox "ehcp-team-shared-1" is selected
    And the database is setup to contain an email with id "<emailId>" and processing status "<status>"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved with processing status "<status>"

    Examples:
      | emailId        | status      |
      | spec-email-001 | Unprocessed |
      | spec-email-002 | Processed   |

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


  Scenario Outline: Email with a fuzzy-matching child folds on a Medium or High score and lists Low possible matches
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key             | value             |
      | emailId         | <emailId>         |
      | conversationId  | <conversationId>  |
      | parentName      | <parentName>      |
      | parentEmail     | <parentEmail>     |
      | expectedMatches | <expectedMatches> |
      | expectedStatus  | <expectedStatus>  |
      | expectedCaseId  | <expectedCaseId>  |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | caseId      | <caseId>      |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
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
# A Medium or High candidate (score 10 or more) folds the email into that case: Folded, expectedCaseId set, expectedMatches blank.
# Low candidates (score below 10) keep the email PendingMatch and list them.
# No candidate and no strong ID keeps the email PendingMatch with no possible matches.
# Strong-ID rows are covered by the strong identifier scenario below.


    Examples:
      | emailId                                | conversationId  | parentName  | parentEmail             | childName     | nhsNumber | dateOfBirth | mosaicId | dfeId | synergyId | caseId | expectedMatches                                         | expectedStatus | expectedCaseId        |
      | scenario-5-fuzzy-name-possible-matches | spec-thread-005 | Sam Example | sam.example@example.com | Freddy        |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-fredd-dob                       | spec-thread-f01 | Sam Example | sam.example@example.com | Fredd         |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-fred-dob                        | spec-thread-f02 | Sam Example | sam.example@example.com | Fred          |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-frederick-dob                   | spec-thread-f03 | Sam Example | sam.example@example.com | Frederick     |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-jones-dob                       | spec-thread-f04 | Sam Example | sam.example@example.com | Jones         |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-jones-smith |
      | spec-f-other-name-dob                  | spec-thread-f05 | Sam Example | sam.example@example.com | Martha        |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7 | PendingMatch   |                       |
      | spec-f-dob-only                        | spec-thread-f06 | Sam Example | sam.example@example.com |               |           | 2020-03-01  |          |       |           |        | spec-case-fred-bloggs:Low:7;spec-case-jones-smith:Low:7 | PendingMatch   |                       |
      | spec-f-fred-initial-dob                | spec-thread-f07 | Sam Example | sam.example@example.com | F. Bloggs     |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-jones-initial-dob               | spec-thread-f08 | Sam Example | sam.example@example.com | J. Smith      |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-jones-smith |
      | spec-f-fuzzy-name-only                 | spec-thread-f09 | Sam Example | sam.example@example.com | Freddy        |           |             |          |       |           |        | spec-case-fred-bloggs:Low:3                             | PendingMatch   |                       |
      | spec-f-fredd-name-only                 | spec-thread-f10 | Sam Example | sam.example@example.com | Fredd         |           |             |          |       |           |        | spec-case-fred-bloggs:Low:3                             | PendingMatch   |                       |
      | spec-f-jones-name-only                 | spec-thread-f11 | Sam Example | sam.example@example.com | Jones         |           |             |          |       |           |        | spec-case-jones-smith:Low:3                             | PendingMatch   |                       |
      | spec-f-full-name-only                  | spec-thread-f12 | Sam Example | sam.example@example.com | Fred Bloggs   |           |             |          |       |           |        | spec-case-fred-bloggs:Low:5                             | PendingMatch   |                       |
      | spec-f-jones-full-only                 | spec-thread-f13 | Sam Example | sam.example@example.com | Jones Smith   |           |             |          |       |           |        | spec-case-jones-smith:Low:5                             | PendingMatch   |                       |
      | spec-f-exact-fred-dob                  | spec-thread-f14 | Sam Example | sam.example@example.com | Fred Bloggs   |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-exact-jones-dob                 | spec-thread-f15 | Sam Example | sam.example@example.com | Jones Smith   |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-jones-smith |
      | spec-f-no-evidence                     | spec-thread-f16 | Sam Example | sam.example@example.com |               |           |             |          |       |           |        |                                                         | PendingMatch   |                       |
      | spec-f-different-dob                   | spec-thread-f17 | Sam Example | sam.example@example.com | Fredster      |           | 2021-03-01  |          |       |           |        | spec-case-fred-bloggs:Low:3                             | PendingMatch   |                       |
      | spec-f-full-lowercase-only             | spec-thread-f18 | Sam Example | sam.example@example.com | fReD bLoGgS   |           |             |          |       |           |        | spec-case-fred-bloggs:Low:5                             | PendingMatch   |                       |
      | spec-f-full-lowercase-dob              | spec-thread-f19 | Sam Example | sam.example@example.com | fReD bLoGgS   |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-different-surname-dob           | spec-thread-f20 | Sam Example | sam.example@example.com | Fred Harper   |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-fuzzy-first-name-dob            | spec-thread-f21 | Sam Example | sam.example@example.com | Freddy Bloggs |           | 2020-03-01  |          |       |           |        |                                                         | Folded         | spec-case-fred-bloggs |
      | spec-f-different-name-different-dob    | spec-thread-f22 | Sam Example | sam.example@example.com | Tom           |           | 2019-03-01  |          |       |           |        |                                                         | PendingMatch   |                       |

  Scenario Outline: Strong identifier matching folds a case or returns scored conflicts
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key             | value             |
      | emailId         | <emailId>         |
      | conversationId  | <conversationId>  |
      | parentName      | <parentName>      |
      | parentEmail     | <parentEmail>     |
      | expectedMatches | <expectedMatches> |
      | expectedStatus  | <expectedStatus>  |
      | expectedCaseId  | <expectedCaseId>  |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | caseId      | <caseId>      |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
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
# Strong-ID rows fold when the identifier matches a case, and conflict when identifiers disagree.
# A strong ID that matches no case creates a case (see the strong identifier scenario), not a PendingMatch.

    Examples:
      | emailId                           | conversationId  | parentName  | parentEmail             | childName   | nhsNumber    | caseId    | dateOfBirth | mosaicId | dfeId   | synergyId | expectedStatus | expectedCaseId        | expectedMatches                                                 |
      | spec-s-nhs-fold                   | spec-thread-s01 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |           |  2020-03-01 |          |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
      | spec-s-dfe-fold                   | spec-thread-s02 | Sam Example | sam.example@example.com | Fred Bloggs |              |           |  2020-03-01 |          | D-20001 |           | Folded         | spec-case-fred-bloggs |                                                                 |
      | spec-s-mosaic-fold                | spec-thread-s03 | Sam Example | sam.example@example.com | Fred Bloggs |              |           |  2020-03-01 | M-10001  |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
      | spec-s-synergy-attribute-fold     | spec-thread-s04 | Sam Example | sam.example@example.com | Fred Bloggs |              |           |  2020-03-01 |          |         | S-1234567 | Folded         | spec-case-fred-bloggs |                                                                 |
      | spec-s-synergy-field-fold         | spec-thread-s05 | Sam Example | sam.example@example.com | Fred Bloggs |              | S-1234567 |  2020-03-01 |          |         |           | Folded         | spec-case-fred-bloggs |                                                                 |
      | spec-s-nhs-jones-fold             | spec-thread-s06 | Sam Example | sam.example@example.com | Jones Smith | 456 789 0123 |           |             |          |         |           | Folded         | spec-case-jones-smith |                                                                 |
      | spec-s-dfe-jones-fold             | spec-thread-s07 | Sam Example | sam.example@example.com | Jones Smith |              |           |             |          | D-20002 |           | Folded         | spec-case-jones-smith |                                                                 |
      | spec-s-nhs-conflict               | spec-thread-s08 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           |  2021-03-01 |          |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
      | spec-s-dfe-conflict               | spec-thread-s09 | Sam Example | sam.example@example.com | Alice       |              |           |  2021-03-01 |          | D-20001 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
      | spec-s-mosaic-conflict            | spec-thread-s10 | Sam Example | sam.example@example.com | Alice       |              |           |  2021-03-01 | M-10001  |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
      | spec-s-synergy-attribute-conflict | spec-thread-s11 | Sam Example | sam.example@example.com | Alice       |              |           |  2021-03-01 |          |         | S-1234567 | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
      | spec-s-synergy-field-conflict     | spec-thread-s12 | Sam Example | sam.example@example.com | Alice       |              | S-1234567 |  2021-03-01 |          |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10                                 |
      | spec-s-competing-ids              | spec-thread-s13 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           |             |          | D-20002 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:10;spec-case-jones-smith:Medium:10 |
      | spec-s-same-case-two-ids          | spec-thread-s14 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           |  2021-03-01 |          | D-20001 |           | Conflict       |                       | spec-case-fred-bloggs:High:20                                   |
      | spec-s-competing-dob-ids          | spec-thread-s15 | Sam Example | sam.example@example.com | Alice       | 123 456 7890 |           |  2020-03-01 |          | D-20002 |           | Conflict       |                       | spec-case-fred-bloggs:Medium:17;spec-case-jones-smith:Medium:17 |
      | spec-s-exact-name-wrong-dob       | spec-thread-s16 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |           |  2021-03-01 |          |         |           | Conflict       |                       | spec-case-fred-bloggs:Medium:15                                 |
      | spec-s-exact-name-competing-ids   | spec-thread-s17 | Sam Example | sam.example@example.com | Fred Bloggs | 123 456 7890 |           |  2020-03-01 |          | D-20002 |           | Conflict       |                       | spec-case-fred-bloggs:High:22;spec-case-jones-smith:Medium:17   |

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
      | spec-c-1 | spec-thread-c1 | Sam Example | sam.example@example.com | Alex Example | 987 654 3210 |  2016-03-14 |          |         |           |
      | spec-c-1 | spec-thread-c1 | Sam Example | sam.example@example.com | Alex Example | 987 654 3210 |             |          |         |           |
      | spec-c-2 | spec-thread-c2 | Sam Example | sam.example@example.com | Alex Example |              |             |          | D-20001 |           |
      | spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex         |              |             | M-10001  | D-20001 |           |
      | spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex         |              |             | M-10001  |         |           |
      | spec-c-3 | spec-thread-c3 | Sam Example | sam.example@example.com | Alex Example |              |             |          |         | S-1234567 |

  Scenario Outline: Email with a child matching an existing case without conflicts folds and adds new information to the case
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key               | value               |
      | emailId           | <emailId>           |
      | conversationId    | <conversationId>    |
      | parentName        | <parentName>        |
      | parentEmail       | <parentEmail>       |
      | childParentName   | <childParentName>   |
      | childParentEmail  | <childParentEmail>  |
      | childParent2Name  | <childParent2Name>  |
      | childParent2Email | <childParent2Email> |
      | expectedCaseId    | <expectedCaseId>    |
      | expectedStatus    | Folded              |
      | expectedMatches   |                     |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
      | address     | <address>     |
      | gp          | <gp>          |
      | siblings    | <siblings>    |
      | school      | <school>      |
    And the database is setup to contain "scenario-11-identifier-fold-data.json"
    And the database contains cases "scenario-11-identifier-fold-cases.json"
    And we setup mock ai extraction to return dataset "scenario-11-identifier-fold-ai-extract.json"
    And we setup mock graph api to return dataset "scenario-11-identifier-fold-graph-email.json"
    When the email record "scenario-11-identifier-fold-queue.json" is added to the queue
    And we wait for the database processing status to be "Processed"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved matching "scenario-11-identifier-fold-expected-result.json"
    And the email child should have matched case id "<expectedCaseId>"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"<expectedCaseId>" endpoint
    Then the case record should be retrieved matching "scenario-11-identifier-fold-<expectedCase>-expected-case.json"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"spec-case-leo-grant" endpoint
    Then the case record should be retrieved matching "scenario-11-identifier-fold-leo-unchanged-expected-case.json"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"spec-case-omar-sadiq" endpoint
    Then the case record should be retrieved matching "scenario-11-identifier-fold-omar-unchanged-expected-case.json"
# Seeded cases: spec-case-ruby-evans (NHS 666 777 8889, Synergy S-6667778, Mosaic M-60001, GP Dr Shah, Parent Tom Evans, one earlier email, no DOB or DfE),
# spec-case-iris-webb (DOB 2013-12-05, Synergy S-6667781, Mosaic M-60004, Parent Carl Webb, no NHS number),
# spec-case-nora-field (DOB 2014-06-20, NHS 777 888 1112, DfE D-70003, address, GP and sibling, Parent Ann Field),
# spec-case-omar-sadiq (same DOB as Nora Field) and spec-case-leo-grant, which must never change.
# The email is Folded with no possible matches. The case gains the email and any new information, with its source email, and nothing is added twice.
# Changed information is covered by the next scenario.
# expectedCase names the scenario-11-identifier-fold-<expectedCase>-expected-case.json file.

    Examples:
      | emailId                    | conversationId  | parentName  | parentEmail             | childName  | nhsNumber    | dateOfBirth | mosaicId | dfeId   | synergyId | address    | gp      | siblings  | school           | childParentName | childParentEmail      | childParent2Name | childParent2Email       | expectedCaseId       | expectedCase      |
      | spec-u-nhs                 | spec-thread-u01 | Sam Example | sam.example@example.com | Ruby Evans | 666 777 8889 |             |          |         |           |            |         |           |                  |                 |                       |                  |                         | spec-case-ruby-evans | ruby-email-only   |
      | spec-u-synergy             | spec-thread-u02 | Sam Example | sam.example@example.com | Ruby Evans |              |             |          |         | S-6667778 |            |         |           |                  |                 |                       |                  |                         | spec-case-ruby-evans | ruby-email-only   |
      | spec-u-mosaic              | spec-thread-u03 | Sam Example | sam.example@example.com | Ruby Evans |              |             | M-60001  |         |           |            |         |           |                  |                 |                       |                  |                         | spec-case-ruby-evans | ruby-email-only   |
      | spec-u-new-dob             | spec-thread-u04 | Sam Example | sam.example@example.com | Ruby Evans | 666 777 8889 | 2015-07-19  |          |         |           |            |         |           |                  |                 |                       |                  |                         | spec-case-ruby-evans | ruby-dob          |
      | spec-u-new-parent          | spec-thread-u05 | Sam Example | sam.example@example.com | Ruby Evans | 666 777 8889 |             |          |         |           |            |         |           |                  | Tom Evans       | tom.evans@example.com | Megan Evans      | megan.evans@example.com | spec-case-ruby-evans | ruby-parent       |
      | spec-u-new-attributes      | spec-thread-u06 | Sam Example | sam.example@example.com | Ruby Evans | 666 777 8889 |             |          | D-70001 |           |            | Dr Shah |           | Oakfield Primary |                 |                       |                  |                         | spec-case-ruby-evans | ruby-attributes   |
      | spec-u-all-new             | spec-thread-u07 | Sam Example | sam.example@example.com | Ruby Evans | 666 777 8889 | 2015-07-19  |          | D-70001 |           |            | Dr Shah |           | Oakfield Primary | Tom Evans       | tom.evans@example.com | Megan Evans      | megan.evans@example.com | spec-case-ruby-evans | ruby-all          |
      | spec-u-fills-nhs           | spec-thread-u08 | Sam Example | sam.example@example.com | Iris Webb  | 666 777 1110 |             | M-60004  |         |           |            |         |           |                  |                 |                       |                  |                         | spec-case-iris-webb  | iris-nhs          |
      | spec-u-parent-match        | spec-thread-u09 | Sam Example | sam.example@example.com | Ruby Evans |              |             |          |         |           |            |         |           | Oakfield Primary | Tom Evans       | tom.evans@example.com |                  |                         | spec-case-ruby-evans | ruby-parent-match |
      | spec-u-new-id-parent-match | spec-thread-u10 | Sam Example | sam.example@example.com | Ruby Evans |              |             |          | D-70001 |           |            |         |           |                  | Tom Evans       | tom.evans@example.com |                  |                         | spec-case-ruby-evans | ruby-new-dfe      |
      | spec-u-one-high            | spec-thread-u11 | Sam Example | sam.example@example.com | Nora Field |              | 2014-06-20  |          |         |           | 1 Oak Road | Dr Lane | Max Field | Hillside Academy |                 |                       |                  |                         | spec-case-nora-field | nora-school       |
      | spec-u-one-medium          | spec-thread-u12 | Sam Example | sam.example@example.com | Nora Field |              | 2014-06-20  |          |         |           |            |         |           |                  | Joe Field       | joe.field@example.com |                  |                         | spec-case-nora-field | nora-parent       |
      | spec-u-single-medium       | spec-thread-u13 | Sam Example | sam.example@example.com | Iris Webb  |              | 2013-12-05  |          |         |           |            |         |           | Hillside Academy |                 |                       |                  |                         | spec-case-iris-webb  | iris-school       |

  Scenario Outline: Email with a child matching an existing case without conflicts folds and keeps the latest information
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key               | value               |
      | emailId           | <emailId>           |
      | conversationId    | <conversationId>    |
      | parentName        | <parentName>        |
      | parentEmail       | <parentEmail>       |
      | childParentName   | <childParentName>   |
      | childParentEmail  | <childParentEmail>  |
      | childParentPhone  | <childParentPhone>  |
      | expectedCaseId    | <expectedCaseId>    |
      | expectedStatus    | Folded              |
      | expectedMatches   |                     |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | dateOfBirth | <dateOfBirth> |
      | dfeId       | <dfeId>       |
      | address     | <address>     |
      | gp          | <gp>          |
      | siblings    | <siblings>    |
      | school      | <school>      |
    And the database is setup to contain "scenario-11-identifier-fold-data.json"
    And the database contains cases "scenario-11-changed-information-cases.json"
    And we setup mock ai extraction to return dataset "scenario-11-identifier-fold-ai-extract.json"
    And we setup mock graph api to return dataset "scenario-11-identifier-fold-graph-email.json"
    When the email record "scenario-11-identifier-fold-queue.json" is added to the queue
    And we wait for the database processing status to be "Processed"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved matching "scenario-11-identifier-fold-expected-result.json"
    And the email child should have matched case id "<expectedCaseId>"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"<expectedCaseId>" endpoint
    Then the case record should be retrieved matching "scenario-11-changed-information-<expectedCase>-expected-case.json"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/cases/"spec-case-lena-frost" endpoint
    Then the case record should be retrieved matching "scenario-11-changed-information-lena-unchanged-expected-case.json"
# Seeded cases: spec-case-freya-nolan (NHS 555 666 7771, DOB 2014-09-02, DfE D-80001, school Oakfield Primary, address 1 Elm Road, GP Dr Shah,
# Parent Paula Nolan; everything from an email of 2026-10-01),
# spec-case-hugo-marsh (NHS 555 666 7782, DfE D-80002 and GP Dr Lane from an email of 2026-10-01, school Hillside Academy and
# Parent Rita Marsh from an email of 2026-12-01, no address) and spec-case-lena-frost, which must never change.
# The email processed is dated 2026-11-01, so it is newer than Freya's information and older than some of Hugo's.
# The latest email wins: a newer value replaces the old one, an older email never overwrites newer information,
# and information left out of the email is kept.

    Examples:
      | emailId                  | conversationId  | parentName  | parentEmail             | childName     | nhsNumber    | dateOfBirth | dfeId   | address       | gp      | siblings  | school           | childParentName | childParentEmail      | childParentPhone | expectedCaseId        | expectedCase          |
      | spec-w-dfe               | spec-thread-w01 | Sam Example | sam.example@example.com | Freya Nolan   |              |             | D-80001 |               |         |           |                  |                 |                       |                  | spec-case-freya-nolan | freya-email-only      |
      | spec-w-same-information  | spec-thread-w02 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         | 1 Elm Road    | Dr Shah |           | Oakfield Primary | Paula Nolan     | paula.old@example.com | 07700 900101     | spec-case-freya-nolan | freya-email-only      |
      | spec-w-new-school        | spec-thread-w03 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         |               |         |           | Hillside Academy |                 |                       |                  | spec-case-freya-nolan | freya-school          |
      | spec-w-new-address-gp    | spec-thread-w04 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         | 22 Park Lane  | Dr Lane |           |                  |                 |                       |                  | spec-case-freya-nolan | freya-address-gp      |
      | spec-w-new-parent-email  | spec-thread-w05 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         |               |         |           |                  | Paula Nolan     | paula.new@example.com | 07700 900101     | spec-case-freya-nolan | freya-parent-email    |
      | spec-w-new-parent-phone  | spec-thread-w06 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         |               |         |           |                  | Paula Nolan     | paula.old@example.com | 07700 900999     | spec-case-freya-nolan | freya-parent-phone    |
      | spec-w-new-name          | spec-thread-w07 | Sam Example | sam.example@example.com | Freya Carter  | 555 666 7771 |             |         |               |         |           |                  |                 |                       |                  | spec-case-freya-nolan | freya-name            |
      | spec-w-all-changed       | spec-thread-w08 | Sam Example | sam.example@example.com | Freya Carter  | 555 666 7771 |             |         | 22 Park Lane  | Dr Lane |           | Hillside Academy | Paula Nolan     | paula.new@example.com | 07700 900999     | spec-case-freya-nolan | freya-all             |
      | spec-w-changed-and-new   | spec-thread-w09 | Sam Example | sam.example@example.com | Freya Nolan   | 555 666 7771 |             |         |               |         | Max Nolan | Hillside Academy |                 |                       |                  | spec-case-freya-nolan | freya-changed-and-new |
      | spec-w-left-out          | spec-thread-w10 | Sam Example | sam.example@example.com |               | 555 666 7771 |             |         |               |         |           |                  | Paula Nolan     |                       |                  | spec-case-freya-nolan | freya-email-only      |
      | spec-w-parent-match      | spec-thread-w11 | Sam Example | sam.example@example.com | Freya Nolan   |              |             |         |               |         |           |                  | Paula Nolan     | paula.new@example.com | 07700 900101     | spec-case-freya-nolan | freya-parent-email    |
      | spec-w-no-identifier     | spec-thread-w12 | Sam Example | sam.example@example.com | Freya Nolan   |              | 2014-09-02  |         |               |         |           | Hillside Academy |                 |                       |                  | spec-case-freya-nolan | freya-school          |
      | spec-w-older-school      | spec-thread-w13 | Sam Example | sam.example@example.com | Hugo Marsh    | 555 666 7782 |             |         |               |         |           | Oakfield Primary |                 |                       |                  | spec-case-hugo-marsh  | hugo-email-only       |
      | spec-w-older-parent      | spec-thread-w14 | Sam Example | sam.example@example.com | Hugo Marsh    | 555 666 7782 |             |         |               |         |           |                  | Rita Marsh      | rita.old@example.com  | 07700 900111     | spec-case-hugo-marsh  | hugo-email-only       |
      | spec-w-older-name        | spec-thread-w15 | Sam Example | sam.example@example.com | Hugo Marshall | 555 666 7782 |             |         |               |         |           |                  |                 |                       |                  | spec-case-hugo-marsh  | hugo-email-only       |
      | spec-w-older-new-address | spec-thread-w16 | Sam Example | sam.example@example.com | Hugo Marsh    | 555 666 7782 |             |         | 9 Mill Street |         |           |                  |                 |                       |                  | spec-case-hugo-marsh  | hugo-new-address      |
      | spec-w-older-and-newer   | spec-thread-w17 | Sam Example | sam.example@example.com | Hugo Marsh    | 555 666 7782 |             |         |               | Dr Shah |           | Oakfield Primary |                 |                       |                  | spec-case-hugo-marsh  | hugo-gp               |

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


  Scenario Outline: Email with a fuzzy-matching child folds on a parent match and stays pending without one
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key              | value              |
      | emailId          | <emailId>          |
      | conversationId   | <conversationId>   |
      | parentName       | <parentName>       |
      | parentEmail      | <parentEmail>      |
      | childParentName  | <childParentName>  |
      | childParentEmail | <childParentEmail> |
      | expectedMatches  | <expectedMatches>  |
      | expectedStatus   | <expectedStatus>   |
      | expectedCaseId   | <expectedCaseId>   |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | caseId      | <caseId>      |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
    And the database is setup to contain "scenario-7-low-match-relationships-data.json"
    And the database contains cases "scenario-7-low-match-relationships-cases.json"
    And we setup mock ai extraction to return dataset "scenario-7-low-match-relationships-ai-extract.json"
    And we setup mock graph api to return dataset "scenario-7-low-match-relationships-graph-email.json"
    When the email record "scenario-7-low-match-relationships-queue.json" is added to the queue
    And we wait for the database processing status to be "Processed"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved matching "scenario-7-low-match-relationships-expected-result.json"
    And the email child should have matched case id "<expectedCaseId>"
# Seeded cases: spec-case-oliver-hughes (Oliver Hughes, DOB 2018-06-12, NHS 222 333 4445, Parent Grace Hughes, grace.hughes@example.com)
# and spec-case-amelia-khan (Amelia Khan, DOB 2018-06-12, NHS 222 333 5556, Parent Sara Khan, sara.khan@example.com), which must not match.
# parentName / parentEmail are the email sender. childParentName / childParentEmail are the extracted child's Parent relationship.
# A parent match (name or email) folds the email into the case it belongs to. The confidence band comes from the score.
# Sara Khan belongs to Amelia Khan. Amelia does not match on name or DOB, so that parent does not fold the email into Amelia.
# Without a parent match the email stays PendingMatch and lists the Low candidate. Low is a score below 10.
# Expected matches are ordered id:ConfidenceBand:score entries separated by semicolons.


    Examples:
      | emailId                     | conversationId  | parentName  | parentEmail             | childName | nhsNumber | caseId | dateOfBirth | mosaicId | dfeId | synergyId | childParentName | childParentEmail         | expectedStatus | expectedCaseId          | expectedMatches               |
      | spec-l-name-parent-email    | spec-thread-l01 | Sam Example | sam.example@example.com | Oli       |           |        |             |          |       |           | Grace Hughes    | grace.hughes@example.com | Folded         | spec-case-oliver-hughes |                               |
      | spec-l-name-parent-name     | spec-thread-l02 | Sam Example | sam.example@example.com | Oliver    |           |        |             |          |       |           | Grace Hughes    | other@example.com        | Folded         | spec-case-oliver-hughes |                               |
      | spec-l-name-parent-only     | spec-thread-l03 | Sam Example | sam.example@example.com | Oli       |           |        |             |          |       |           | Mark Hughes     | grace.hughes@example.com | Folded         | spec-case-oliver-hughes |                               |
      | spec-l-name-initials-parent | spec-thread-l04 | Sam Example | sam.example@example.com | Oliver H  |           |        |             |          |       |           | Grace Hughes    | other@example.com        | Folded         | spec-case-oliver-hughes |                               |
      | spec-l-name-other-parent    | spec-thread-l05 | Sam Example | sam.example@example.com | Oli       |           |        |             |          |       |           | Sara Khan       | sara.khan@example.com    | PendingMatch   |                         | spec-case-oliver-hughes:Low:3 |

 
  Scenario Outline: Email with a child whose identifier differs from the matched case is a conflict
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key              | value              |
      | emailId          | <emailId>          |
      | conversationId   | <conversationId>   |
      | parentName       | <parentName>       |
      | parentEmail      | <parentEmail>      |
      | childParentName  | <childParentName>  |
      | childParentEmail | <childParentEmail> |
      | expectedMatches  | <expectedMatches>  |
      | expectedStatus   | <expectedStatus>   |
      | expectedCaseId   | <expectedCaseId>   |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | caseId      | <caseId>      |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
    And the database is setup to contain "scenario-8-low-match-identifier-mismatch-data.json"
    And the database contains cases "scenario-8-low-match-identifier-mismatch-cases.json"
    And we setup mock ai extraction to return dataset "scenario-8-low-match-identifier-mismatch-ai-extract.json"
    And we setup mock graph api to return dataset "scenario-8-low-match-identifier-mismatch-graph-email.json"
    When the email record "scenario-8-low-match-identifier-mismatch-queue.json" is added to the queue
    And we wait for the database processing status to be "Processed"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved matching "scenario-8-low-match-identifier-mismatch-expected-result.json"
    And the email child should have matched case id "<expectedCaseId>"
# Seeded cases: spec-case-isla-patel (Isla Patel, DOB 2017-09-23, NHS 333 444 5556, Synergy S-3334445, DfE D-40001, Mosaic M-30001,
# Parent Raj Patel, raj.patel@example.com) and spec-case-ivy-chen (Ivy Chen, DOB 2017-09-23, NHS 333 444 6667, Parent Lin Chen), which must not match.
# Each row gives the child one identifier that differs from spec-case-isla-patel. The name and parent match that case.
# A strong ID of the same type with a different value is a conflict (rule 5a): Conflict, no matched case, spec-case-isla-patel listed as a candidate.
# The candidate score is the name and parent score: 7, or 5 when the parent email differs.



    Examples:
      | emailId                | conversationId  | parentName  | parentEmail             | childName | nhsNumber    | caseId    | dateOfBirth | mosaicId | dfeId   | synergyId | childParentName | childParentEmail      | expectedStatus | expectedCaseId | expectedMatches            |
      | spec-l-diff-nhs        | spec-thread-l06 | Sam Example | sam.example@example.com | Isla      | 999 888 7776 |           |             |          |         |           | Raj Patel       | raj.patel@example.com | Conflict       |                | spec-case-isla-patel:Low:7 |
      | spec-l-diff-mosaic     | spec-thread-l07 | Sam Example | sam.example@example.com | Isla      |              |           |             | M-39999  |         |           | Raj Patel       | raj.patel@example.com | Conflict       |                | spec-case-isla-patel:Low:7 |
      | spec-l-diff-dfe        | spec-thread-l08 | Sam Example | sam.example@example.com | Isla      |              |           |             |          | D-49999 |           | Raj Patel       | raj.patel@example.com | Conflict       |                | spec-case-isla-patel:Low:7 |
      | spec-l-diff-synergy    | spec-thread-l09 | Sam Example | sam.example@example.com | Isla      |              |           |             |          |         | S-3999999 | Raj Patel       | raj.patel@example.com | Conflict       |                | spec-case-isla-patel:Low:7 |
      | spec-l-diff-caseid     | spec-thread-l10 | Sam Example | sam.example@example.com | Isla      |              | S-3999999 |             |          |         |           | Raj Patel       | raj.patel@example.com | Conflict       |                | spec-case-isla-patel:Low:7 |
      | spec-l-diff-nhs-parent | spec-thread-l11 | Sam Example | sam.example@example.com | Isla      | 999 888 7776 |           |             |          |         |           | Raj Patel       | other@example.com     | Conflict       |                | spec-case-isla-patel:Low:5 |


  Scenario Outline: Email with a child matching on date of birth folds on a parent match and stays pending without one
    Given the mailbox "ehcp-team-shared-1" is selected
    And the scenario values are
      | key              | value              |
      | emailId          | <emailId>          |
      | conversationId   | <conversationId>   |
      | parentName       | <parentName>       |
      | parentEmail      | <parentEmail>      |
      | childParentName  | <childParentName>  |
      | childParentEmail | <childParentEmail> |
      | expectedMatches  | <expectedMatches>  |
      | expectedStatus   | <expectedStatus>   |
      | expectedCaseId   | <expectedCaseId>   |
    And the child identifiers are
      | identifier  | value         |
      | childName   | <childName>   |
      | nhsNumber   | <nhsNumber>   |
      | caseId      | <caseId>      |
      | dateOfBirth | <dateOfBirth> |
      | mosaicId    | <mosaicId>    |
      | dfeId       | <dfeId>       |
      | synergyId   | <synergyId>   |
    And the database is setup to contain "scenario-9-low-match-dob-data.json"
    And the database contains cases "scenario-9-low-match-dob-cases.json"
    And we setup mock ai extraction to return dataset "scenario-9-low-match-dob-ai-extract.json"
    And we setup mock graph api to return dataset "scenario-9-low-match-dob-graph-email.json"
    When the email record "scenario-9-low-match-dob-queue.json" is added to the queue
    And we wait for the database processing status to be "Processed"
    When a request is made to the /mailbox/"ehcp-team-shared-1"/emails/"<emailId>" endpoint
    Then the email record should be retrieved matching "scenario-9-low-match-dob-expected-result.json"
    And the email child should have matched case id "<expectedCaseId>"
# Seeded cases: spec-case-noah-williams (Noah Williams, DOB 2019-05-05, NHS 444 555 6667, Parent Emma Williams, emma.williams@example.com)
# and spec-case-zara-ahmed (Zara Ahmed, DOB 2016-01-20, NHS 444 555 7778, Parent Omar Ahmed), which must not match.
# Leave childParentName / childParentEmail blank for no extracted relationships.
# A date of birth match with a parent match folds the email into spec-case-noah-williams.
# A date of birth match with no parent match stays PendingMatch and lists the Low candidate. Low is a score below 10.
# Expected matches are ordered id:ConfidenceBand:score entries separated by semicolons.


    Examples:
      | emailId                 | conversationId  | parentName  | parentEmail             | childName | nhsNumber | caseId | dateOfBirth | mosaicId | dfeId | synergyId | childParentName | childParentEmail          | expectedStatus | expectedCaseId          | expectedMatches               |
      | spec-l-dob-other-name   | spec-thread-l12 | Sam Example | sam.example@example.com | Leo       |           |        | 2019-05-05  |          |       |           |                 |                           | PendingMatch   |                         | spec-case-noah-williams:Low:7 |
      | spec-l-dob-no-name      | spec-thread-l13 | Sam Example | sam.example@example.com |           |           |        | 2019-05-05  |          |       |           |                 |                           | PendingMatch   |                         | spec-case-noah-williams:Low:7 |
      | spec-l-dob-parent-email | spec-thread-l14 | Sam Example | sam.example@example.com | Leo       |           |        | 2019-05-05  |          |       |           | Kate Brown      | emma.williams@example.com | Folded         | spec-case-noah-williams |                               |
      | spec-l-dob-parent-name  | spec-thread-l15 | Sam Example | sam.example@example.com | Leo       |           |        | 2019-05-05  |          |       |           | Emma Williams   | other@example.com         | Folded         | spec-case-noah-williams |                               |

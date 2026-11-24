# Pipeline build and test: regression test checklist

Suggested tests after any change to `pr-build-and-test.yml`, `templates/`, `scripts/` or `Rakefile`.

## Pipeline steps

1. Install step fails: Tool install ❌, later steps skipped, comment still posted.
1. Build fails (Rake go): Build ❌, Unit and Specs ⚠️ Not run.
1. Tool install row sits above Build in the comment.
1. Skipped install steps do not count toward Tool install time.

## Build output

1. Build warning: Build shows "⚠️ Passed with N warning(s)" and lists the warnings.
1. Build error: Build shows ❌ and lists the errors.

## Test results

1. Unit test failure: Unit row ❌ with the failing test name listed.
1. Specs failure: Specs row ❌ with the failing scenario listed.
1. All tests pass: Unit and Specs rows ✅ with counts.

## PR comment lifecycle

1. Second run on the same PR: earlier comment gets the superseded reply and is resolved; human replies kept.
1. Passing run after a failed run: the earlier failed thread is superseded.
1. Bad token or wrong repo ID: script warns (no false "Posted").
1. `-TidyOldThreads` (local): latest comment deleted only if it has no replies.

## Local

1. `rake go` without `TF_BUILD`: runs as before, no pipeline output or TRX logging.

## Variables

1. Flags (`RakeStarted`, `UnitTestsStarted`, `SpecsStarted`) default to `false`, so the summary step never gets an undefined macro.

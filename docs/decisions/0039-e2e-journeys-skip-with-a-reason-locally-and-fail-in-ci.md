# 0039 — The browser journeys skip with a reason when a prerequisite is missing, and CI turns the skip into a failure; the unit job runs the unit projects only

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** testing-strategy §3 (what runs when: E2E is its own layer and never in the unit job), §9 (every committed test entry point runs in some CI context); e2e-acceptance-testing §2 (a skip is visible with its reason, and a silent pass is never allowed), §6 (CI wiring); blazor-web-app §13 (the unit job must not need a browser); AGENTS.md "Frozen contracts" and "Warnings are errors"

## Context

A browser suite needs Chromium and Docker. A developer's machine may have neither, and a unit run must never start a browser. A missing prerequisite must also never turn the suite green: the e2e guide's central point is that a skip can pass as a pass.

## Decision

- **Skip with a reason.** When Chromium cannot be launched, or no estate can be reached or started, every journey calls `Assert.Skip` with the reason. The same convention as the PostgreSQL-gated tests (`TEST_POSTGRES`).
- **Fail in CI.** `MIROCLONE_E2E_REQUIRED=1`, set by the `e2e` job, turns the same gap into a failed fixture, so the job cannot pass with every journey skipped.
- **The unit job runs each unit project by name.** The `pr-validation` test step runs every `tests/*.csproj` except `MiroClone.Web.E2E` and `MiroClone.TestSupport` (a library). A name filter was tried first: under Microsoft.Testing.Platform a project whose tests are all filtered out exits with code 8, which fails the step.
- **The `e2e` job** runs after the build, installs Chromium with `playwright install --with-deps chromium`, and runs the project with `--no-build`. It is a separate job, so the unit job's time and its pass or fail are unaffected by the browser.

## Consequences

- A developer who runs the suite without Docker or Chromium sees the reason on every journey, not a green result.
- The guard depends on `MIROCLONE_E2E_REQUIRED` being set in CI. The workflow sets it, and the PR body says so.
- A new unit project must be a `tests/*.csproj` with tests to run in the unit job; that is the only convention the step relies on.

# Ticket UI and end-to-end tests

Owner: Senul Mintharu Somarathna, IT24101497.
Tool: Playwright with JavaScript. Scope: the Ticket service through the real frontend.
Target: an existing Azure deployment or local stack, selected with `UI_BASE_URL`.
This test project does not deploy services.

## What the final suite checks

| ID | Check |
|---|---|
| UI-001 | Login page shows the required controls |
| UI-004 | Employee creates one ticket, sees it after refresh, Admin assigns it to the supplied Agent if needed, Agent sees that same ticket after refresh |
| UI-005 | A nonexistent account is rejected and remains logged out |
| UI-006 | Missing issue type is rejected by the form |
| UI-007 | Missing description is rejected by the form |
| UI-008 | Whitespace-only issue type and description are rejected |
| UI-009 | Employee is redirected away from the Agent queue to their own form |

These are **7 test cases**. Journey steps, screenshots and repeated runs are not
additional cases. UI-002 (Employee only) and UI-003 (existing-ticket Agent check)
remain available as separate diagnostic tests; they are not repeated in the final suite.
No result is claimed until that test has actually run against the target.

## Windows: run one step at a time

Requirements: Node.js 22+, installed Microsoft Edge, and a running application.
From this `testing/ui/` folder in PowerShell:

```powershell
npm.cmd ci
$env:UI_BASE_URL = "https://YOUR-FRONTEND.azurecontainerapps.io"
npm.cmd run test:journey
```

Enter QA Employee, Agent and Admin credentials in the private terminal prompts.
Passwords are hidden and passed only in child-process environment variables.
The command uses Edge on Windows and checks/installs the FFmpeg helper if needed.
It runs UI-004 only. The Agent must be in rotation; use controlled QA accounts.

Once the journey works, run all 7 cases in one report:

```powershell
npm.cmd run test:suite
npm.cmd run report
```

Each journey invocation creates **one real labelled QA ticket**. All three logins
are checked before creation. Creation has no automatic retry; its returned ID is
recorded even on a partial failure. Do not blindly rerun after such a failure.
There is no Ticket delete API in the checked source, so the QA ticket remains.
Normal notifications and assignment events may occur.

The Admin changes **only that newly created QA ticket's assignment**, and only if
it went to another Agent. Existing tickets, rotation membership and status are
not changed. The report records the initial owner, final owner and assignment
method. A manual reassignment result does not prove automatic assignment to the
chosen Agent. The three roles use isolated browser sessions.

The form validation cases expect zero create requests. Unexpected create attempts
are blocked by the test to avoid storing invalid data, and still cause failure.
UI-009 checks the browser route guard; it does not claim backend security coverage.

## Reports and retained history

The latest report is under `reports/html/`, with JUnit, JSON and selected screenshots
under `reports/`. `npm.cmd run report` serves it locally until Ctrl+C.
Every completed run copies the whole report to a dated `reports-history/` folder:

```powershell
npm.cmd run report:history
```

Choose a run number. Old reports overwritten before history was added cannot be
recovered by this feature. Back up the final report folder for submission.

Authenticated traces, videos and automatic screenshots are disabled. Selected
post-login screenshots are attached deliberately. A first-position reporter
redacts known QA credentials from report events and failure context. API
diagnostics contain only actor labels, methods, paths, status codes and failure
flags. Keep reports private: QA ticket details/contact information may be visible.
Reports, report history, dependencies and local `.env` files are ignored by Git.

## Other commands

```powershell
npm.cmd run test:headed -- login-smoke.spec.js
npm.cmd run test:employee
npm.cmd run test:agent
npm.cmd run test:suite -- --headless
npm.cmd run test:list
```

`test:agent` reads the newest saved successful QA ticket evidence for this target
before Playwright clears the latest artifacts. Use its actual assigned Agent.
A different Agent is a setup mismatch, not proof of an application defect.

## Headless and GitHub Actions

For noninteractive runs supply these environment variables privately:
`UI_BASE_URL`, `UI_EMPLOYEE_EMAIL`, `UI_EMPLOYEE_PASSWORD`, `UI_AGENT_EMAIL`,
`UI_AGENT_PASSWORD`, `UI_ADMIN_EMAIL`, `UI_ADMIN_PASSWORD`.

```bash
# From the repository root in Git Bash, with the variables set privately:
./scripts/run-tests.sh ui
```

`testing/ui/run.sh` installs locked dependencies and Chromium, then `npm test`
runs the final 7 cases headlessly. Missing credentials fail clearly; no placeholder
success or skipped journey is reported. Assertions fail the runner with a nonzero exit.

The owned `.github/workflows/testing-ui.yml` accepts the six credential secrets,
reads the repository Actions variable `UI_BASE_URL`, and uploads the report even
on failure. The shared pipeline owner must configure the secrets and pass them to
the reusable workflow (for example `secrets: inherit`). The shared pipeline has
not been changed here. A successful local run does not prove that CI has run.

## Main files

- `tests/`: test cases and explicit journey steps.
- `pages/`: reusable UI actions and locators.
- `helpers/session-helpers.js`: identity checks and safe request diagnostics.
- `scripts/run-suite.js`: private prompts and suite selection.
- `playwright.config.js`: browsers, timeouts, output and reporters.
- `reporters/`: credential redaction and automatic report history.

The Azure deployed commit is not independently verified by these browser checks.
Tests use real UI actions and services; the only network interception in the final
suite blocks an unexpected invalid form submission. There are no fixed sleeps.

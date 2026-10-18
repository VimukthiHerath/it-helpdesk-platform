# Senul's UI test handoff

Source branch: `testing/ui`, based on `testing/base`.
Only copy/review `testing/ui/` and `.github/workflows/testing-ui.yml`.
No commits, pushes, shared pipeline edits or GitHub Actions runs were made here.

## Verified local result

On 18 October 2026, all 7 final-suite cases passed against the existing
proudsmoke Central India deployment in Microsoft Edge: 41.1 seconds,
0 failed, 0 flaky and 0 skipped.

This was a Playwright run on Senul's PC targeting Azure. It was not a GitHub
Actions run and does not establish a passing Linux/Chromium run yet.
The HTML report, JSON/JUnit results and attachments are retained locally.
The deployed Azure commit was not independently verified.

## Pipeline owner: connect this layer

1. Include the UI source and its owned workflow in the team's testing branch.
   Do not copy dependencies, credentials, generated reports or report history
   into Git. Keep the relative repository paths from the source ZIP.
2. In repository Settings > Secrets and variables > Actions, add the Actions
   variable `UI_BASE_URL` with this origin (no `/login` suffix):

   `https://it-helpdesk-frontend.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io`

3. Privately configure these six repository Actions secrets using the QA
   Employee, Agent2 and Administrator accounts. Agent2 must be in rotation:

   - `UI_EMPLOYEE_EMAIL`
   - `UI_EMPLOYEE_PASSWORD`
   - `UI_AGENT_EMAIL`
   - `UI_AGENT_PASSWORD`
   - `UI_ADMIN_EMAIL`
   - `UI_ADMIN_PASSWORD`

   No passwords are included in the source package. Share them only through the
   team's private credential channel or enter them directly in GitHub settings.

4. In the shared `.github/workflows/testing-pipeline.yml`, the UI job needs
   `secrets: inherit`. It currently calls the workflow without passing secrets:

   ```yaml
   ui:
     name: UI / end-to-end
     uses: ./.github/workflows/testing-ui.yml
     secrets: inherit
   ```

   This shared file belongs to the pipeline owner and was not edited by Senul.
   Named mappings of just these six secrets are also supported.

5. While Azure is running, run the team's Testing Pipeline. The owned UI workflow
   installs Node 22, locked dependencies and Chromium, executes the final suite,
   and uploads `testing-ui-results`, including a failing report if a test fails.
   Review all 7 cases and download the artifact; save the run URL for submission.

## What this run changes

Each final-suite run creates one uniquely labelled QA ticket. All three logins
are checked before creation. If automatic assignment chooses another Agent,
the QA Admin reassigns only this new ticket to the supplied QA Agent. The report
records whether manual reassignment was needed. It does not claim automatic
assignment to Agent2 when an Admin performed that step.

No other ticket, rotation membership or ticket status is changed. No delete API
exists in the checked source, so the QA ticket is retained. Assignment and
notification events may occur. There are no automatic creation retries.

## Local demo

From `testing/ui/`, with Node 22+ and Edge installed:

```powershell
npm.cmd ci
$env:UI_BASE_URL = "https://it-helpdesk-frontend.proudsmoke-64e7a7a1.centralindia.azurecontainerapps.io"
npm.cmd run test:suite
npm.cmd run report
```

The suite privately prompts for the three QA accounts. Existing local reports
remain in dated `reports-history/` folders. `npm.cmd run report:history` lists
them. For a headless local run use `test:suite -- --headless` after installing
Chromium with `npx.cmd playwright install chromium`.

The report ZIP is private evidence, separate from the source ZIP. Extract the
whole report ZIP and serve its HTML folder with Playwright; do not extract only
`index.html`, since its screenshot attachments are separate files.

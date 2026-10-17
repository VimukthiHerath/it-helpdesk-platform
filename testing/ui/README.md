# UI / end-to-end testing (Senul)

**Goal:** automate the ticket journey in a real browser, across the frontend and the services behind it.

## First steps
1. Pick the tool (Playwright is a good fit for a React app) and create the test project inside `testing/ui/`.
2. Decide the target and write it down here. The tests need a running system:
   - the deployed Azure app: start it with `./scripts/start-all.sh` and use its address; or
   - a local stack: `docker compose up -d`, the services with `dotnet run`, and `npm start` in `frontend/`.
3. Automate the main flow:
   - an **employee** logs in, creates a ticket, and sees it in My tickets
   - an **agent** logs in and sees assigned tickets in their queue
4. Read the address and the test accounts from **environment variables**, never from committed files. Passwords must not be committed.
5. Make `testing/ui/run.sh` install what it needs, run the tests headless, and write the HTML report to `testing/ui/reports/`.

## Done looks like
Run the script, watch (or show a video of) the browser flow, and show the report.

## Be ready to explain
UI test versus end-to-end test; locators and why they should be stable; auto-waiting; the page object pattern; why UI tests are flaky and how to reduce that.

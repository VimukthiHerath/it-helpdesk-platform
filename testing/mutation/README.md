# Mutation testing (Rashaad)

**Goal:** measure how good the Ticket unit tests really are, by changing the code on purpose and checking the tests notice.

## First steps
1. Install the tool: `dotnet tool install --global dotnet-stryker`.
2. Try a first run from the service folder (the config is a **starting point and has not been run yet**; fix it if the tool complains):
   ```bash
   cd services/ticket/Ticket.Api
   dotnet stryker -f ../../../testing/mutation/stryker-config.json
   ```
3. Open the HTML report. Note the **baseline mutation score** before Shamim's new tests exist.
4. When Shamim's tests land, run again. Find the **surviving** mutants and say why they survived.
5. Write extra tests (or ask Shamim) to kill some survivors, and show the score going up.
6. Make `testing/mutation/run.sh` run Stryker and write its report to `testing/mutation/reports/`. In `testing-mutation.yml`, add the install step and the MySQL service the Ticket tests need (copy them from `testing-unit.yml`).

## Done looks like
Show the report, one killed mutant, one survivor and the score before and after.

## Be ready to explain
What a mutant is; killed, survived, timed out and no-coverage; mutation score; equivalent mutants; why high code coverage does not mean good tests.

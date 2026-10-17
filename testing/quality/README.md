# Code quality and metrics (Vimukthi)

**Goal:** show automated static analysis and quality indicators for the Ticket service: coverage, code smells, technical debt, duplication and ratings.

## What already exists
- SonarCloud analyses the whole repository on every pull request (`.github/workflows/sonarcloud.yml`), with a quality gate.
- Coverage is produced by coverlet in the unit tests.

## First steps
1. Open the SonarCloud project and find the Ticket service folder (`services/ticket`). Write down its current metrics.
2. Write `testing/quality/run.sh` so it collects the Ticket service's indicators and prints them in a table: coverage, bugs, vulnerabilities, code smells, technical debt, duplication and the quality gate result. The SonarCloud API is public for this project (`https://sonarcloud.io/api/measures/component_tree`).
3. Add an HTML coverage report from the unit tests (for example with ReportGenerator) saved under `testing/quality/reports/`.
4. Fix a few real Ticket findings, to show the metrics improving.
5. Decide when the script should fail, for example if the quality gate is not OK.

## Done looks like
Run the script, show the metrics table and the SonarCloud dashboard, and show one issue fixed with the number moving.

## Be ready to explain
Code smell, bug and vulnerability; technical debt; the quality gate; line and branch coverage; cyclomatic complexity; duplication; why metrics can mislead.

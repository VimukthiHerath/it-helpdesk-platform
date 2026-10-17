# Code quality and metrics (Vimukthi)

**Goal:** automated static analysis and quality indicators for the Ticket service: coverage, code smells,
vulnerabilities, technical debt, duplication and ratings, collected in one report that can fail the run.

## Run it
```bash
./scripts/run-tests.sh quality
```
It needs the unit tests' coverage file. If there is none it runs the unit tests first, which need MySQL
(see `testing/README.md`). It writes everything to `testing/quality/reports/`.

| File | What it does |
|---|---|
| `run.sh` | The one command. Runs the two scripts below and fails if either does. |
| `coverage_report.py` | Reads the coverage file and shows line and branch coverage **per source file**, least covered first, plus `reports/coverage.html`. |
| `quality_report.py` | Reads SonarCloud's public API for the Ticket API folder: the quality gate, 14 indicators, and every open issue. Shows the change since the saved baseline. |
| `baseline.json` | A saved SonarCloud snapshot taken **before** any fixes, so the demo can show before and after. |

SonarCloud itself is the analysis engine. It already runs on every pull request through
`.github/workflows/sonarcloud.yml`; these scripts collect and judge its results for one service.

## When the run fails
It exits 1 if **any** of these is true, and prints which:
- the SonarCloud quality gate is not OK
- coverage for the Ticket API is below `MIN_COVERAGE` (default 40, set it as an environment variable)
- a blocker or critical issue is open

Anything else (minor smells, info) is reported but does not fail the run.

Useful options: `PR=68 ./scripts/run-tests.sh quality` reports on a pull request instead of the main
branch, and `python testing/quality/quality_report.py --save-baseline testing/quality/baseline.json`
saves a new "before" snapshot.

## The before and after story for the demo
Baseline (main branch, before the fixes): **1 vulnerability, 14 code smells, security rating B**.
The fixes made on this branch and what each one resolves:

| Finding | Fix |
|---|---|
| `docker:S6471` Dockerfile runs as root (the vulnerability) | The container now runs as the image's non-root user |
| `csharpsquid:S6966` use `RunAsync` | `await app.RunAsync()` |
| `csharpsquid:S1118` utility class needs a protected constructor | Added one to `Program` |
| `csharpsquid:S6667` logging inside a catch without the exception | The normal-shutdown message is logged in `finally` instead, so there is no catch-block log (passing the cancellation exception would only add a noisy stack trace) |
| `CA1869` new `JsonSerializerOptions` on every call (3) | One shared static instance |
| `csharpsquid:S6968` missing `ProducesResponseType` (2) | Added, which also improves the Swagger documentation |

**Checked locally for the Dockerfile fix:** the old deployed image reports `uid=0(root)`; the rebuilt image reports
`uid=1654(app)`, starts normally, answers `GET /health` with 200 and logs no permission errors
(`docker run --rm --entrypoint id <image>` shows the user).

SonarCloud only re-analyses after the change reaches a pull request or `main`, so run
`PR=<number> ./scripts/run-tests.sh quality` on the pull request, or the default on `main`
once merged, to see the numbers move.

## Not fixed on purpose
- **Duplication (about 16%)** comes from files copied between services (`KafkaTracing.cs`, parts of
  `Program.cs`, the Kafka consumers). Removing it needs a shared library, which is a larger design change.
- **Five `S101` DTO class names** (`TicketCreateDTO` and similar). Renaming touches many files and tests for no behaviour change.
- **`S6964`** on `UpdateTicketStatusDTO`: adding `[JsonRequired]` would change the error message API clients receive.

## Be ready to explain
Code smell, bug and vulnerability; technical debt (SQALE) and how it is estimated; the quality gate and
"new code"; line coverage versus branch coverage; cyclomatic and cognitive complexity; duplication;
why a metric can mislead (high coverage with weak assertions, duplication that is really copied infrastructure).

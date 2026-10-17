# Testing pipeline (SE3112 take-home)

One automated pipeline that runs every test layer against the **Ticket service**, plus one script that
runs the same layers on a laptop for the live demo. It only tests: it never builds an image, never
touches Azure and never blocks a service's own release pipeline, so each service stays independently
deployable.

## Why the Ticket service
It has the full ticket flow (create, list, change status, history, report), publishes events to Kafka,
and already has a test project. It suits all the areas below, so one service is explained once and
every layer is aimed at it.

## Layout
```
testing/
  README.md          this file: tools, commands, who did what
  unit/              Unit tests and mocking        run.sh  (works: runs the existing Ticket tests)
  mutation/          Mutation testing              run.sh  (placeholder)  stryker-config.json
  quality/           Code quality and metrics      run.sh  (placeholder)
  ui/                UI / end-to-end tests         run.sh  (placeholder)
scripts/run-tests.sh                     one command for every layer
.github/workflows/testing-pipeline.yml   the pipeline: calls one reusable workflow per layer
.github/workflows/testing-unit.yml         owner: Shamim
.github/workflows/testing-mutation.yml     owner: Rashaad
.github/workflows/testing-quality.yml      owner: Vimukthi
.github/workflows/testing-ui.yml           owner: Senul
```
Each layer has **its own folder and its own workflow file**, so nobody edits anyone else's work.

## Who did what
| Student | Area | Folder |
|---|---|---|
| Shamim (IT24101503) | Unit Testing and Mocking | `testing/unit/` |
| Rashaad (IT24100146) | Mutation Testing | `testing/mutation/` |
| Vimukthi (IT24101500) | Code Quality and Metrics | `testing/quality/` |
| Senul (IT24101497) | UI / End-to-End Testing | `testing/ui/` |

The brief allows **exactly four** areas, one per student. Load and performance testing is not one of
the four. If the group decides to swap an area, add a folder, a `run.sh` and a `testing-<area>.yml`, and
add the layer to `testing-pipeline.yml` and `scripts/run-tests.sh`.

## Commands
```bash
./scripts/run-tests.sh            # every layer, with a summary table
./scripts/run-tests.sh unit       # one layer: unit | mutation | quality | ui
```
Run it from Git Bash on Windows. In GitHub: **Actions > Testing Pipeline > Run workflow**, or push to
any branch named `testing/...`. The run ends with a combined report and one downloadable artifact per layer.

## Tool setup
| Needed for | Install |
|---|---|
| everything | .NET 8 SDK, Git Bash, Docker Desktop |
| unit tests | `dotnet tool install --global dotnet-ef --version 8.0.8`, then `docker compose up -d mysql` and `cd services/ticket/Ticket.Api && dotnet ef database update --context ApplicationDbContext` |
| mutation | `dotnet tool install --global dotnet-stryker` |
| code quality | Python 3 (to read the SonarCloud API); SonarCloud already analyses the repo |
| UI / end-to-end | Node.js 20 and a browser test tool such as Playwright (installed inside `testing/ui/`) |

## How to work
1. Branch from `testing/base`: `git checkout -b testing/<your-area> testing/base`.
2. Change **only your own folder and your own workflow file**.
3. Make `run.sh` in your folder do the real work, and keep it exit 0 only when your checks pass.
4. Run `./scripts/run-tests.sh <your-area>` until it is reliable, then push and open a pull request.
5. Update the table above if your tools differ from what is written here.

## Live demo checklist
- Run `./scripts/run-tests.sh <your-area>` once beforehand, on the same machine you will present from.
- Know what each line of the output means. The viva asks about **your own** code.
- Have the pipeline run in GitHub open in a tab, as a fallback.

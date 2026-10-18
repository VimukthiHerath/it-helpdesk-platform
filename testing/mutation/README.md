# Mutation testing (Rashaad)

**Goal:** measure how good the Ticket unit tests really are, by changing the code on purpose and checking the tests notice.

Stryker.NET makes a small change to the code (a "mutant", for example `>` becomes `>=`, or a condition is negated) and runs
the unit tests against it. A failing test has **killed** the mutant. A test run that still passes has let it **survive**, which
means the tests would not have caught that bug.

## Run it
```bash
dotnet tool install --global dotnet-stryker --version 4.16.0     # once
./scripts/run-tests.sh mutation
```
No database is needed (the Ticket unit tests are in memory). It takes about 90 seconds.
The HTML report is written to `testing/mutation/reports/index.html`; open it in a browser.

| File | What it does |
|---|---|
| `run.sh` | Runs Stryker, copies the report, and **fails** if Stryker is missing, crashes, produces no report, or scores below the threshold. |
| `stryker-config.json` | What to mutate (`Controller/**` and `Services/**` of the Ticket API), the reports, and the score thresholds. |
| `reports/index.html` | The committed report from the last full run. |

Thresholds in the config: **high 90, low 80, break 70**. Below 70 the run fails; between 70 and 80 it is flagged as low.

## Result (verified)
Run on the unit tests merged with the code fixes (64 tests):

| | |
|---|---|
| Mutants created | 549 |
| Tested | 190 |
| **Killed** | **190** |
| **Survived** | **0** |
| **Mutation score** | **100%** |

The 359 mutants that were not tested were skipped by Stryker's own filters, not hidden in the code (there are no
`Stryker disable` comments): 303 fall outside the `Controller` and `Services` folders chosen in the config, 44 are removed by
Stryker's "block already covered" optimisation, and 12 produced code that does not compile.

## Be honest about what 100% means
- It is a score **within the chosen scope** (`Controller` and `Services`). Models, DTOs and `Program.cs` are not mutated.
- Only the mutants Stryker generated and did not skip count. It says the tests are strong for those, not for every possible bug.
- A high mutation score is much stronger evidence than high coverage, because coverage only shows code ran, not that anything was checked.

## Be ready to explain
What a mutant is; killed, survived, timed out and no coverage; the mutation score and how it is calculated;
equivalent mutants (changes that do not alter behaviour, so no test can kill them); why code coverage can be high while tests are weak;
what the `break` threshold does in a pipeline.

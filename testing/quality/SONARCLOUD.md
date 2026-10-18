# SonarCloud: code quality and metrics (Vimukthi)

Everything about code quality in this project, in one place: what the tool is, how it is wired into our
pipeline, what it measures, what the numbers say today, what was done about them, and how to demo and defend it.

- Tool: **SonarQube Cloud** (the hosted version of SonarQube, formerly called SonarCloud). Same analysis engine, run by SonarSource, free for public repositories.
- Project: https://sonarcloud.io/project/overview?id=VimukthiHerath_it-helpdesk-platform
- Numbers below were read from SonarCloud's public API on 18 Oct 2026, from the latest analysis of `main` (17 Oct 2026).
- The history of what broke while setting it up is in `docs/SONARQUBE_ADJUSTMENTS.md`; this document summarises it.

---

## 1. What it is and why we use it

A scan reads the source code, without running it, and reports:
- **bugs**: code that is likely to behave wrongly
- **vulnerabilities**: code that is a security weakness
- **code smells**: code that works but is hard to maintain
- **duplication**, **complexity**, and how much of the code the **tests cover**

It then judges the result against a **quality gate**: a pass or fail rule set. That makes quality something
a pull request can fail on, instead of an opinion. For the assignment this is the "Code Quality and Metrics" area:
automated static analysis, plus quality indicators such as coverage, code smells and technical debt.

## 2. How it fits our pipeline

```
pull request or push to develop / main
        |
        v
.github/workflows/sonarcloud.yml   ("SonarCloud Quality / Test and analyze")
   1. build the five services and apply the migrations to a throwaway MySQL
   2. run the backend tests with coverage   (coverlet writes coverage.opencover.xml)
   3. run the frontend tests with coverage  (writes frontend/coverage/lcov.info)
   4. SonarScanner uploads the code and both coverage files to SonarCloud
        |
        v
SonarCloud analyses, applies the quality gate, and comments on the pull request ("SonarCloud Code Analysis")
```

Two checks appear on a pull request and are easy to confuse:

| Check | What it is | Red means |
|---|---|---|
| `SonarCloud Quality / Test and analyze` | Our GitHub Actions job | The build, tests or upload failed |
| `SonarCloud Code Analysis` | SonarCloud's own verdict, the quality gate | The new code missed a rule. The build was fine. |

How the workflow is set up (`.github/workflows/sonarcloud.yml`):
- **Triggers:** push and pull request to `main` and `develop`, only when code or its configuration changes.
- **Settings:** the project key and organisation are GitHub *variables*; the token is a GitHub *secret* (`SONAR_TOKEN`).
- **One scan for the whole repository** (a monorepo), so five services and the frontend are analysed together.
- **Languages seen:** C#, JavaScript, CSS, JSON, **YAML (our GitHub Actions workflows)**, Dockerfiles and Azure templates.
  Roslyn compiler analyzer findings (rules named `CA...`) are imported too.
- **Coverage exclusions:** `frontend/src/**` (few tests yet), `**/Migrations/**` (generated EF code) and `**/Program.cs`
  (start-up wiring). Excluded files are still analysed for bugs and smells, only left out of the *coverage* figure.
- **Integration tests are filtered out of this run** (`Category!=Integration`), because they need Kafka and a live Auth service.

### Limits of the free plan we met
- Gate results can only be read for the **main** branch. So the workflow does not wait for the gate (`sonar.qualitygate.wait` is off); the result shows on the pull request instead.
- The built-in quality gate cannot be edited, so "80% coverage on new code" cannot be relaxed in SonarCloud. We handled that with the exclusions above, openly and in writing.

## 3. What it measures (the vocabulary)

| Term | Meaning |
|---|---|
| **Bug** | Code that will probably misbehave. Counts toward the *reliability* rating. |
| **Vulnerability** | A confirmed security weakness. Counts toward the *security* rating. |
| **Security hotspot** | Security-sensitive code that a person must review and mark safe or not. Not yet a vulnerability. |
| **Code smell** | Works, but is hard to read or change. Counts toward *maintainability*. |
| **Technical debt** | The estimated time to fix all the code smells, in minutes (each rule has a fixed remediation time). |
| **Ratings A to E** | Reliability and security: A means none, and the letter worsens with the severity of the worst issue (B minor, C major, D critical, E blocker). Maintainability: A means debt is at most 5% of the estimated cost to write the code, then B up to 10%, C 20%, D 50%, E above. |
| **Line coverage** | The share of executable lines that at least one test ran. |
| **Branch coverage** | The share of decision outcomes (the true and false side of each `if`) that tests took. |
| **Coverage** | One combined figure of lines and branches. |
| **Duplication** | The share of lines that sit inside blocks copied elsewhere. |
| **Cyclomatic complexity** | The number of independent paths through the code, so roughly the minimum number of tests to cover it. |
| **Cognitive complexity** | How hard the code is for a person to follow; nesting counts for more than a flat run of `if`s. |
| **New code** | The lines a pull request changes (or changed since the last release). The gate looks at these, not at old code. |
| **Quality gate** | The pass or fail rule set, applied to new code. |

The idea behind judging only **new code** is "clean as you code": do not demand that the whole history be fixed at once,
but never let new code make things worse.

## 4. Our quality gate

The gate conditions we have seen applied on pull requests (all on *new code*):

| Condition | Required |
|---|---|
| Reliability rating | A |
| Security rating | A |
| Maintainability rating | A |
| Coverage | at least 80% |
| Duplicated lines | at most 3% |
| Security hotspots reviewed | 100% |

### How the gate failed, and what each failure taught us
| # | What failed | Cause | Fix | Lesson |
|---|---|---|---|---|
| 1 | "End analysis" step failed with HTTP 403 | Free plan gives gate data only for `main` | Stopped waiting for the gate | Know your plan's limits |
| 2 | Reliability C, security C on the first PR to `main` | 48 unhandled promises in React, plus an inline secret in a workflow command | `void` markers, secret moved to `env:` | Some findings are real, some are noise; read each |
| 3 | Coverage 2% | A PR that only touched untested frontend pages | Excluded the frontend from the coverage figure | The gate measures only changed lines, so tiny PRs can swing wildly |
| 4 | Coverage 57% on the phone-number PR | 140 generated migration lines counted as untested | Excluded `**/Migrations/**` | Generated code does not belong in a coverage target |
| 5 | **Pull request #68**: coverage 21.4%, duplication 3.5%, security C | 28 of the 29 uncovered new lines and most duplicated lines were one copied code block in SLA; the C came from 5 workflow findings | Removed the copy; pinned third-party actions to commit SHAs; moved a token permission to job level; excluded `Program.cs` from coverage | Copy and paste is both a coverage and a duplication problem. Fix the cause, not the number. |

Be ready to say plainly that **two of these fixes were exclusions**. They are legitimate (generated code, start-up wiring,
untested frontend) and are written down, but they do change what the number means.

## 5. What the numbers say today

Latest `main` analysis (17 Oct 2026):

| Component | Lines | Coverage | Bugs | Vulns | Smells | Debt | Duplication | Rating (rel / sec / maint) |
|---|---|---|---|---|---|---|---|---|
| **Whole project** | 11,995 | 49.2% | 46 | 14 | 130 | 568 min (9h 28m) | 6.6% | C / C / A |
| Auth | 1,360 | 73.9% | 0 | 1 | 25 | 97 min | 5.2% | A / B / A |
| **Ticket** | 1,169 | 41.8% | 0 | 1 | 14 | 60 min | 15.7% | A / B / A |
| Assignment | 1,008 | 50.3% | 0 | 1 | 21 | 100 min | 18.6% | A / B / A |
| SLA | 792 | 46.5% | 0 | 1 | 16 | 48 min | 11.4% | A / B / A |
| Notification | 1,078 | 31.6% | 0 | 1 | 13 | 91 min | 20.4% | A / B / A |
| Frontend | 3,570 | not measured | **46** | 0 | 24 | 152 min | 0% | **C** / A / A |

How to read it honestly:
- **All 46 bugs are one rule**, `javascript:S9383` "Promises must be awaited": 46 promise-returning calls in the React
  code with no `void`, `await` or `.catch` (35 are `navigate(...)` calls, 11 are calls such as `.then(...)`). They cannot
  actually fail in our usage, but the tool is right that a rejected promise would go unhandled. This is the single thing
  holding the project's reliability at C, and the cheapest big win left.
- **The 14 vulnerabilities** are: 5 Dockerfiles that run as root (`docker:S6471`; the Ticket one is already fixed on
  `testing/quality`), 5 Azure template findings (`azureresourcemanager:S6378`, a missing managed identity), and 4 GitHub Actions findings.
- **Duplication is high in the services (11 to 20%)** because the same files are copied between them (`KafkaTracing.cs`,
  most of `Program.cs`, the Kafka consumers). The real fix is a shared library, which is a design change, not a quick edit.
- **Coverage ranges from 32% (Notification) to 74% (Auth).** The unit-test owner's work should raise Ticket's.

## 6. My part: the tooling

All in `testing/quality/`, run with one command:

```bash
./scripts/run-tests.sh quality
```

| File | What it does |
|---|---|
| `run.sh` | Runs the steps below in order and fails if either fails. |
| `coverage_report.py` | Reads the coverage file the unit tests write and shows line and branch coverage **per file**, least covered first, and `reports/coverage.html`. Pure Python, no downloads. |
| `quality_report.py` | Asks SonarCloud's public API for the Ticket API folder: the quality gate, 14 indicators and **every open issue** (rule, file, line), and shows the change since a saved baseline. |
| `baseline.json` | A snapshot of SonarCloud's numbers taken **before** any fixes, so the demo can show before and after. |

When it fails (exit code 1), and says why: the quality gate is not OK, coverage is below `MIN_COVERAGE` (default 40), or a
blocker or critical issue is open. Minor smells are reported but do not fail the run. `PR=<number>` reports on a pull request.

It also runs in the pipeline as the **quality** job of `testing-pipeline.yml`, with its own database, and uploads the reports.

Two things went wrong while building it, worth telling because they show testing the tool itself:
1. My first version listed 2 smells where SonarCloud reported 14. SonarCloud's issue search on a folder covers only the files
   directly in it, not sub-folders, so the script now walks every sub-folder. The totals now match.
2. The first coverage figure had to be checked against SonarCloud's. My per-file calculation gives 42.9% line coverage and
   SonarCloud reports 42.9%, so the two independent routes agree.

### Fixes made to the Ticket service (the before and after)
Baseline: **1 vulnerability, 14 code smells, security rating B.**

| Finding | Fix |
|---|---|
| `docker:S6471` the container runs as root | Runs as the image's non-root user. **Checked locally**: old image `uid=0(root)`, new image `uid=1654(app)`, `/health` returns 200, no permission errors. |
| `csharpsquid:S6966` use `RunAsync` | `await app.RunAsync()` |
| `csharpsquid:S1118` utility class constructor | Protected constructor on `Program` |
| `csharpsquid:S6667` logging in a catch | Shutdown message logged in `finally` |
| `CA1869` new `JsonSerializerOptions` each call (3) | One shared static instance |
| `csharpsquid:S6968` missing response type (2) | `ProducesResponseType` added, which also improves the Swagger page |

All 24 Ticket tests still pass. Expected after SonarCloud re-analyses: **vulnerabilities 1 to 0, security rating B to A, smells 14 to about 6**.
Not fixed on purpose: duplication (needs a shared library), five DTO class names (`S101`, a rename across many files for no
behaviour change) and `S6964` (adding `[JsonRequired]` would change the error message clients receive).

## 7. Demo script (about 2 minutes)

1. **Say what it is (15 s).** "SonarCloud analyses our code on every pull request. My part turns its results into one
   report that can fail the build, and shows where the tests are thin."
2. **Run it live (30 s).** `./scripts/run-tests.sh quality`. Point at: coverage by file (TicketController has the most
   untested lines), the quality gate block, the indicator table, the open issues list.
3. **Explain one number (30 s).** Pick the vulnerability: "Dockerfile runs as root. I fixed it and proved it:" show
   `docker run --rm --entrypoint id <image>` giving `uid=1654(app)`.
4. **Show before and after (30 s).** The "change since the saved run" column once a pull request is analysed
   (`PR=<number> ./scripts/run-tests.sh quality`), or the baseline against current main.
5. **Show the gate doing its job (15 s).** Open pull request #68: red on duplication and coverage, green after the fix.

Have the SonarCloud dashboard and the pipeline run open in browser tabs as a fallback if the network is slow.

## 8. Viva questions and answers

**What is the difference between a bug, a vulnerability and a code smell?**
A bug is likely wrong behaviour. A vulnerability is a security weakness. A code smell works but is hard to maintain.
They feed three separate ratings: reliability, security and maintainability.

**What is technical debt?**
The estimated time to fix all the maintainability issues, in minutes. Each rule has a fixed remediation time. Ticket has 60 minutes of it.

**What is the quality gate, and why only "new code"?**
A pass or fail rule set. It judges only the lines a pull request changes, so old code cannot block new work, but new code can
never make things worse. That is "clean as you code".

**Line coverage versus branch coverage?**
Line coverage counts lines that ran. Branch coverage counts the true and false outcome of each decision. A line with an `if`
can be 100% line-covered while only one side of the `if` was ever tried. Ticket: 42.9% line, 35.7% branch.

**Does 100% coverage mean good tests?**
No. It shows code ran, not that anything was checked. A test with no assertions gives coverage and no protection. Mutation testing
(a teammate's area) measures that gap.

**Cyclomatic versus cognitive complexity?**
Cyclomatic counts independent paths, roughly the minimum number of tests needed. Cognitive measures how hard the code is for a human to follow, and punishes deep nesting.

**Why is duplication so high in the services?**
The services copy shared infrastructure files (Kafka tracing, start-up code, consumers). It is a real design cost of microservices
without a shared library, not carelessness in the logic.

**Why did you exclude Migrations, Program.cs and the frontend from coverage?**
Migrations are generated. Program.cs is start-up wiring with no logic. The frontend has few tests yet. Each exclusion is written
down with its reason, and they hide the number, not the problem. I would say that rather than let someone find it.

**What is the biggest issue left?**
46 places in the React code that ignore a promise (35 of them `navigate(...)`). They hold project reliability at C.

**SonarCloud or SonarQube?**
Same engine. SonarQube you host yourself. SonarCloud, now "SonarQube Cloud", is hosted and free for public repositories, so it needs no server for us.

**Can the pipeline fail on quality?**
Yes. My script exits 1 on a failed gate, low coverage or a blocker issue. The SonarCloud check itself also goes red on a pull request.
On the free plan the workflow cannot wait for the gate on non-main branches, which is why the script reads it from the API.

**How do you know your script is right?**
It agrees with SonarCloud's own totals (14 smells, 42.9% line coverage), and I found and fixed a mismatch while building it.

## 9. Honest limits
- SonarCloud only re-measures on a pull request or `main`, so my fixes show up after they are merged or in a pull request analysis.
- Static analysis finds patterns, not logic errors. It does not flag what looks like a real defect in the SLA service: reading the code,
  urgency 0 ("within 1 hour") is mapped to the tier seeded as 48 hours. That comes from reading the code and has not yet been confirmed by running it.
- Some rules are noise in context, for example unawaited `navigate()` calls that cannot fail. Each finding needs judgement.
- A coverage or duplication target can be gamed (exclusions, tests without assertions). The numbers are evidence, not proof.

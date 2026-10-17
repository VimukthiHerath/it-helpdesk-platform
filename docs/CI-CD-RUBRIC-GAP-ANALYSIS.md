# CI/CD pipeline: gap analysis against the final-evaluation rubric

Written for the SE3022 final evaluation. It compares what our GitHub Actions pipelines do today with
the rubric's "CI/CD Pipeline" criterion (15% of the mark) and lists what is missing, in the order it
should be fixed. Every "today" statement below was checked against the workflow files in
`.github/workflows/` and the workflow run history, not against older documentation.

## 1. What the rubric asks for

| Level | What the rubric says |
|---|---|
| Good (55-70%) | Pipeline builds the project automatically. Deployment is partially automated or needs manual triggers. |
| Very good (70-90%) | Automated pipeline that builds, runs tests and deploys to a staging or production environment successfully. |
| Excellent (90%+) | Robust, multi-stage pipeline. Includes steps for **testing (unit, integration)**, **code analysis** and **secure deployment**. Reliable and a fundamental part of the team's workflow. |

The template also asks section 6 to cover: a pipeline diagram, **stages and job separation** (for
example build, test, deploy), and the tools used with a justification for each.

## 2. What we have today

Eight workflows, all triggered on pushes and pull requests to `main` and `develop`.

| Workflow | Does |
|---|---|
| `auth-ci.yml`, `ticket-ci.yml`, `assignment-ci.yml`, `sla-ci.yml`, `notification-ci.yml` | Restore, build, apply EF migrations to a MySQL service container, run unit tests, build the Docker image. On a push to `develop` only: log in to Azure with OIDC, apply migrations to the Azure database, push the image to ACR, update the Container App. Path-filtered per service. |
| `frontend-ci.yml` | `npm ci`, unit tests with coverage, production build, Docker image. On a push to `develop`: push to ACR and update the Container App. |
| `sonarcloud.yml` | Builds and tests all five backends and the frontend with coverage, then runs the SonarCloud analysis. |
| `wso2-gateway-ci.yml` | Validates the five OpenAPI files, boots a WSO2 container as a smoke test. |

Evidence it is reliable: the 30 most recent runs were all green, and the recent push runs on
`develop` had a single SonarCloud failure, fixed within hours. Images are tagged with the commit SHA, deployment uses OIDC instead of stored
cloud passwords, and the database migrations are applied by the pipeline.

## 3. Gaps, most valuable first

| # | Gap | Why it matters for the rubric | Effort |
|---|---|---|---|
| 1 | **No security scanning.** No dependency vulnerability check, no secret scanning, no container image scan, no CodeQL. | "Secure deployment" and "code analysis" are named in the Excellent level. Sonar covers code quality only. | Small |
| 2 | **Build, test and deploy are one job per workflow.** Deploy is a set of `if:` steps at the end of the test job. | The template asks for "stages and job separation". Separate jobs with `needs:` show the stages, allow a deploy to be gated, and stop a failed test job from sharing a runner with a deploy. | Medium |
| 3 | **Integration tests never run in CI.** Ticket and Assignment tests tagged `Integration` are filtered out (`--filter "Category!=Integration"`). | The rubric names integration tests. Today CI proves unit tests only. | Medium |
| 4 | **No check that a deployment actually worked.** `az containerapp update` is the last step; a crashing new revision still shows a green pipeline. | "Reliable" and "secure deployment". There is no verification and no rollback. | Medium |
| 5 | **Test results and coverage are not kept.** The auth workflow writes a `.trx` file that is never uploaded; coverage XML is only read by Sonar. | The documentation criterion rewards coverage reports; the pipeline should publish them as artifacts and in the job summary. | Small |
| 6 | **Five near-identical backend workflows.** Any CI change is copied by hand five times. | Maintainability. A reusable workflow (`workflow_call`) removes the duplication and is also what makes gap 2 cheap to fix. | Medium |
| 7 | **No environment separation or promotion.** Only `develop` deploys. `main` never deploys. There is no staging. | The Very good level says "staging/production". At minimum GitHub Environments with a protected production job would show intent. | Depends on your environment decision |
| 8 | **WSO2 API definitions are validated but never published.** Changing `infra/wso2/apis/*.json` does not update the gateway; it needs `scripts/publish-wso2-apis.sh azure` run by hand. | This is a real manual deployment step that caused a live gap during the phone-number change. | Medium; needs the gateway to be running |
| 9 | **The SonarCloud quality gate is not enforced.** The free plan refuses gate data for non-main branches, so the pipeline cannot fail on it. | The PR check shows the result, but the pipeline itself does not block. Known limit, documented in `SONARQUBE_ADJUSTMENTS.md`. | None available on the free plan |
| 10 | **No concurrency control.** Two quick pushes to `develop` can deploy out of order. | Reliability. | Small |
| 11 | **Third-party actions pinned by tag, not commit SHA.** | Supply-chain hardening, flagged by Sonar. | Small |
| 12 | **No Docker layer caching, no lint step for the frontend.** | Speed and quality. | Small |

## 4. Concrete findings from checking the code

* **Vulnerable packages in the services.** `dotnet list package --vulnerable --include-transitive`
  on Auth reports High advisories for `System.Text.Json 8.0.0` and
  `Microsoft.Extensions.Caching.Memory 8.0.0`, and a Moderate one for `OpenTelemetry.Api 1.12.0`.
  A blocking dependency check would fail today. The fix is to pin patched versions as direct
  package references and re-run each service's tests. The other four services share the same
  OpenTelemetry and framework packages and need the same check.
* **`docs/CI-CD-PIPELINE-EXPLAINED.md` is out of date.** Its gap 1 says no tests run in CI. They do
  now. It also lists seven workflows; there are eight. That file should be corrected before it is
  used as a source for the final document.
* **Branch protection** cannot be read without admin rights. If it is not enabled, the PR-first
  workflow is a convention only. Turning on "require status checks" for `develop` is a settings
  change, not code.

## 5. Suggested order of work

1. Security scanning workflow (gap 1) and artifact upload (gap 5): additive, cannot break a deploy.
2. Reusable backend workflow with separate test, build and deploy jobs, concurrency control and a
   post-deploy health check (gaps 2, 4, 6, 10). This rewrites the deploy path, so it should be
   merged when someone can watch the first `develop` run.
3. Integration test job (gap 3).
4. Environment protection and WSO2 publish job (gaps 7, 8) once the environment strategy is decided.

Anything that only runs on a push to `develop` cannot be proven from a pull request. The first
merge after a deploy-path change should be watched, with the previous image tag noted for rollback.

## 6. Status after the first round of fixes

"Verified" says what was actually run, not just written. Anything that only runs on a push to
`develop` is not proven until that push happens.

| # | Gap | Status | How it was checked |
|---|---|---|---|
| 1 | Security scanning | **Done.** CodeQL (C# and JavaScript), gitleaks, and a Trivy scan of each image. A report-only dependency check was added and then removed, because it only listed problems | `security.yml` and the Trivy steps in every pipeline ran green on the pull request |
| 2 | Job separation | **Done.** `test`, `image`, `deploy` for every service and the frontend | The test and image jobs ran green on the pull request for all six pipelines. The `deploy` job is skipped on pull requests by design and is unproven until the first push to `develop` |
| 3 | Integration tests | **Partly.** A job runs the 3 Ticket tests against a Kafka container, and ran green on the pull request. The 7 Assignment integration tests are still excluded | It reports without gating the deploy; add it to the `needs` of the image job after a few green runs |
| 4 | Post-deploy check and rollback | **Done.** Both paths exercised against the live `sla-service` on 2026-10-16 | Redeploying its current image: healthy, rollback skipped, exit 0. Deploying a missing image tag: the new revision stayed `Activating` for the full 240 s timeout, the action rolled back to the previous image and exited 1, and `sla-service` ended healthy on the original image with 100% traffic. The test ran the action's own steps from a local script, not on a GitHub runner |
| 5 | Test results and coverage kept | **Done.** Artifacts plus a results table in each job summary | Coverage command and summary script run locally (Auth: 52 passed, 47.5% line coverage) |
| 6 | Reusable workflow | **Done.** One `backend-service.yml`, five callers of about 40 lines | Workflow lint clean |
| 7 | Environments and promotion | Open | Depends on the environment strategy |
| 8 | Publish WSO2 definitions from CI | Open | Needs `az containerapp exec` against a running gateway; not attempted without a way to test it |
| 9 | SonarCloud gate enforced | Not possible on the free plan | |
| 10 | Concurrency | **Done.** PR runs cancel superseded runs; each Container App deploys one at a time | |
| 11 | SHA pinning | **Done** for the third-party actions (`azure/login`, `gitleaks`); GitHub-owned actions use version tags. Automatic dependency updates (Dependabot) were tried and removed: the rubric does not ask for them, and they opened about 30 pull requests, many of them major-version bumps that would break the .NET 8 services | |
| 12 | Caching and lint | **Partly.** npm cache added; the production build runs with `CI=true`, so lint warnings fail it. No Docker layer cache | |

### What the local checks found

* **The first version of the health check was wrong.** `az ... --output tsv` prints each value of a
  list on its own line, so the script read the first value and saw the other two as empty. Against a
  healthy app it would have timed out and rolled back a good deploy. It was caught by running the
  script against the live app, and fixed to read one line.
* **A blocking Trivy scan of the whole image would fail every build.** The Auth image carries three
  CRITICAL `perl-base` findings that have a fix in Debian but not yet in Microsoft's `aspnet:8.0`
  base image. The gate therefore covers only the libraries our code depends on, which passed. The
  operating-system findings are reported but do not block.
* **The first pull-request run found two more problems that local checks could not.**
  1. The Trivy action failed at "Set up job". `aquasecurity/trivy-action` pulls in
     `aquasecurity/setup-trivy`, whose tags no longer resolve, so every pinned version of the action
     is broken. The pipeline now runs the Trivy container directly, with the command that had already
     passed locally.
  2. The EF tools failed to prepare the test database. They start the app to read its configuration,
     and the app reads its connection string from a service-specific key; without it the app fell back
     to `appsettings.json` (a MySQL on port 3307 that does not exist on a runner). The same flaw in the
     deploy job would have failed the migration step on the first real deploy, so the connection key is
     now set there too.
* **A revision that cannot pull its image never reports `Failed`.** It stays `Activating` with no
  health state, so the timeout, not a failure signal, is what triggers the rollback. A bad deploy
  therefore takes the full five minutes to be reported.


#!/usr/bin/env python3
"""Quality indicators for the Ticket service, read from SonarCloud.

SonarCloud already analyses the repository on every pull request (see .github/workflows/sonarcloud.yml).
This script asks its public web API for the numbers that belong to the Ticket service and turns them
into one readable report: the quality gate, coverage, code smells, vulnerabilities, technical debt,
duplication, complexity and the individual open issues.

    python3 quality_report.py                       the main branch
    python3 quality_report.py --pull-request 68     one pull request
    python3 quality_report.py --baseline baseline.json     also show the change since a saved run
    python3 quality_report.py --save-baseline baseline.json   remember this run for later

Exit code: 0 = every check passed, 1 = a check failed, 2 = SonarCloud could not be reached.
Only the standard library is used. No token is needed because the project is public.
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

API = "https://sonarcloud.io/api"
PROJECT = "VimukthiHerath_it-helpdesk-platform"
DIRECTORY = "services/ticket/Ticket.Api"

# Metric key -> label. The order is the order of the report.
METRICS = [
    ("ncloc", "Lines of code"),
    ("coverage", "Coverage (%)"),
    ("line_coverage", "Line coverage (%)"),
    ("branch_coverage", "Branch coverage (%)"),
    ("bugs", "Bugs"),
    ("vulnerabilities", "Vulnerabilities"),
    ("code_smells", "Code smells"),
    ("sqale_index", "Technical debt (minutes)"),
    ("duplicated_lines_density", "Duplication (%)"),
    ("complexity", "Cyclomatic complexity"),
    ("cognitive_complexity", "Cognitive complexity"),
    ("reliability_rating", "Reliability rating"),
    ("security_rating", "Security rating"),
    ("sqale_rating", "Maintainability rating"),
]
RATINGS = {"1.0": "A", "2.0": "B", "3.0": "C", "4.0": "D", "5.0": "E"}
# For these, a lower number is better, so a fall is an improvement.
LOWER_IS_BETTER = {"bugs", "vulnerabilities", "code_smells", "sqale_index", "duplicated_lines_density",
                   "complexity", "cognitive_complexity", "reliability_rating", "security_rating", "sqale_rating"}
SEVERITY_ORDER = ["BLOCKER", "CRITICAL", "MAJOR", "MINOR", "INFO"]


def get(path, **params):
    url = API + path + "?" + urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
    with urllib.request.urlopen(url, timeout=30) as response:
        return json.load(response)


def scope(args):
    """Which version of the code to ask about: a branch, a pull request, or the default branch."""
    return {"branch": args.branch, "pullRequest": args.pull_request}


def read(args):
    where = scope(args)
    gate = get("/qualitygates/project_status", projectKey=PROJECT, **where)["projectStatus"]
    keys = ",".join(key for key, _ in METRICS)
    measures = get("/measures/component", component=f"{PROJECT}:{args.directory}", metricKeys=keys, **where)
    values = {m["metric"]: str(m.get("value", m.get("periods", [{}])[0].get("value", ""))) for m in measures["component"]["measures"]}
    # An issue search on a folder only covers the files directly inside it, so ask for every sub-folder too.
    tree = get("/components/tree", component=f"{PROJECT}:{args.directory}", qualifiers="DIR", strategy="all", ps=500, **where)
    folders = [args.directory] + [c["path"] for c in tree.get("components", []) if c.get("path")]
    issues = get("/issues/search", componentKeys=PROJECT, directories=",".join(folders), resolved="false", ps=500,
                 facets="severities,types", **where)
    return gate, values, issues


def pretty(key, value):
    if value == "":
        return "-"
    if key.endswith("_rating"):
        return RATINGS.get(value, value)
    if key == "sqale_index":
        minutes = int(float(value))
        return f"{minutes} ({minutes // 60}h {minutes % 60}m)"
    return value


def change(key, now, before):
    if before is None or now == "" or before == "":
        return ""
    try:
        delta = float(now) - float(before)
    except ValueError:
        return ""
    if delta == 0:
        return "no change"
    better = (delta < 0) == (key in LOWER_IS_BETTER)
    sign = "+" if delta > 0 else ""
    text = f"{sign}{delta:g}"
    if key.endswith("_rating"):
        text = f"{RATINGS.get(before, before)} -> {RATINGS.get(now, now)}"
    return f"{text}  ({'better' if better else 'worse'})"


def evaluate(args, gate, values, issues):
    """The rules that decide pass or fail. Everything else is reported but does not fail the run."""
    problems = []
    if gate["status"] != "OK":
        problems.append(f"Quality gate is {gate['status']}")
    coverage = values.get("coverage")
    if coverage and float(coverage) < args.min_coverage:
        problems.append(f"Coverage {coverage}% is below the minimum {args.min_coverage}%")
    serious = [i for i in issues["issues"] if i["severity"] in ("BLOCKER", "CRITICAL")]
    if serious:
        problems.append(f"{len(serious)} blocker or critical issue(s) are open")
    return problems


def render(args, gate, values, issues, baseline, problems):
    lines = []
    scope_name = f"pull request {args.pull_request}" if args.pull_request else (f"branch {args.branch}" if args.branch else "main branch")
    lines.append(f"Code quality: {args.directory} ({scope_name})")
    lines.append("")
    lines.append(f"Quality gate (whole project): {gate['status']}")
    for condition in gate.get("conditions", []):
        lines.append(f"  - {condition['metricKey']:34} {condition['status']:6} actual {condition.get('actualValue', '-')}")
    lines.append("")
    header = f"{'Metric':28} {'Value':22}" + ("Change since the saved run" if baseline else "")
    lines.append(header)
    lines.append("-" * len(header))
    for key, label in METRICS:
        row = f"{label:28} {pretty(key, values.get(key, '')):22}"
        if baseline:
            row += change(key, values.get(key, ""), baseline.get("metrics", {}).get(key))
        lines.append(row)
    lines.append("")
    severities = {f["val"]: f["count"] for f in next((f for f in issues["facets"] if f["property"] == "severities"), {"values": []})["values"]}
    lines.append("Open issues: " + (", ".join(f"{severities.get(s, 0)} {s.lower()}" for s in SEVERITY_ORDER if severities.get(s)) or "none"))
    for issue in sorted(issues["issues"], key=lambda i: SEVERITY_ORDER.index(i["severity"]) if i["severity"] in SEVERITY_ORDER else 9):
        where = issue["component"].split(":", 1)[-1].replace(args.directory + "/", "")
        lines.append(f"  [{issue['severity']:8}] {issue['type']:13} {issue['rule']:20} {where}:{issue.get('line', '-')}")
        lines.append(f"             {issue['message'][:110]}")
    lines.append("")
    lines.append("RESULT: " + ("PASS" if not problems else "FAIL"))
    for problem in problems:
        lines.append("  - " + problem)
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--directory", default=DIRECTORY, help="folder to report on (default: the Ticket API)")
    parser.add_argument("--branch", help="a SonarCloud branch; default is the main branch")
    parser.add_argument("--pull-request", help="a pull request number instead of a branch")
    parser.add_argument("--min-coverage", type=float, default=float(os.environ.get("MIN_COVERAGE", "40")),
                        help="fail below this coverage percentage (default 40, or the MIN_COVERAGE variable)")
    parser.add_argument("--baseline", help="a file written by --save-baseline, to show the change since then")
    parser.add_argument("--save-baseline", help="write this run's numbers to a file")
    parser.add_argument("--output-dir", default=str(Path(__file__).parent / "reports"), help="where the report files go")
    args = parser.parse_args()

    try:
        gate, values, issues = read(args)
    except (urllib.error.URLError, TimeoutError, KeyError, ValueError) as error:
        print(f"Could not read SonarCloud: {type(error).__name__}: {error}", file=sys.stderr)
        return 2

    baseline = json.loads(Path(args.baseline).read_text(encoding="utf-8")) if args.baseline and Path(args.baseline).exists() else None
    problems = evaluate(args, gate, values, issues)
    report = render(args, gate, values, issues, baseline, problems)
    print(report)

    out = Path(args.output_dir)
    out.mkdir(parents=True, exist_ok=True)
    snapshot = {"directory": args.directory, "gate": gate["status"], "metrics": values,
                "open_issues": [{"rule": i["rule"], "severity": i["severity"], "file": i["component"].split(":", 1)[-1],
                                 "line": i.get("line"), "message": i["message"]} for i in issues["issues"]]}
    (out / "quality-report.json").write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
    (out / "quality-report.txt").write_text(report + "\n", encoding="utf-8")
    if args.save_baseline:
        Path(args.save_baseline).write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
        print(f"\nSaved this run as the baseline: {args.save_baseline}")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write("### Code quality (Ticket service)\n\n```\n" + report + "\n```\n")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())

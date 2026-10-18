#!/usr/bin/env python3
"""Turns the coverage file written by the unit tests into a readable report.

    python3 coverage_report.py [path/to/coverage.opencover.xml]

It reads the OpenCover XML that coverlet writes (testing/unit/run.sh), works out line and branch
coverage per source file, prints the files with the most untested lines first, and writes a
self-contained HTML page to testing/quality/reports/coverage.html.

Exit code: 0 on success, 2 if the coverage file is missing. Only the standard library is used.
"""
import html
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

DEFAULT_INPUT = Path("TestResults/unit/ticket-unit-coverage.opencover.xml")
OUTPUT_DIR = Path(__file__).parent / "reports"
# Generated or wiring code that tests are not expected to cover.
IGNORED_PARTS = ("/Migrations/", "/obj/", "/Program.cs")


def short(path):
    """A readable path starting at the project folder."""
    clean = path.replace("\\", "/")
    marker = "Ticket.Api/"
    return clean.split(marker, 1)[1] if marker in clean else clean


def read(xml_path):
    root = ET.parse(xml_path).getroot()
    names = {f.get("uid"): f.get("fullPath", "") for f in root.iter("File")}
    lines = defaultdict(dict)       # file uid -> {line number: hits}
    branches = defaultdict(lambda: [0, 0])   # file uid -> [branch points, visited]
    for method in root.iter("Method"):
        ref = method.find("FileRef")
        if ref is None:
            continue
        uid = ref.get("uid")
        for point in method.iter("SequencePoint"):
            line = int(point.get("sl"))
            lines[uid][line] = max(lines[uid].get(line, 0), int(point.get("vc")))
        for point in method.iter("BranchPoint"):
            branches[uid][0] += 1
            if int(point.get("vc")) > 0:
                branches[uid][1] += 1
    rows = []
    for uid, hits in lines.items():
        path = short(names.get(uid, uid))
        if any(part in "/" + path for part in IGNORED_PARTS):
            continue
        total, covered = len(hits), sum(1 for v in hits.values() if v > 0)
        b_total, b_covered = branches[uid]
        rows.append({"file": path, "lines": total, "covered": covered, "missing": total - covered,
                     "line_pct": 100 * covered / total if total else 100.0,
                     "branches": b_total, "branch_pct": 100 * b_covered / b_total if b_total else None})
    return sorted(rows, key=lambda r: (-r["missing"], r["file"]))


def totals(rows):
    lines, covered = sum(r["lines"] for r in rows), sum(r["covered"] for r in rows)
    return lines, covered, (100 * covered / lines if lines else 100.0)


def colour(pct):
    return "#2e7d32" if pct >= 80 else "#f9a825" if pct >= 50 else "#c62828"


def write_html(rows, out_path):
    lines, covered, pct = totals(rows)
    body = []
    for r in rows:
        bar = f'<div style="background:#e0e0e0;width:160px"><div style="background:{colour(r["line_pct"])};width:{r["line_pct"]:.0f}%;height:12px"></div></div>'
        branch = "-" if r["branch_pct"] is None else f'{r["branch_pct"]:.0f}%'
        body.append(f'<tr><td>{html.escape(r["file"])}</td><td>{r["covered"]}/{r["lines"]}</td><td>{r["line_pct"]:.1f}%</td>'
                    f'<td>{bar}</td><td>{branch}</td><td>{r["missing"]}</td></tr>')
    page = ("<!doctype html><meta charset='utf-8'><title>Ticket coverage</title>"
            "<style>body{font:14px system-ui;margin:28px}table{border-collapse:collapse}td,th{padding:6px 12px;border:1px solid #ccc;text-align:left}"
            "th{background:#eef2f6}</style><h1>Ticket service: unit test coverage</h1>"
            f"<p><b>{pct:.1f}%</b> of {lines} coverable lines are covered ({covered}). Migrations and Program.cs are left out. "
            "Files with the most untested lines come first.</p><table><tr><th>File</th><th>Lines</th><th>Line %</th><th></th><th>Branch %</th><th>Untested lines</th></tr>"
            + "".join(body) + "</table>")
    out_path.write_text(page, encoding="utf-8")


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_INPUT
    if not path.exists():
        print(f"No coverage file at {path}. Run the unit tests first: ./scripts/run-tests.sh unit", file=sys.stderr)
        return 2
    rows = read(path)
    lines, covered, pct = totals(rows)
    print(f"Ticket coverage from {path}")
    print(f"  {pct:.1f}% of {lines} coverable lines covered (migrations and Program.cs left out)\n")
    print(f"  {'File':52} {'Lines':>9} {'Line %':>8} {'Branch %':>9} {'Untested':>9}")
    for r in rows[:12]:
        branch = "-" if r["branch_pct"] is None else f'{r["branch_pct"]:.0f}%'
        print(f"  {r['file']:52} {str(r['covered']) + '/' + str(r['lines']):>9} {r['line_pct']:>7.1f}% {branch:>9} {r['missing']:>9}")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    write_html(rows, OUTPUT_DIR / "coverage.html")
    print(f"\n  Full report: {OUTPUT_DIR / 'coverage.html'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

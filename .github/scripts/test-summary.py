"""Print a markdown summary of one service's test run for the GitHub job summary.

Usage: test-summary.py <service> <results-dir>

Reads the .trx file (pass/fail/skip counts) and the opencover XML (line and branch
coverage) that the test step writes into <results-dir>. Prints a note instead of failing
when a file is missing, so a broken test step still gets a readable summary.
"""
import glob
import sys
import xml.etree.ElementTree as ET


def local(tag):
    return tag.rsplit('}', 1)[-1]


def trx_counts(path):
    for element in ET.parse(path).getroot().iter():
        if local(element.tag) == 'Counters':
            return {key: int(element.get(key, 0)) for key in ('total', 'passed', 'failed', 'notExecuted')}
    return None


def opencover_summary(path):
    for element in ET.parse(path).getroot().iter('Summary'):
        return float(element.get('sequenceCoverage', 0)), float(element.get('branchCoverage', 0))
    return None


def main():
    service, results = sys.argv[1], sys.argv[2]
    print(f'### {service}: tests and coverage\n')

    trx_files = glob.glob(f'{results}/**/*.trx', recursive=True)
    if trx_files:
        counts = trx_counts(trx_files[0])
        if counts:
            print('| Total | Passed | Failed | Not run |')
            print('|---|---|---|---|')
            print(f"| {counts['total']} | {counts['passed']} | {counts['failed']} | {counts['notExecuted']} |\n")
    else:
        print('No test result file was produced.\n')

    coverage_files = glob.glob(f'{results}/**/*.opencover.xml', recursive=True)
    if coverage_files:
        summary = opencover_summary(coverage_files[0])
        if summary:
            print(f'Line coverage **{summary[0]:.1f}%**, branch coverage **{summary[1]:.1f}%**.')
    else:
        print('No coverage report was produced.')


if __name__ == '__main__':
    main()

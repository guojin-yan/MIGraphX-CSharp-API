#!/usr/bin/env python3

import json
import pathlib
import sys


def fail(message):
    raise SystemExit(message)


def value(obj, *names, default=None):
    for name in names:
        if name in obj:
            return obj[name]
    return default


if len(sys.argv) < 8:
    fail("usage: aggregate-long-run.py OUTPUT SOURCE VERSION PHASE TOTAL_SECONDS INTERVAL_SECONDS PROCESS_JSON...")

output = pathlib.Path(sys.argv[1])
source_sha = sys.argv[2]
version = sys.argv[3]
phase = sys.argv[4]
total_seconds = int(sys.argv[5])
interval_seconds = int(sys.argv[6])
process_paths = [pathlib.Path(value) for value in sys.argv[7:]]
if not process_paths or interval_seconds != 600:
    fail("long-run aggregation requires at least one process and a 600-second interval")

reports = []
for path in process_paths:
    with path.open(encoding="utf-8") as stream:
        report = json.load(stream)
    if value(report, "State", "state") != "executed" or value(report, "Evidence", "evidence") != "runtime-candidate-executed-review-required":
        fail(f"probe report is not an executed review-required result: {path}")
    if value(report, "SourceSha", "sourceSha") != source_sha:
        fail(f"probe source SHA mismatch: {path}")
    managed_identity = value(report, "ManagedIdentity", "managedIdentity", default={}) or {}
    if value(managed_identity, "PackageVersion", "packageVersion") != version:
        fail(f"probe package version mismatch: {path}")
    cases = value(report, "Cases", "cases", default=[]) or []
    if len(cases) != 1 or value(cases[0], "State", "state") != "passed":
        fail(f"probe case did not pass: {path}")
    detail = value(cases[0], "Detail", "detail", default={}) or {}
    if detail.get("phase") != phase or detail.get("failures") != 0:
        fail(f"probe phase/failure mismatch: {path}")
    reports.append((path, report, cases[0], detail))

duration = sum(int(detail.get("durationSeconds", 0)) for _, _, _, detail in reports)
iterations = sum(int(detail.get("iterations", 0)) for _, _, _, detail in reports)
failures = sum(int(detail.get("failures", 0)) for _, _, _, detail in reports)
if duration != total_seconds or failures != 0:
    fail(f"aggregated duration/failure mismatch: duration={duration}, failures={failures}")

first_report = reports[0][1]
first_case = reports[0][2]
aggregate_detail = {
    "phase": phase,
    "durationSeconds": duration,
    "iterations": iterations,
    "failures": failures,
    "rssRecoveryObserved": all(detail.get("rssRecoveryObserved") is True for _, _, _, detail in reports),
    "gpuMemoryObservation": "not-collected-by-managed-probe",
    "hostRestartHandledByWrapper": False,
    "processRestartIntervalSeconds": interval_seconds,
    "processCount": len(reports),
    "processReports": [
        {
            "file": path.name,
            "startedUtc": value(report, "StartedUtc", "startedUtc"),
            "completedUtc": value(report, "CompletedUtc", "completedUtc"),
            "durationSeconds": detail.get("durationSeconds"),
            "iterations": detail.get("iterations"),
            "failures": detail.get("failures"),
            "rssRecoveryObserved": detail.get("rssRecoveryObserved"),
        }
        for path, report, _, detail in reports
    ],
}
result = {
    "SchemaVersion": value(first_report, "SchemaVersion", "schemaVersion", default="1.0.0"),
    "State": "executed",
    "Evidence": "runtime-candidate-executed-review-required",
    "Phase": value(first_report, "Phase", "phase", default="long-run"),
    "SourceSha": source_sha,
    "StartedUtc": value(reports[0][1], "StartedUtc", "startedUtc"),
    "CompletedUtc": value(reports[-1][1], "CompletedUtc", "completedUtc"),
    "ManagedIdentity": managed_identity,
    "Cases": [{
        "Id": value(first_case, "Id", "id", default="m11-long-run-resource-recovery"),
        "State": "passed",
        "DurationMilliseconds": sum(int(value(case, "DurationMilliseconds", "durationMilliseconds", default=0)) for _, _, case, _ in reports),
        "Detail": aggregate_detail,
        "Exception": None,
        "Message": None,
    }],
    "Exception": None,
    "Message": None,
}
output.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")

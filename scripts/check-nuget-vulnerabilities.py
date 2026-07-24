#!/usr/bin/env python3
"""Fail CI when high/critical NuGet advisories are present.

Reads `dotnet list package --vulnerable --include-transitive --format json`
output and exits nonzero for High/Critical findings that are not covered by a
non-expired allowlist entry.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Any, Iterable
from urllib.parse import urlsplit, urlunsplit


FAIL_SEVERITIES = frozenset({"high", "critical"})


@dataclass(frozen=True)
class Finding:
    project: str
    package_id: str
    resolved_version: str
    severity: str
    advisory_url: str
    scope: str


@dataclass(frozen=True)
class AllowlistEntry:
    package_id: str
    advisory_url: str
    reason: str
    expires_on: date


def normalize_advisory_url(url: str) -> str:
    parts = urlsplit((url or "").strip())
    path = parts.path.rstrip("/")
    return urlunsplit((parts.scheme.lower(), parts.netloc.lower(), path, "", ""))


def parse_allowlist(path: Path | None, today: date) -> list[AllowlistEntry]:
    if path is None:
        return []

    data = json.loads(path.read_text(encoding="utf-8"))
    entries: list[AllowlistEntry] = []
    for raw in data.get("exceptions", []):
        expires_on = date.fromisoformat(str(raw["expiresOn"]))
        if expires_on < today:
            continue
        entries.append(
            AllowlistEntry(
                package_id=str(raw["packageId"]).strip(),
                advisory_url=normalize_advisory_url(str(raw["advisoryUrl"])),
                reason=str(raw.get("reason", "")).strip(),
                expires_on=expires_on,
            )
        )
    return entries


def is_allowed(finding: Finding, allowlist: Iterable[AllowlistEntry]) -> AllowlistEntry | None:
    advisory = normalize_advisory_url(finding.advisory_url)
    for entry in allowlist:
        if (
            entry.package_id.lower() == finding.package_id.lower()
            and entry.advisory_url == advisory
        ):
            return entry
    return None


def iter_packages(framework: dict[str, Any]) -> Iterable[tuple[str, dict[str, Any]]]:
    for scope, key in (
        ("top-level", "topLevelPackages"),
        ("transitive", "transitivePackages"),
    ):
        for package in framework.get(key) or []:
            yield scope, package


def collect_findings(report: dict[str, Any]) -> list[Finding]:
    findings: list[Finding] = []
    for project in report.get("projects") or []:
        project_path = str(project.get("path") or "")
        for framework in project.get("frameworks") or []:
            for scope, package in iter_packages(framework):
                package_id = str(package.get("id") or "")
                resolved = str(
                    package.get("resolvedVersion")
                    or package.get("resolvedversion")
                    or ""
                )
                for vuln in package.get("vulnerabilities") or []:
                    severity = str(vuln.get("severity") or "").strip()
                    advisory_url = str(
                        vuln.get("advisoryurl")
                        or vuln.get("advisoryUrl")
                        or ""
                    ).strip()
                    findings.append(
                        Finding(
                            project=project_path,
                            package_id=package_id,
                            resolved_version=resolved,
                            severity=severity,
                            advisory_url=advisory_url,
                            scope=scope,
                        )
                    )
    return findings


def write_summary(
    path: Path | None,
    *,
    failing: list[Finding],
    allowed: list[tuple[Finding, AllowlistEntry]],
    visible: list[Finding],
) -> None:
    if path is None:
        return

    lines = [
        "## NuGet vulnerability gate",
        "",
        f"- High/critical failing: **{len(failing)}**",
        f"- High/critical allowlisted: **{len(allowed)}**",
        f"- Low/moderate visible: **{len(visible)}**",
        "",
    ]

    def table(title: str, rows: list[Finding], extra: str | None = None) -> None:
        lines.append(f"### {title}")
        lines.append("")
        if not rows:
            lines.append("_None_")
            lines.append("")
            return
        headers = "| Package | Version | Severity | Scope | Advisory |"
        sep = "| --- | --- | --- | --- | --- |"
        if extra:
            headers = "| Package | Version | Severity | Scope | Advisory | Notes |"
            sep = "| --- | --- | --- | --- | --- | --- |"
        lines.extend([headers, sep])
        for finding in rows:
            advisory = finding.advisory_url or "(missing)"
            base = (
                f"| `{finding.package_id}` | `{finding.resolved_version}` | "
                f"{finding.severity} | {finding.scope} | {advisory}"
            )
            if extra is None:
                lines.append(base + " |")
            else:
                lines.append(base + f" | {extra} |")
        lines.append("")

    table("Failing (high/critical)", failing)
    if allowed:
        lines.append("### Allowlisted (high/critical)")
        lines.append("")
        lines.append(
            "| Package | Version | Severity | Scope | Advisory | Reason | Expires |"
        )
        lines.append("| --- | --- | --- | --- | --- | --- | --- |")
        for finding, entry in allowed:
            lines.append(
                f"| `{finding.package_id}` | `{finding.resolved_version}` | "
                f"{finding.severity} | {finding.scope} | {finding.advisory_url} | "
                f"{entry.reason or '_n/a_'} | {entry.expires_on.isoformat()} |"
            )
        lines.append("")
    table("Other severities", visible)

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write("\n".join(lines))
        if not lines[-1].endswith("\n"):
            handle.write("\n")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "report",
        type=Path,
        help="JSON report from `dotnet list package --vulnerable --format json`",
    )
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=None,
        help="Optional JSON allowlist of temporary exceptions",
    )
    parser.add_argument(
        "--summary",
        type=Path,
        default=None,
        help="Append a Markdown summary (typically $GITHUB_STEP_SUMMARY)",
    )
    parser.add_argument(
        "--today",
        type=str,
        default=None,
        help="Override today (YYYY-MM-DD) for allowlist expiry checks",
    )
    args = parser.parse_args(argv)

    today = (
        date.fromisoformat(args.today)
        if args.today
        else datetime.now(timezone.utc).date()
    )
    report = json.loads(args.report.read_text(encoding="utf-8"))
    allowlist = parse_allowlist(args.allowlist, today)
    findings = collect_findings(report)

    failing: list[Finding] = []
    allowed: list[tuple[Finding, AllowlistEntry]] = []
    visible: list[Finding] = []

    for finding in findings:
        severity = finding.severity.lower()
        if severity not in FAIL_SEVERITIES:
            visible.append(finding)
            continue
        entry = is_allowed(finding, allowlist)
        if entry is not None:
            allowed.append((finding, entry))
        else:
            failing.append(finding)

    write_summary(args.summary, failing=failing, allowed=allowed, visible=visible)

    if not findings:
        print("No NuGet vulnerabilities reported.")
        return 0

    for finding in visible:
        print(
            f"info: {finding.severity} {finding.package_id} "
            f"{finding.resolved_version} ({finding.scope}) {finding.advisory_url}"
        )
    for finding, entry in allowed:
        print(
            f"allowlisted: {finding.severity} {finding.package_id} "
            f"{finding.resolved_version} until {entry.expires_on.isoformat()} "
            f"({entry.reason}) {finding.advisory_url}"
        )
    for finding in failing:
        print(
            f"error: {finding.severity} {finding.package_id} "
            f"{finding.resolved_version} ({finding.scope}) {finding.advisory_url}",
            file=sys.stderr,
        )

    if failing:
        print(
            f"NuGet vulnerability gate failed: {len(failing)} high/critical "
            "finding(s).",
            file=sys.stderr,
        )
        return 1

    print(
        f"NuGet vulnerability gate passed "
        f"({len(allowed)} allowlisted high/critical, {len(visible)} lower severity)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

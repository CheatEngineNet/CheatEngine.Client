"""Validate the versioned Client engineering backlog without contacting GitHub."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections.abc import Iterable, Mapping
from pathlib import Path
from typing import Any


MANIFEST_RELATIVE_PATH = Path("docs/engineering/backlog.json")
WORK_ITEMS_RELATIVE_PATH = Path("docs/engineering/work-items")
ARCHIVE_RECONCILIATION_RELATIVE_PATH = Path("docs/engineering/archive-reconciliation.json")
PLANNING_ID = re.compile(r"^CLI-(?:PLAN|E0[1-8]|0(?:0[1-9]|1[0-9]|2[0-4]))$")
ALLOWED_KINDS = frozenset({"roadmap", "epic", "governance", "bug", "feature", "validation", "research"})
ALLOWED_PRIORITIES = frozenset({"P1", "P2", "P3"})
ALLOWED_STATUSES = frozenset(
    {
        "Needs refinement",
        "Contract blocked",
        "Artifact blocked",
        "Ready",
        "In progress",
        "In review",
        "Qualified",
        "Deferred",
    }
)
REQUIRED_ITEM_FIELDS = frozenset(
    {
        "id",
        "repository_key",
        "kind",
        "title",
        "parent",
        "epic",
        "milestone",
        "priority",
        "observation",
        "objective",
        "requirements",
        "acceptance",
        "tests",
        "risks",
        "blocked_by",
        "sources",
        "branch",
        "pr_title",
        "scope_exclusions",
        "completion_artifacts",
        "mandatory",
        "evidence",
        "children",
        "blocks",
        "status",
        "repository",
        "artifact_gate",
        "labels",
        "path",
        "long_term_impact",
        "benefits",
        "body_template",
    }
)


def require(condition: bool, message: str) -> None:
    """Raise an always-enabled validation error instead of relying on assert."""
    if not condition:
        raise ValueError(message)


def require_non_empty_strings(value: Any, field: str, item_id: str) -> list[str]:
    require(isinstance(value, list) and value, f"{item_id}: {field} must be a non-empty array.")
    require(
        all(isinstance(entry, str) and entry.strip() for entry in value),
        f"{item_id}: {field} must contain non-empty strings.",
    )
    return value


def ensure_acyclic(dependencies: Mapping[str, Iterable[str]]) -> None:
    """Fail with the cycle path when local blocked-by dependencies are cyclic."""
    state: dict[str, int] = {}
    stack: list[str] = []

    def visit(node: str) -> None:
        node_state = state.get(node, 0)
        if node_state == 1:
            start = stack.index(node)
            raise ValueError("Cyclic local blocked_by dependency: " + " -> ".join([*stack[start:], node]))
        if node_state == 2:
            return

        state[node] = 1
        stack.append(node)
        for dependency in dependencies[node]:
            visit(dependency)
        stack.pop()
        state[node] = 2

    for item_id in dependencies:
        visit(item_id)


def validate_manifest_data(data: Mapping[str, Any]) -> tuple[dict[str, dict[str, Any]], dict[str, int]]:
    """Validate backlog schema and local/cross-repository dependency invariants."""
    require(data.get("schema_version") == 1, "backlog.json: schema_version must be 1.")
    require(data.get("bootstrap_id") == "ce-engineering-2026-09-21", "backlog.json: unexpected bootstrap_id.")

    repository = data.get("repository")
    require(isinstance(repository, dict), "backlog.json: repository must be an object.")
    require(repository.get("key") == "client", "backlog.json: repository.key must be client.")
    require(
        repository.get("repository") == "CheatEngineNet/CheatEngine.Client",
        "backlog.json: repository.repository must be CheatEngineNet/CheatEngine.Client.",
    )

    raw_items = data.get("items")
    require(isinstance(raw_items, list) and raw_items, "backlog.json: items must be a non-empty array.")
    items: dict[str, dict[str, Any]] = {}
    for item in raw_items:
        require(isinstance(item, dict), "backlog.json: every item must be an object.")
        item_id = item.get("id")
        require(isinstance(item_id, str) and PLANNING_ID.fullmatch(item_id), f"Invalid planning ID: {item_id!r}.")
        require(item_id not in items, f"Duplicate planning ID: {item_id}.")
        missing = REQUIRED_ITEM_FIELDS - item.keys()
        require(not missing, f"{item_id}: missing required fields: {', '.join(sorted(missing))}.")
        require(item["repository_key"] == "client", f"{item_id}: repository_key must be client.")
        require(
            item["repository"] == repository["repository"],
            f"{item_id}: repository does not match manifest repository.",
        )
        require(item["kind"] in ALLOWED_KINDS, f"{item_id}: unknown kind {item['kind']!r}.")
        require(item["priority"] in ALLOWED_PRIORITIES, f"{item_id}: unknown priority {item['priority']!r}.")
        require(item["status"] in ALLOWED_STATUSES, f"{item_id}: unknown status {item['status']!r}.")
        require_non_empty_strings(item["requirements"], "requirements", item_id)
        require_non_empty_strings(item["acceptance"], "acceptance", item_id)
        require_non_empty_strings(item["tests"], "tests", item_id)
        require_non_empty_strings(item["sources"], "sources", item_id)
        require_non_empty_strings(item["scope_exclusions"], "scope_exclusions", item_id)
        require_non_empty_strings(item["completion_artifacts"], "completion_artifacts", item_id)
        require(isinstance(item["blocked_by"], list), f"{item_id}: blocked_by must be an array.")
        require(isinstance(item["blocks"], list), f"{item_id}: blocks must be an array.")
        require(isinstance(item["children"], list), f"{item_id}: children must be an array.")
        required_text = [
            item["title"],
            item["observation"],
            item["objective"],
            item["risks"],
            item["mandatory"],
            item["evidence"],
            item["artifact_gate"],
            item["long_term_impact"],
            item["benefits"],
            item["body_template"],
        ]
        require(
            all(isinstance(value, str) and value.strip() for value in required_text),
            f"{item_id}: required text fields must be non-empty strings.",
        )
        if item["kind"] in {"roadmap", "epic"}:
            require(
                item["branch"] is None and item["pr_title"] is None,
                f"{item_id}: roadmap and epic items must not propose an implementation branch or PR title.",
            )
        else:
            require(
                isinstance(item["branch"], str)
                and item["branch"].strip()
                and isinstance(item["pr_title"], str)
                and item["pr_title"].strip(),
                f"{item_id}: implementation leaves must include a branch and PR title.",
            )
        marker = f"<!-- ce-bootstrap:{item_id} -->"
        require(marker in item["body_template"], f"{item_id}: body_template has no stable marker.")
        expected_path = (WORK_ITEMS_RELATIVE_PATH / f"{item_id}.md").as_posix()
        require(item["path"] == expected_path, f"{item_id}: path must be {expected_path}.")
        require(
            {"ce:bootstrap", f"ce:kind:{item['kind']}", f"ce:priority:{item['priority']}"}.issubset(item["labels"]),
            f"{item_id}: labels must include bootstrap, kind, and priority values.",
        )
        items[item_id] = item

    milestones = data.get("milestones")
    require(isinstance(milestones, list) and milestones, "backlog.json: milestones must be a non-empty array.")
    milestone_ids: set[str] = set()
    for milestone in milestones:
        require(isinstance(milestone, dict), "backlog.json: every milestone must be an object.")
        milestone_id = milestone.get("id")
        require(
            isinstance(milestone_id, str) and re.fullmatch(r"CLI-M[0-6]", milestone_id),
            f"Invalid milestone ID: {milestone_id!r}.",
        )
        require(milestone_id not in milestone_ids, f"Duplicate milestone ID: {milestone_id}.")
        require(milestone.get("repository_key") == "client", f"{milestone_id}: repository_key must be client.")
        require(
            milestone.get("due_on") is None and milestone.get("release_version") is None,
            f"{milestone_id}: bootstrap must not invent a due date or release version.",
        )
        milestone_ids.add(milestone_id)

    external_dependencies = data.get("external_dependencies")
    require(isinstance(external_dependencies, list), "backlog.json: external_dependencies must be an array.")
    external_edges: set[tuple[str, str]] = set()
    for edge in external_dependencies:
        require(isinstance(edge, dict), "backlog.json: every external dependency must be an object.")
        blocker, blocked, relationship = edge.get("blocker"), edge.get("blocked"), edge.get("relationship")
        require(isinstance(blocker, str) and blocker.startswith("SDK-"), f"Invalid external blocker: {blocker!r}.")
        require(blocked in items, f"External dependency has unknown Client item: {blocked!r}.")
        require(relationship == "blocked_by", f"{blocker} -> {blocked}: relationship must be blocked_by.")
        require((blocker, blocked) not in external_edges, f"Duplicate external dependency: {blocker} -> {blocked}.")
        external_edges.add((blocker, blocked))

    local_dependencies: dict[str, list[str]] = {}
    for item_id, item in items.items():
        parent = item["parent"]
        require(parent is None or parent in items, f"{item_id}: parent references an unknown item.")
        if parent is not None:
            require(item_id in items[parent]["children"], f"{item_id}: parent does not list this child.")
        for child in item["children"]:
            require(child in items, f"{item_id}: child references an unknown item: {child!r}.")
            require(items[child]["parent"] == item_id, f"{item_id}: child {child} has a different parent.")
        milestone = item["milestone"]
        require(milestone is None or milestone in milestone_ids, f"{item_id}: unknown milestone {milestone!r}.")
        if item["kind"] == "roadmap":
            require(
                parent is None and milestone is None and item["epic"] is None,
                f"{item_id}: roadmap must be the hierarchy root.",
            )
        elif item["kind"] == "epic":
            require(item["epic"] is None, f"{item_id}: an epic cannot belong to another epic.")
        else:
            require(
                item["epic"] in items and items[item["epic"]]["kind"] == "epic",
                f"{item_id}: leaf must name an existing epic.",
            )
            require(parent == item["epic"], f"{item_id}: leaf parent and epic must agree.")

        local_dependencies[item_id] = []
        for blocker in item["blocked_by"]:
            require(isinstance(blocker, str) and blocker, f"{item_id}: blocked_by contains an invalid value.")
            if blocker in items:
                require(
                    item_id in items[blocker]["blocks"],
                    f"{item_id}: local blocker {blocker} lacks the reverse blocks edge.",
                )
                local_dependencies[item_id].append(blocker)
            else:
                require((blocker, item_id) in external_edges, f"{item_id}: undeclared external blocker {blocker}.")
        for blocked in item["blocks"]:
            require(blocked in items, f"{item_id}: blocks references an unknown item: {blocked!r}.")
            require(
                item_id in items[blocked]["blocked_by"],
                f"{item_id}: blocked item {blocked} lacks the reverse blocked_by edge.",
            )

    ensure_acyclic(local_dependencies)

    for blocker, blocked in external_edges:
        require(blocker in items[blocked]["blocked_by"], f"{blocker} -> {blocked}: missing external blocked_by edge.")

    return items, {"items": len(items), "milestones": len(milestone_ids), "external_dependencies": len(external_edges)}


def validate_work_item_documents(repository_root: Path, items: Mapping[str, Mapping[str, Any]]) -> None:
    """Verify the actual versioned documents behind the manifest item paths."""
    for item_id, item in items.items():
        document = repository_root / item["path"]
        require(document.is_file(), f"{item_id}: missing work-item document {item['path']}.")
        content = document.read_text(encoding="utf-8")
        marker = f"<!-- ce-bootstrap:{item_id} -->"
        require(content.count(marker) == 1, f"{item_id}: document must contain exactly one stable marker.")
        require(f"## {item_id} " in content, f"{item_id}: document has no matching level-two heading.")


def validate_archive_reconciliation(repository_root: Path, item_ids: set[str]) -> dict[str, int]:
    """Validate the checked-in, bounded reconciliation of the supplied review archive."""
    reconciliation_path = repository_root / ARCHIVE_RECONCILIATION_RELATIVE_PATH
    require(
        reconciliation_path.is_file(),
        f"Missing archive reconciliation: {ARCHIVE_RECONCILIATION_RELATIVE_PATH.as_posix()}.",
    )
    try:
        reconciliation = json.loads(reconciliation_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exception:
        raise ValueError(
            f"Invalid JSON in {ARCHIVE_RECONCILIATION_RELATIVE_PATH.as_posix()}: {exception}"
        ) from exception

    require(isinstance(reconciliation, dict), "archive-reconciliation.json: root must be an object.")
    require(reconciliation.get("schema_version") == 1, "archive-reconciliation.json: schema_version must be 1.")

    archive = reconciliation.get("archive")
    require(isinstance(archive, dict), "archive-reconciliation.json: archive must be an object.")
    require(
        archive.get("file_name") == "CheatEngineNet_Architecture_Review_2026-09-21.zip",
        "archive-reconciliation.json: unexpected archive file name.",
    )
    require(
        isinstance(archive.get("sha256"), str) and re.fullmatch(r"[0-9a-f]{64}", archive["sha256"]),
        "archive-reconciliation.json: archive.sha256 must be a lowercase SHA-256 value.",
    )
    require(
        archive.get("content_manifest") == "SHA256SUMS.json"
        and isinstance(archive.get("content_manifest_sha256"), str)
        and re.fullmatch(r"[0-9a-f]{64}", archive["content_manifest_sha256"]),
        "archive-reconciliation.json: content manifest identity is invalid.",
    )

    counts = reconciliation.get("counts")
    require(isinstance(counts, dict), "archive-reconciliation.json: counts must be an object.")
    expected_counts = {
        "findings": 36,
        "specified_product_scenarios": 80,
        "ownership_rows": 32,
        "capability_rows": 17,
    }
    require(counts == expected_counts, "archive-reconciliation.json: unexpected archive inventory counts.")

    evidence_levels = reconciliation.get("evidence_levels")
    require(
        evidence_levels == ["source", "package", "fixture", "live"],
        "archive-reconciliation.json: evidence_levels must preserve the source/package/fixture/live boundary.",
    )
    ownership = reconciliation.get("ownership_summary")
    require(
        isinstance(ownership, list) and ownership,
        "archive-reconciliation.json: ownership_summary must be non-empty.",
    )
    for entry in ownership:
        require(isinstance(entry, dict), "archive-reconciliation.json: every ownership entry must be an object.")
        require(
            all(
                isinstance(entry.get(key), str) and entry[key].strip()
                for key in ("area", "owner", "client_boundary")
            ),
            "archive-reconciliation.json: ownership entries require area, owner, and client_boundary.",
        )
        require(entry["owner"] in {"SDK", "Client", "Shared"}, "archive-reconciliation.json: unknown ownership owner.")

    groups = reconciliation.get("finding_groups")
    require(isinstance(groups, list) and groups, "archive-reconciliation.json: finding_groups must be non-empty.")
    reconciled_findings: list[str] = []
    for group in groups:
        require(isinstance(group, dict), "archive-reconciliation.json: every finding group must be an object.")
        require(
            all(isinstance(group.get(key), str) and group[key].strip() for key in ("id", "description")),
            "archive-reconciliation.json: finding groups require id and description.",
        )
        findings = require_non_empty_strings(group.get("findings"), "findings", group["id"])
        client_items = require_non_empty_strings(group.get("client_items"), "client_items", group["id"])
        require(set(client_items) <= item_ids, f"{group['id']}: references an unknown Client planning item.")
        reconciled_findings.extend(findings)

    expected_findings = {f"R{number:02d}" for number in range(1, 37)}
    require(
        set(reconciled_findings) == expected_findings and len(reconciled_findings) == len(expected_findings),
        "archive-reconciliation.json: finding groups must cover R01 through R36 exactly once.",
    )
    return {
        "archive_findings": counts["findings"],
        "specified_product_scenarios": counts["specified_product_scenarios"],
    }


def validate_repository(repository_root: Path) -> dict[str, int]:
    """Validate the checked-in manifest and matching work-item documents."""
    root = repository_root.resolve()
    manifest_path = root / MANIFEST_RELATIVE_PATH
    require(manifest_path.is_file(), f"Missing engineering manifest: {MANIFEST_RELATIVE_PATH.as_posix()}.")
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exception:
        raise ValueError(f"Invalid JSON in {MANIFEST_RELATIVE_PATH.as_posix()}: {exception}") from exception
    require(isinstance(data, dict), "backlog.json: root must be an object.")
    items, summary = validate_manifest_data(data)
    validate_work_item_documents(root, items)
    summary.update(validate_archive_reconciliation(root, set(items)))
    return summary


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repository-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root containing docs/engineering/backlog.json (default: script parent).",
    )
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    try:
        summary = validate_repository(arguments.repository_root)
    except (OSError, ValueError) as exception:
        print(f"Engineering manifest validation failed: {exception}", file=sys.stderr)
        return 1
    print(
        "Validated {items} engineering work items, {milestones} milestones, "
        "{external_dependencies} external dependencies, and the {archive_findings}-finding/"
        "{specified_product_scenarios}-scenario archive reconciliation; product tests were not executed.".format(
            **summary
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

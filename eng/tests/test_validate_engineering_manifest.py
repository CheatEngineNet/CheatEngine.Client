from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = REPOSITORY_ROOT / "eng" / "Validate-EngineeringManifest.py"
SPECIFICATION = importlib.util.spec_from_file_location("engineering_manifest_validator", VALIDATOR_PATH)
if SPECIFICATION is None or SPECIFICATION.loader is None:
    raise RuntimeError(f"Unable to load validator from {VALIDATOR_PATH}.")
VALIDATOR = importlib.util.module_from_spec(SPECIFICATION)
SPECIFICATION.loader.exec_module(VALIDATOR)


def load_manifest() -> dict:
    return json.loads((REPOSITORY_ROOT / "docs" / "engineering" / "backlog.json").read_text(encoding="utf-8"))


class EngineeringManifestValidatorTests(unittest.TestCase):
    def test_checked_in_manifest_and_documents_are_valid(self) -> None:
        summary = VALIDATOR.validate_repository(REPOSITORY_ROOT)

        self.assertEqual(
            summary,
            {
                "items": 33,
                "milestones": 7,
                "external_dependencies": 25,
                "archive_findings": 36,
                "specified_product_scenarios": 80,
            },
        )

    def test_external_dependency_must_belong_to_its_declared_client_item(self) -> None:
        manifest = load_manifest()
        cli_007 = next(item for item in manifest["items"] if item["id"] == "CLI-007")
        cli_007["blocked_by"].remove("SDK-008")
        cli_007["blocked_by"].append("SDK-007")

        with self.assertRaisesRegex(ValueError, "undeclared external blocker SDK-007"):
            VALIDATOR.validate_manifest_data(manifest)

    def test_item_path_must_name_the_versioned_work_item_document(self) -> None:
        manifest = load_manifest()
        cli_001 = next(item for item in manifest["items"] if item["id"] == "CLI-001")
        cli_001["path"] = "issues/client/CLI-001.md"

        with self.assertRaisesRegex(ValueError, "path must be docs/engineering/work-items/CLI-001.md"):
            VALIDATOR.validate_manifest_data(manifest)

    def test_cycle_is_rejected(self) -> None:
        dependencies = {"CLI-001": ["CLI-002"], "CLI-002": ["CLI-001"]}

        with self.assertRaisesRegex(ValueError, "Cyclic local blocked_by dependency"):
            VALIDATOR.ensure_acyclic(dependencies)

    def test_reverse_dependency_is_required(self) -> None:
        manifest = copy.deepcopy(load_manifest())
        cli_001 = next(item for item in manifest["items"] if item["id"] == "CLI-001")
        cli_001["blocks"] = []

        with self.assertRaisesRegex(ValueError, "lacks the reverse blocks edge"):
            VALIDATOR.validate_manifest_data(manifest)

    def test_archive_reconciliation_requires_every_finding_exactly_once(self) -> None:
        reconciliation_path = REPOSITORY_ROOT / "docs" / "engineering" / "archive-reconciliation.json"
        reconciliation = json.loads(reconciliation_path.read_text(encoding="utf-8"))
        reconciliation["finding_groups"][0]["findings"].remove("R17")
        temporary_root = self._write_reconciliation(reconciliation)
        item_ids = {item["id"] for item in load_manifest()["items"]}

        with self.assertRaisesRegex(ValueError, "cover R01 through R36 exactly once"):
            VALIDATOR.validate_archive_reconciliation(temporary_root, item_ids)

    def _write_reconciliation(self, reconciliation: dict) -> Path:
        temporary_root = self._temporary_directory()
        reconciliation_directory = temporary_root / "docs" / "engineering"
        reconciliation_directory.mkdir(parents=True)
        reconciliation_path = reconciliation_directory / "archive-reconciliation.json"
        reconciliation_path.write_text(json.dumps(reconciliation), encoding="utf-8")
        return temporary_root

    def _temporary_directory(self) -> Path:
        temporary_directory = self.enterContext(tempfile.TemporaryDirectory())
        return Path(temporary_directory)


if __name__ == "__main__":
    unittest.main()

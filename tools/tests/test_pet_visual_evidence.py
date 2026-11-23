"""HK pet visual evidence compiler contract."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.content.build_pet_visual_evidence import (
    compile_pet_visual_evidence, publish_pet_visual_evidence,
)


def cstring(value: str) -> bytes:
    return value.encode("cp950") + b"\0"


def source_fixture(path: Path) -> None:
    evolution = (
        b"\x00"
        + (50).to_bytes(2, "little")
        + (5).to_bytes(2, "little")
        + cstring("初生型")
        + cstring(r"Enemy\En050a")
        + b"\x00"
        + cstring("原版描述不應進 runtime evidence")
    )
    path.write_bytes(
        b"\x01\x00"
        + (5100).to_bytes(2, "little")
        + cstring("波奇鳥獸")
        + b"\x01"
        + evolution
    )


def atlas_fixture(root: Path, *, missing_attack: bool = False) -> None:
    target = root / "Enemy" / "en050a" / "atlas.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    views = {}
    for action in ("NS", "FA"):
        for facing in range(1, 9):
            if missing_attack and action == "FA" and facing == 8:
                continue
            key = f"EN050A_{action}{facing}"
            views[key] = {
                "clip": key, "flip_h": False,
                "timeline_ticks": [[0, 2]], "loop": action == "NS",
            }
    target.write_text(json.dumps({
        "version": 9,
        "asset_id": "Enemy/en050a",
        "logical_views": views,
        "animation_semantics": {
            "timing_verified": True,
            "direction_mapping_verified": True,
        },
    }), encoding="utf-8")


class PetVisualEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "petcli.ITD"
        source_fixture(self.source)

    def test_source_identity_compiles_without_replacing_runtime_definition(self):
        result = compile_pet_visual_evidence(
            self.source, source_reference="HK fixture")
        self.assertEqual(result["status"], "pass")
        self.assertEqual(result["mapping_count"], 1)
        mapping = result["mappings"][0]
        self.assertEqual(
            (mapping["source_pet_id"], mapping["evolution_id"], mapping["sprite"]),
            (5100, 0, "EN050A"),
        )
        self.assertEqual(mapping["asset_id"], "Enemy/en050a")
        self.assertTrue(result["runtime_definition_mapping_required"])
        self.assertTrue(result["sgtest_definition_ids_preserved"])
        self.assertEqual(result["action_semantics"]["status"], "INFERRED")
        serialized = json.dumps(result, ensure_ascii=False)
        self.assertNotIn("波奇鳥獸", serialized)
        self.assertNotIn("原版描述", serialized)

    def test_runtime_asset_validation_requires_normalized_idle_and_attack_views(self):
        assets = self.root / "assets"
        atlas_fixture(assets)
        result = compile_pet_visual_evidence(self.source, assets)
        self.assertTrue(result["runtime_atlas_validation"])
        self.assertEqual(result["validated_runtime_assets"], 1)
        atlas_fixture(assets, missing_attack=True)
        with self.assertRaisesRegex(ValueError, "missing logical view EN050A_FA8"):
            compile_pet_visual_evidence(self.source, assets)

    def test_runtime_asset_accepts_explicit_partial_ns_fa_verification_only(self):
        assets = self.root / "assets"
        atlas_fixture(assets)
        atlas = assets / "Enemy" / "en050a" / "atlas.json"
        payload = json.loads(atlas.read_text(encoding="utf-8"))
        payload["animation_semantics"] = {
            "status": "normalized_enemy_partial",
            "timing_verified": False,
            "direction_mapping_verified": False,
            "verified_behaviors": ["NS", "FA"],
        }
        atlas.write_text(json.dumps(payload), encoding="utf-8")
        result = compile_pet_visual_evidence(self.source, assets)
        self.assertEqual(result["validated_runtime_assets"], 1)

        payload["animation_semantics"]["verified_behaviors"] = ["NS"]
        atlas.write_text(json.dumps(payload), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "lacks verified NS/FA ACT semantics"):
            compile_pet_visual_evidence(self.source, assets)

    def test_extracted_source_validation_is_cached_by_unique_sprite(self):
        extracted = self.root / "extracted"
        extracted.mkdir()
        with patch(
            "asset_converter.content.build_pet_visual_evidence._validate_source_animation",
            return_value="a" * 64,
        ) as validate:
            result = compile_pet_visual_evidence(
                self.source, extracted_source=extracted)
        self.assertTrue(result["source_animation_validation"])
        self.assertEqual(result["validated_source_animations"]["count"], 1)
        self.assertEqual(
            result["validated_source_animations"]["content_sha256"],
            {"EN050A": "a" * 64},
        )
        validate.assert_called_once_with(extracted.resolve(), "EN050A")

    def test_publish_is_deterministic_and_cli_works_outside_repo(self):
        assets = self.root / "assets"
        atlas_fixture(assets)
        output = self.root / "evidence.json"
        first = publish_pet_visual_evidence(self.source, output, assets)
        before = output.read_bytes()
        second = publish_pet_visual_evidence(self.source, output, assets)
        self.assertEqual(before, output.read_bytes())
        self.assertEqual(first["content_sha256"], second["content_sha256"])

        cli_output = self.root / "cli-evidence.json"
        run = subprocess.run([
            sys.executable, str(REPO / "tools" / "asset_converter" / "cli.py"),
            "pet-visual-evidence",
            "--hk-petcli-itd", str(self.source),
            "--assets", str(assets),
            "--output", str(cli_output),
            "--source-reference", "HK fixture",
        ], cwd=self.root, capture_output=True, text=True)
        self.assertEqual(run.returncode, 0, run.stderr + run.stdout)
        self.assertEqual(json.loads(run.stdout)["status"], "pass")
        self.assertEqual(json.loads(cli_output.read_text(encoding="utf-8"))["mapping_count"], 1)


if __name__ == "__main__":
    unittest.main()

"""Old Server Pet growth evidence tests; no private game data required."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_pet_growth_curve import (
    SCHEMA,
    compile_pet_growth_curve,
    publish_pet_growth_curve,
)
from asset_converter.source.old_pet_level_curve import parse_old_pet_level_curve


class PetGrowthCurveTests(unittest.TestCase):
    @staticmethod
    def fixture(rows: list[int] | None = None) -> str:
        values = rows or list(range(1, 106))
        return "\n".join(str(value) for value in values) + "\n[end]\n"

    def test_old_loader_semantics_are_cumulative_and_bounded_to_100_rows(self):
        thresholds = parse_old_pet_level_curve(self.fixture())
        self.assertEqual(100, len(thresholds))
        self.assertEqual(1, thresholds[0])
        self.assertEqual(3, thresholds[1])
        self.assertEqual(5050, thresholds[-1])

    def test_invalid_or_short_source_fails_closed(self):
        with self.assertRaises(ValueError):
            parse_old_pet_level_curve("1\n2\n[end]\n")
        with self.assertRaises(ValueError):
            parse_old_pet_level_curve(self.fixture().replace("[end]", "oops"))
        rows = list(range(1, 101))
        rows[50] = 0
        with self.assertRaises(ValueError):
            parse_old_pet_level_curve(self.fixture(rows))

    def test_manifest_publication_preserves_only_audited_thresholds(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "Player_Up_Lv.csv"
            output = root / "pet_growth_curve.json"
            source.write_text(self.fixture(), encoding="ascii")
            result = publish_pet_growth_curve(
                source, output, source_reference="unit-test Old Pet curve")
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("pass", result["status"])
            self.assertEqual(SCHEMA, document["schema"])
            self.assertEqual(100, document["max_level"])
            self.assertEqual(5050, document["level_up_total_experience"][-1])
            self.assertEqual("unit-test Old Pet curve", document["source"])

            compiled = compile_pet_growth_curve(source)
            self.assertEqual(
                document["level_up_total_experience"],
                compiled["level_up_total_experience"])


if __name__ == "__main__":
    unittest.main()

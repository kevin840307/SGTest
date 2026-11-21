"""Stable source layout and cross-language wire naming checks.

Keep this free of Godot, .NET and private asset dependencies so every CI job
can validate architectural naming without running a game server.
"""
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / "client"
SERVER = ROOT / "server" / "src" / "SGTest.Server"
WIRE_CONTRACTS = SERVER / "Wire" / "WireContracts.cs"
WIRE_CODEC = CLIENT / "network" / "wire_codec.gd"


class ArchitectureConventionsChecks(unittest.TestCase):
    def test_feature_panels_follow_owning_directory(self) -> None:
        for folder, filename in (
            ("inventory", "inventory_panel.gd"),
            ("trade", "trade_panel.gd"),
            ("player_shop", "player_shop_panel.gd"),
            ("quest", "quest_panel.gd"),
        ):
            with self.subTest(folder=folder):
                self.assertTrue((CLIENT / folder / filename).is_file())

    def test_godot_scripts_use_snake_case_names(self) -> None:
        for script in CLIENT.rglob("*.gd"):
            if "assets" in script.parts:
                continue  # Generated/private source is owned by the converter.
            with self.subTest(path=script.relative_to(ROOT)):
                self.assertRegex(script.name, r"^[a-z][a-z0-9_]*\.gd$")

    def test_local_gdscript_preloads_refer_to_real_files(self) -> None:
        for script in CLIENT.rglob("*.gd"):
            if "assets" in script.parts:
                continue
            source = script.read_text(encoding="utf-8-sig")
            for relative in re.findall(r'preload\("res://([^"]+\.gd)"\)', source):
                with self.subTest(src=script.relative_to(ROOT), target=relative):
                    self.assertTrue((CLIENT / relative).is_file())

    def test_wire_contracts_have_one_owner(self) -> None:
        contracts = WIRE_CONTRACTS.read_text(encoding="utf-8-sig")
        self.assertIn("public enum WireOpcode : ushort", contracts)
        self.assertNotIn("public static partial class WireProtocol", contracts)
        for part in ("WireProtocol.cs", "WireProtocol.Economy.cs",
                     "WireProtocol.Social.cs", "WireProtocol.Pvp.cs",
                     "WireProtocol.Quest.cs"):
            with self.subTest(part=part):
                source = (SERVER / "Wire" / part).read_text(encoding="utf-8-sig")
                self.assertIn("public static partial class WireProtocol", source)
                self.assertNotIn("public enum WireOpcode : ushort", source)

    def test_shared_opcode_numbers_match_across_languages(self) -> None:
        # WireOpcode declarations are the durable public API; no display name,
        # localized state, or presentation color is allowed to renumber them.
        contract = WIRE_CONTRACTS.read_text(encoding="utf-8-sig")
        match = re.search(r"public enum WireOpcode\s*:\s*ushort\s*\{([^}]*)\}", contract, re.S)
        self.assertIsNotNone(match)
        csharp = {
            re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name).upper(): int(code)
            for name, code in re.findall(r"\b([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+)", match.group(1))
        }
        godot = {
            name: int(code)
            for name, code in re.findall(
                r"^const OPCODE_([A-Z][A-Z0-9_]*)\s*:=\s*(\d+)\s*$",
                WIRE_CODEC.read_text(encoding="utf-8-sig"),
                re.M,
            )
        }
        common = csharp.keys() & godot.keys()
        self.assertGreaterEqual(len(common), 70, "Unrecognized wire naming drift")
        for name in sorted(common):
            with self.subTest(opcode=name):
                self.assertEqual(csharp[name], godot[name])


if __name__ == "__main__":
    unittest.main()

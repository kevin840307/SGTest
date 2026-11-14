import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_atlas_groups import build_group, group_key, select_groups


def tiny_spr() -> bytes:
    # One 2x1 RGB565 red pixel followed by a transparent skip.
    row = struct.pack("<HHHH", 1, 0xF800, 0x8001, 0x8000)
    frame = struct.pack("<IIiiI", 2, 1, 1, 0, 20) + row
    return frame + struct.pack("<II", 0, 1)


class GroupTests(unittest.TestCase):
    def test_prefix_key_preserves_directory(self):
        self.assertEqual(group_key(Path("Enemy/EN023A_NS1.spr")), Path("Enemy/EN023A"))
        self.assertEqual(group_key(Path("Player/A/Cloth/A-Back07_AS1.spr")),
                         Path("Player/A/Cloth/A-Back07"))
        self.assertEqual(group_key(Path("Map/Gaea_building/GAEA_CITY_004.spr")),
                         Path("Map/Gaea_building/GAEA_CITY_004"))

    def test_group_manifest_references_each_source_frame(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            folder = root / "Enemy"
            folder.mkdir(parents=True)
            paths = [folder / "EN023A_NS1.spr", folder / "EN023A_NW1.spr"]
            for path in paths:
                path.write_bytes(tiny_spr())
            output = Path(temp) / "out"
            row = build_group(root, output, Path("Enemy/EN023A"), paths, 512)
            manifest = json.loads((output / "Enemy/EN023A/group.atlas.json").read_text())
            self.assertEqual((row["sources"], row["frames"], row["pages"]), (2, 2, 1))
            self.assertGreater(row["page_fill_ratio"], 0)
            self.assertEqual([f["source_frame"] for f in manifest["frames"]], [0, 0])
            self.assertEqual([f["source"] for f in manifest["frames"]],
                             ["Enemy/EN023A_NS1.spr", "Enemy/EN023A_NW1.spr"])
            self.assertEqual(manifest["frames"][0]["raw_origin"], [1, 0])

    def test_explicit_group_filter(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "Enemy").mkdir()
            for name in ("EN001_NS1.spr", "EN001_NW1.spr", "EN002_NS1.spr"):
                (root / "Enemy" / name).write_bytes(tiny_spr())
            groups = select_groups(root, keys={"enemy/en001"})
            self.assertEqual(list(groups), [Path("Enemy/EN001")])
            self.assertEqual(len(next(iter(groups.values()))), 2)


if __name__ == "__main__":
    unittest.main()

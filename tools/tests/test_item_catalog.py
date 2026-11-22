"""Source-derived item catalog contract; no private game data required in CI."""
import hashlib
import json
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO/"tools"))
from asset_converter.content.build_item_catalog import (
    compile_item_catalog,publish_item_catalog,
)
from asset_converter.source.hk_item_itd import (
    ID_OFFSET,NAME_OFFSET,RECORD_LENGTH,TOOLTIP_BYTES,TOOLTIP_OFFSET,
)


class ItemCatalogTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"Items"

    def source(self,category: str,number: int, *,name: str,
               declared_id=None,icon=421,declared_category=None,extra=None):
        folder=self.root/category
        folder.mkdir(parents=True,exist_ok=True)
        path=folder/f"{number}.tres"
        lines=[
            '[gd_resource type="Resource" format=3]',
            '[resource]',
            'id = '+str(number if declared_id is None else declared_id),
            'item_name = '+json.dumps(name,ensure_ascii=False),
            'icon_id = '+str(icon),
            'item_type_category = '+json.dumps(
                declared_category or category.capitalize()),
        ]
        for key,value in (extra or {}).items():
            lines.append(key+' = '+(
                json.dumps(value,ensure_ascii=False)
                if isinstance(value,str) else str(value)))
        path.write_text('\n'.join(lines)+'\n',encoding="utf-8")
        return path

    def test_utf8_names_source_hash_and_determinism(self):
        source=self.source("general",2701,name="測試石塊",
            extra={"description":"來源描述\n第二行","move_speed":-50})
        self.source("equip",15001,name="測試盔甲",icon=626,
            extra={"atk":257,"durability":50,"req_level":54,
                   "special_effect":"測試效果"})
        manifest=compile_item_catalog(self.root,
            source_reference="verified converted client Items")
        self.assertEqual(manifest["version"],2)
        self.assertEqual(manifest["item_count"],2)
        self.assertEqual([x["item_id"] for x in manifest["items"]],[2701,15001])
        self.assertEqual(manifest["items"][0]["name"],"測試石塊")
        self.assertEqual(manifest["items"][0]["icon_id"],421)
        self.assertEqual(manifest["items"][0]["presentation"]["move_speed"],-50)
        self.assertEqual(manifest["items"][0]["presentation"]["description"],
                         "來源描述\n第二行")
        equip=manifest["items"][1]["presentation"]
        self.assertEqual((equip["atk"],equip["durability"],equip["req_level"]),
                         (257,50,54))
        self.assertEqual(manifest["items"][0]["source_sha256"],
                         hashlib.sha256(source.read_bytes()).hexdigest())
        self.assertNotIn(str(self.root),json.dumps(manifest))
        out=Path(self.temporary.name)/"client"/"content"/"item_catalog.json"
        one=publish_item_catalog(self.root,out,
                 source_reference="verified converted client Items")
        before=out.read_bytes()
        two=publish_item_catalog(self.root,out,
                 source_reference="verified converted client Items")
        self.assertEqual(before,out.read_bytes())
        self.assertEqual(one["catalog_sha256"],two["catalog_sha256"])
        self.assertFalse(one["runtime_item_effects_verified"])

    def test_hk_unique_name_overlay_preserves_runtime_id(self):
        self.source("equip",15001,name="跨版本測試盔甲",icon=626,
                    extra={"atk":257,"durability":50})
        item_type=3
        hk=bytearray(RECORD_LENGTH[item_type])
        hk[0]=item_type
        struct.pack_into("<H",hk,ID_OFFSET[item_type],31207)
        name="跨版本測試盔甲".encode("cp950")
        hk[NAME_OFFSET[item_type]:NAME_OFFSET[item_type]+len(name)]=name
        tip="HK 原版說明\n等級限制： 54".encode("cp950")
        hk[TOOLTIP_OFFSET[item_type]:TOOLTIP_OFFSET[item_type]+len(tip)]=tip
        hk_path=Path(self.temporary.name)/"item.itd"
        hk_path.write_bytes(hk)
        item_extra=Path(self.temporary.name)/"item_extra.csv"
        item_extra.write_bytes(
            "31207,跨版本測試盔甲,EQUIP,0,1,1,1,A\r\n".encode("cp950"))
        grading=Path(self.temporary.name)/"grading.csv"
        grading.write_bytes((
            "S,248,60,60,255\r\n"
            "A,255,0,255,255\r\n"
            "B,75,255,255,255\r\n"
            "C,123,255,0,255\r\n"
            "D,255,255,255,255\r\n").encode("cp950"))
        manifest=compile_item_catalog(
            self.root,source_reference="runtime fixture",
            hk_item_itd=hk_path,hk_item_extra=item_extra,hk_grading=grading,
            hk_source_reference="HK fixture")
        entry=manifest["items"][0]
        self.assertEqual(entry["item_id"],15001)
        self.assertEqual(entry["presentation"]["hk_original_item_id"],31207)
        self.assertEqual(entry["presentation"]["hk_original_type"],3)
        self.assertIn("等級限制： 54",
                      entry["presentation"]["hk_original_tooltip"])
        self.assertEqual(entry["presentation"]["hk_grade"],"A")
        self.assertEqual(tuple(entry["presentation"][f"hk_grade_{c}"]
                               for c in "rgba"),(255,1,255,255))
        self.assertEqual(manifest["hk_evidence"]["unique_name_matches"],1)
        self.assertEqual(manifest["hk_evidence"]["tooltip_matches"],1)
        self.assertEqual(manifest["hk_evidence"]["grade_color_matches"],1)
        self.assertEqual(manifest["hk_evidence"]["extra_name_mismatches"],0)
        self.assertEqual(
            manifest["hk_evidence"]["grading"]["zero_channels_promoted_to_one"],2)
        self.assertTrue(manifest["hk_evidence"]["runtime_item_ids_preserved"])

    def test_hk_grade_metadata_requires_complete_evidence_pair(self):
        self.source("equip",15001,name="測試盔甲")
        dummy=Path(self.temporary.name)/"item_extra.csv"
        dummy.write_text("",encoding="utf-8")
        with self.assertRaisesRegex(ValueError,"supplied together"):
            compile_item_catalog(
                self.root,source_reference="fixture",hk_item_extra=dummy)

    def test_subset_selected_ids_and_missing_rejected(self):
        self.source("general",2701,name="素材A")
        self.source("weapon",20001,name="素材B")
        value=compile_item_catalog(self.root,source_reference="fixture",
                                   only_ids={2701})
        self.assertEqual([x["item_id"] for x in value["items"]],[2701])
        with self.assertRaisesRegex(ValueError,"no source definition"):
            compile_item_catalog(self.root,source_reference="fixture",
                                 only_ids={3701})

    def test_reject_malformed_sources_without_overwriting_last_good(self):
        good=self.source("general",2701,name="測試石塊")
        out=Path(self.temporary.name)/"item_catalog.json"
        publish_item_catalog(self.root,out,source_reference="fixture")
        before=out.read_bytes()
        good.write_text('[resource]\nid = 2702\nitem_name = "偽造資料"\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError,"file ID differs"):
            publish_item_catalog(self.root,out,source_reference="fixture")
        self.assertEqual(before,out.read_bytes())
        good.write_text('[resource]\nid = 2701\nitem_name = "偽造\\n換行"\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError,"invalid item_name"):
            compile_item_catalog(self.root,source_reference="fixture")
        good.write_bytes(b'\xff\xfe')
        with self.assertRaisesRegex(ValueError,"not UTF-8"):
            compile_item_catalog(self.root,source_reference="fixture")

    def test_duplicate_item_ids_across_folders_rejected(self):
        self.source("general",2701,name="合法")
        self.source("recovery",2701,name="碰撞")
        with self.assertRaisesRegex(ValueError,"ambiguous duplicate"):
            compile_item_catalog(self.root,source_reference="fixture")

    def test_single_cli_subcommand_supports_external_working_directory(self):
        self.source("general",2701,name="小石頭")
        output=Path(self.temporary.name)/"export.json"
        run=subprocess.run([
            sys.executable,str(REPO/"tools"/"asset_converter"/"cli.py"),
            "item-catalog","--items",str(self.root),"--output",str(output),
            "--source-reference","fixture"],cwd=self.temporary.name,
            capture_output=True,text=True)
        self.assertEqual(run.returncode,0,run.stderr+run.stdout)
        self.assertEqual(json.loads(output.read_text(encoding="utf-8"))["item_count"],1)
        self.assertEqual(json.loads(run.stdout)["status"],"pass")


if __name__=="__main__":
    unittest.main()

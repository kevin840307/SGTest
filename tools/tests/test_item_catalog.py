"""Source-derived item catalog contract; no private game data required in CI."""
import hashlib
import json
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


class ItemCatalogTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"Items"

    def source(self,category: str,number: int, *,name: str,
               declared_id=None,icon=421,declared_category=None):
        folder=self.root/category
        folder.mkdir(parents=True,exist_ok=True)
        path=folder/f"{number}.tres"
        path.write_text(
            '[gd_resource type="Resource" format=3]\n'
            '[resource]\n'
            'id = '+str(number if declared_id is None else declared_id)+'\n'
            'item_name = '+json.dumps(name,ensure_ascii=False)+'\n'
            'icon_id = '+str(icon)+'\n'
            'item_type_category = '+json.dumps(
                declared_category or category.capitalize())+'\n',
            encoding="utf-8",
        )
        return path

    def test_utf8_names_source_hash_and_determinism(self):
        source=self.source("general",2701,name="測試石塊")
        self.source("equip",15001,name="測試盔甲",icon=626)
        manifest=compile_item_catalog(self.root,
            source_reference="verified converted client Items")
        self.assertEqual(manifest["item_count"],2)
        self.assertEqual([x["item_id"] for x in manifest["items"]],[2701,15001])
        self.assertEqual(manifest["items"][0]["name"],"測試石塊")
        self.assertEqual(manifest["items"][0]["icon_id"],421)
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

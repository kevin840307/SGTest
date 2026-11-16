"""End-to-end external assets.pck smoke without any playable Client or Server.

By default, builds one synthetic Enemy and one lossless UI atlas. --assets
instead verifies a real published catalog and optional map contracts. Both
paths mount the external PCK in an empty Godot project and load textures via
ResourceLoader. Original media remains private; --output can retain the PCK.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.runtime_pack import build_runtime_pack
from asset_converter.cli import check_runtime
from asset_converter.json_files import write_json


LOAD_SMOKE_GDSCRIPT = """
extends SceneTree

func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if args.is_empty():
        push_error("Expected external PCK path")
        quit(2)
        return
    if not ProjectSettings.load_resource_pack(str(args[0]), true):
        push_error("Cannot mount external PCK")
        quit(3)
        return
    var paths := [
        "res://assets/Enemy/en_smoke/page_0.png",
        "res://assets/ui/account_smoke/page_0.png"
    ]
    for path in paths:
        var texture := ResourceLoader.load(path) as Texture2D
        if texture == null or texture.get_width() != 64 or texture.get_height() != 64:
            push_error("Invalid imported texture from PCK: " + path)
            quit(4)
            return
        print("PCK_RESOURCE_PASS ", path, " ", texture.get_class())
    print("PCK_RUNTIME_SMOKE_PASS")
    quit(0)
"""


REAL_LOAD_GDSCRIPT = """
extends SceneTree

func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if args.size() != 2 or not ProjectSettings.load_resource_pack(args[0], true):
        push_error("Cannot mount external PCK")
        quit(2)
        return
    var expected = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
    var codec_extents := 0
    for path in expected.files:
        if FileAccess.get_sha256(path) != expected.files[path]:
            push_error("PCK file hash mismatch: " + path)
            quit(3)
            return
    for item in expected.textures:
        var texture := ResourceLoader.load(item.path, "Texture2D", ResourceLoader.CACHE_MODE_IGNORE) as Texture2D
        if texture == null or texture.get_width() != int(item.width) or texture.get_height() != int(item.height):
            push_error("Invalid PCK texture: " + item.path)
            quit(4)
            return
        if item.get("block_aligned",false):
            var image := texture.get_image()
            if image == null or image.get_width() != int(item.width) or image.get_height() != int(item.height):
                push_error("PCK physical texture extent differs: " + item.path)
                quit(5)
                return
            # Inspect both stored variants without assuming this device can
            # render ASTC. Metadata/readback extent is not a macOS GPU test.
            var metadata := ConfigFile.new()
            if metadata.load(item.path + ".import") != OK:
                push_error("Missing PCK import metadata: " + item.path)
                quit(6)
                return
            for key in ["path.bptc","path.astc"]:
                if not metadata.has_section_key("remap",key):
                    continue # Lossless UI pages have no codec variants.
                var imported := CompressedTexture2D.new()
                if imported.load(metadata.get_value("remap",key)) != OK:
                    quit(6)
                    return
                var physical := imported.get_image()
                if physical == null or physical.get_width() != int(item.width) or physical.get_height() != int(item.height):
                    push_error("PCK stored codec extent differs: " + item.path + " " + key)
                    quit(7)
                    return
                codec_extents += 1
        print("PCK_RESOURCE_PASS ", item.path)
    print("PCK_CODEC_EXTENTS_PASS ",codec_extents)
    print("PCK_RUNTIME_SMOKE_PASS")
    quit(0)
"""


def real_contract(assets: Path, map_ids: list[int]) -> tuple[dict, dict]:
    """Bind actual source metadata/pages to an independently mounted pack."""
    files, textures, maps = {}, {}, {}
    def include(path):
        relative = path.relative_to(assets).as_posix()
        files['res://assets/'+relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    def texture(path, size, block_aligned=False):
        relative = path.relative_to(assets).as_posix()
        include(path)
        with Image.open(path) as original:
            if list(original.size) != list(size):
                raise ValueError('texture source dimensions differ: '+relative)
        textures[relative] = {'path':'res://assets/'+relative,'width':size[0],'height':size[1]}
        if block_aligned:
            if any(side%4 for side in size):
                raise ValueError('runtime atlas page is not block aligned: '+relative)
            textures[relative]['block_aligned'] = True
    catalog_path = assets/'catalog/assets.json'
    catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
    include(catalog_path)
    manifests = 0
    for category in catalog['categories'].values():
        index_path = assets/category['index']
        include(index_path)
        for entry in json.loads(index_path.read_text(encoding='utf-8'))['assets'].values():
            if entry['kind'] != 'sprite_atlas':
                continue
            manifest_path = assets/entry['file']
            include(manifest_path)
            manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
            manifests += 1
            if len(manifest['pages']) != len(manifest['page_sizes']):
                raise ValueError('atlas page dimension count differs: '+str(manifest_path))
            for name, size in zip(manifest['pages'], manifest['page_sizes']):
                path = manifest_path.parent/name
                if hashlib.sha256(path.read_bytes()).hexdigest() != manifest['page_sha256'][name]:
                    raise ValueError('atlas page hash differs: '+str(path))
                texture(path, size, manifest.get('page_layout')=='height_dense_p0_block4_v1')
            definition = manifest.get('animation_semantics',{}).get('definition')
            if definition:
                include(assets/definition)
    if not textures:
        raise ValueError('catalog has no texture pages')
    for map_id in map_ids:
        maps[map_id] = check_runtime(assets, assets, map_id)
        folder = assets/f'maps/{map_id}'
        for name in ('world.json','population.json','npcs.json','blocking.dat','tiles.bin'):
            include(folder/name)
        with Image.open(folder/'terrain.png') as terrain:
            size = terrain.size
        texture(folder/'terrain.png', size)
    return {'files':files,'textures':list(textures.values())}, {'maps':maps,'atlas_assets':manifests}


def prepare_fixture(source: Path) -> None:
    for directory_name in ("Enemy/en_smoke", "ui/account_smoke"):
        folder = source / directory_name
        folder.mkdir(parents=True)
        image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        painter = ImageDraw.Draw(image)
        painter.rectangle((5, 7, 56, 52), fill=(20, 180, 245, 255))
        painter.rectangle((11, 11, 33, 29), fill=(253, 180, 40, 255))
        image.save(folder / "page_0.png")
        (folder / "atlas.json").write_text(json.dumps({
            "version": 3,
            "packing_policy": "per_asset_height_dense_v1",
            "group": directory_name,
            "pages": ["page_0.png"],
            "frames": [{"page": 0, "rect": [0, 0, 64, 64],
                        "crop": [0, 0, 64, 64], "size": [64, 64],
                        "origin": [0, 0], "source": "smoke.spr",
                        "source_frame": 0}],
        }), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path,
                        help="Godot 4 console executable supporting --headless and --import")
    parser.add_argument('--assets', type=Path, help='test a published real asset catalog instead of synthetic fixtures')
    parser.add_argument('--map', type=int, action='append', help='also validate real map contracts and terrain')
    parser.add_argument('--output', type=Path, help='retain the generated private external PCK here')
    parser.add_argument('--report', type=Path, help='write a persistent smoke result')
    args = parser.parse_args()
    if args.map and (not args.assets or len(set(args.map)) != len(args.map) or any(not 1 <= n <= 65535 for n in args.map)):
        parser.error('map IDs require assets and must be unique integers between 1 and 65535')
    godot = args.godot.resolve()
    if not godot.is_file():
        parser.error(f"Godot executable not found: {godot}")

    with tempfile.TemporaryDirectory(prefix="sgtest-pck-smoke-") as working:
        tmp = Path(working)
        source = args.assets.resolve() if args.assets else tmp / "source_assets"
        metadata = {}
        contract = None
        if args.assets:
            contract, metadata = real_contract(source, sorted(args.map or []))
        else:
            prepare_fixture(source)
        output = args.output.resolve() if args.output else tmp / "assets.pck"
        report = build_runtime_pack(
            godot, source, output, stage=tmp / "stage"
        )
        if not args.assets and (report["vram_atlas_pages"] != 1 or report["lossless_atlas_pages"] != 1):
            raise RuntimeError(f"unexpected compression policy: {report}")
        if not args.assets and (report["bptc"] != 1 or report["astc"] != 1):
            raise RuntimeError(f"missing imported format remaps: {report}")

        empty_client = tmp / "empty_client"
        empty_client.mkdir()
        (empty_client / "project.godot").write_text(
            "config_version=5\n", encoding="utf-8"
        )
        script = tmp / "load_external_pack.gd"
        script.write_text(REAL_LOAD_GDSCRIPT if args.assets else LOAD_SMOKE_GDSCRIPT, encoding="utf-8")
        command = [
            str(godot), "--headless", "--path", str(empty_client),
            "--script", str(script), "--", str(output),
        ]
        if contract is not None:
            contract_path = tmp/'expected.json'
            write_json(contract_path, contract)
            command.append(str(contract_path))
        result = subprocess.run(command, cwd=REPO, capture_output=True,
                                text=True, encoding="utf-8", errors="replace",
                                timeout=120)
        if result.returncode or "PCK_RUNTIME_SMOKE_PASS" not in result.stdout:
            raise RuntimeError(
                f"external PCK ResourceLoader smoke failed (exit={result.returncode})"
                f"\n{result.stdout}\n{result.stderr}"
            )
        expected_count = len(contract['textures']) if contract is not None else 2
        if result.stdout.count("PCK_RESOURCE_PASS") != expected_count:
            raise RuntimeError("Not all PCK resources were loaded")
        print(result.stdout, end="")
        summary = {
            "status": "PASS",
            "source_files": report["source_files"],
            "vram_atlas_pages": report["vram_atlas_pages"],
            "lossless_atlas_pages": report["lossless_atlas_pages"],
            "bptc": report["bptc"],
            "astc": report["astc"],
            "pck_bytes": report["bytes"],
            "tested_with_empty_client": True,
            "temporary_files_deleted_on_exit": True,
            'pck_retained': bool(args.output),
            'pck_sha256': report['sha256'],
            'textures_loaded': expected_count,
            'raw_files_hashed': len(contract['files']) if contract is not None else 0,
            'stored_codec_extents_checked': next((int(line.split()[-1]) for line in result.stdout.splitlines()
                                                 if line.startswith('PCK_CODEC_EXTENTS_PASS ')),0),
            **metadata,
            'scope':'external PCK ResourceLoader; not rendered gameplay or Client/Server E2E',
        }
        if args.report:
            write_json(args.report, summary)
        print(json.dumps(summary, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

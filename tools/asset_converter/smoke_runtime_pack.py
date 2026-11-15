"""End-to-end external assets.pck smoke without any playable Client or Server.

Builds one VRAM-compressed Enemy atlas and one lossless UI atlas, packs both
in an external Godot PCK, then mounts the PCK in a completely empty Godot
project and loads both textures via ResourceLoader. Temporary files are
automatically deleted and original game media is never required.
"""
from __future__ import annotations

import argparse
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
    args = parser.parse_args()
    godot = args.godot.resolve()
    if not godot.is_file():
        parser.error(f"Godot executable not found: {godot}")

    with tempfile.TemporaryDirectory(prefix="sgtest-pck-smoke-") as working:
        tmp = Path(working)
        source = tmp / "source_assets"
        prepare_fixture(source)
        output = tmp / "assets.pck"
        report = build_runtime_pack(
            godot, source, output, stage=tmp / "stage"
        )
        if report["vram_atlas_pages"] != 1 or report["lossless_atlas_pages"] != 1:
            raise RuntimeError(f"unexpected compression policy: {report}")
        if report["bptc"] != 1 or report["astc"] != 1:
            raise RuntimeError(f"missing imported format remaps: {report}")

        empty_client = tmp / "empty_client"
        empty_client.mkdir()
        (empty_client / "project.godot").write_text(
            "config_version=5\n", encoding="utf-8"
        )
        script = tmp / "load_external_pack.gd"
        script.write_text(LOAD_SMOKE_GDSCRIPT, encoding="utf-8")
        command = [
            str(godot), "--headless", "--path", str(empty_client),
            "--script", str(script), "--", str(output),
        ]
        result = subprocess.run(command, cwd=REPO, capture_output=True,
                                text=True, encoding="utf-8", errors="replace",
                                timeout=120)
        if result.returncode or "PCK_RUNTIME_SMOKE_PASS" not in result.stdout:
            raise RuntimeError(
                f"external PCK ResourceLoader smoke failed (exit={result.returncode})"
                f"\n{result.stdout}\n{result.stderr}"
            )
        if result.stdout.count("PCK_RESOURCE_PASS") != 2:
            raise RuntimeError("Not all PCK resources were loaded")
        print(result.stdout, end="")
        print(json.dumps({
            "status": "PASS",
            "source_files": report["source_files"],
            "vram_atlas_pages": report["vram_atlas_pages"],
            "lossless_atlas_pages": report["lossless_atlas_pages"],
            "bptc": report["bptc"],
            "astc": report["astc"],
            "pck_bytes": report["bytes"],
            "tested_with_empty_client": True,
            "temporary_files_deleted_on_exit": True,
        }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

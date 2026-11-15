"""Build the platform-aware external runtime asset PCK.

The source asset tree remains outside the executable. World-rendered atlas pages
use Godot VRAM compression with desktop BPTC/S3TC plus ETC2/ASTC variants.
Pixel-sensitive UI and item icons remain lossless. Logical asset paths and the
pack format stay identical across Windows/macOS; only the selected texture
variant is platform/device dependent.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PACKER = Path(__file__).with_name("runtime_pack_packer.gd")

VRAM_ATLAS_PREFIXES = (
    "enemy/",
    "npcs/",
    "players/",
    "equipment/",
    "pets/",
    "mecha/",
    "effects/",
    "maps/visuals/",
    "items/ground/",
)


def use_vram_compression(relative: Path) -> bool:
    value = relative.as_posix().casefold()
    return any(value.startswith(prefix) for prefix in VRAM_ATLAS_PREFIXES)


PROJECT_TEXT = """config_version=5

[application]
config/name="SGTest Runtime Asset Pack"

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
textures/vram_compression/import_s3tc_bptc=true
textures/vram_compression/import_etc2_astc=true
"""


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def atlas_pages(assets: Path) -> list[Path]:
    pages: set[Path] = set()
    for manifest in assets.rglob("atlas.json"):
        try:
            doc = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise RuntimeError(f"invalid atlas manifest: {manifest}: {exc}") from exc
        values = doc.get("pages", [])
        if not isinstance(values, list):
            raise RuntimeError(f"atlas pages is not an array: {manifest}")
        for value in values:
            name = str(value)
            if not name or Path(name).name != name:
                raise RuntimeError(f"unsafe atlas page name {name!r}: {manifest}")
            page = manifest.parent / name
            if not page.is_file():
                raise RuntimeError(f"atlas page missing: {page}")
            pages.add(page.resolve())
    return sorted(pages)


def copy_assets(source: Path, stage_assets: Path) -> int:
    if stage_assets.parent.exists():
        shutil.rmtree(stage_assets.parent)
    stage_assets.mkdir(parents=True, exist_ok=True)
    count = 0
    for source_file in source.rglob("*"):
        if not source_file.is_file():
            continue
        relative = source_file.relative_to(source)
        if source_file.name == ".gdignore" or source_file.suffix == ".import":
            continue
        target = stage_assets / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source_file, target)
        count += 1
    return count


def run_import(godot: Path, stage: Path) -> None:
    result = subprocess.run(
        [str(godot), "--headless", "--editor", "--path", str(stage), "--import"],
        cwd=REPO,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=1800,
    )
    if result.returncode:
        raise RuntimeError("Godot asset import failed:\\n" + result.stdout[-8000:] + result.stderr[-8000:])


def configure_atlas_imports(stage: Path, stage_assets: Path, relative_pages: list[Path]) -> None:
    for relative in relative_pages:
        import_path = (stage_assets / relative).with_name(relative.name + ".import")
        if not import_path.is_file():
            raise RuntimeError(f"Godot did not create texture import metadata: {import_path}")
        text = import_path.read_text(encoding="utf-8")

        # The first import pass creates lossless/default artifacts. Godot does
        # not consistently consider an edited .import parameter set dirty on a
        # fresh second process, so explicitly invalidate this page's generated
        # files and md5 marker before asking the editor to import again.
        for line in text.splitlines():
            if not line.startswith("dest_files="):
                continue
            for virtual in json.loads(line.removeprefix("dest_files=")):
                artifact = stage / str(virtual).removeprefix("res://")
                artifact.unlink(missing_ok=True)
                artifact.with_suffix(".md5").unlink(missing_ok=True)

        replacements = {
            "compress/mode=0": "compress/mode=2",
            "compress/high_quality=false": "compress/high_quality=true",
            "compress/lossy_quality=0.7": "compress/lossy_quality=1.0",
            "compress/uastc_level=0": "compress/uastc_level=2",
            "compress/hdr_compression=1": "compress/hdr_compression=0",
            "process/fix_alpha_border=true": "process/fix_alpha_border=false",
        }
        for old, new in replacements.items():
            text = text.replace(old, new)
        import_path.write_text(text, encoding="utf-8")


def validate_atlas_imports(
    stage: Path,
    stage_assets: Path,
    all_pages: list[Path],
    vram_pages: list[Path],
) -> dict:
    bptc = astc = 0
    vram_set = set(vram_pages)
    for relative in all_pages:
        import_path = (stage_assets / relative).with_name(relative.name + ".import")
        text = import_path.read_text(encoding="utf-8")
        if relative not in vram_set:
            if "compress/mode=0" not in text or '"vram_texture": true' in text:
                raise RuntimeError(f"lossless atlas page changed import mode: {import_path}")
            continue
        if "compress/mode=2" not in text or '"vram_texture": true' not in text:
            raise RuntimeError(f"atlas page is not VRAM compressed: {import_path}")
        if "path.bptc=" not in text or "path.astc=" not in text:
            raise RuntimeError(f"atlas page lacks Windows/macOS format remaps: {import_path}")
        for line in text.splitlines():
            if line.startswith("dest_files="):
                values = json.loads(line.removeprefix("dest_files="))
                for virtual in values:
                    relative_import = str(virtual).removeprefix("res://")
                    imported = stage / relative_import
                    if not imported.is_file():
                        raise RuntimeError(f"imported texture missing: {virtual}")
                    if str(virtual).endswith(".bptc.ctex"):
                        bptc += 1
                    if str(virtual).endswith(".astc.ctex"):
                        astc += 1
    if bptc != len(vram_pages) or astc != len(vram_pages):
        raise RuntimeError(
            f"compressed variant count mismatch pages={len(vram_pages)} bptc={bptc} astc={astc}"
        )
    return {
        "atlas_pages": len(all_pages),
        "vram_atlas_pages": len(vram_pages),
        "lossless_atlas_pages": len(all_pages) - len(vram_pages),
        "bptc": bptc,
        "astc": astc,
    }


def pack(godot: Path, stage: Path, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [
            str(godot), "--headless", "--path", str(stage),
            "--script", str(PACKER), "--", f"--output={output}",
        ],
        cwd=REPO,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=1800,
    )
    if result.returncode or not output.is_file():
        raise RuntimeError("imported PCK build failed:\\n" + result.stdout[-8000:] + result.stderr[-8000:])


def build_runtime_pack(
    godot: Path,
    source_assets: Path,
    output: Path,
    stage: Path | None = None,
) -> dict:
    godot = godot.resolve()
    source_assets = source_assets.resolve()
    output = output.resolve()
    stage = (stage or REPO / ".cache" / "asset-pack-project").resolve()
    if not source_assets.is_dir():
        raise RuntimeError(f"asset source missing: {source_assets}")
    source_pages = atlas_pages(source_assets)
    relative_pages = [page.relative_to(source_assets) for page in source_pages]
    vram_pages = [relative for relative in relative_pages if use_vram_compression(relative)]

    copied = copy_assets(source_assets, stage / "assets")
    (stage / "project.godot").write_text(PROJECT_TEXT, encoding="utf-8")

    # First pass creates stable .import metadata/UIDs. We then opt only atlas
    # pages into VRAM compression and run a second import pass.
    run_import(godot, stage)
    configure_atlas_imports(stage, stage / "assets", vram_pages)
    run_import(godot, stage)
    imported = validate_atlas_imports(stage, stage / "assets", relative_pages, vram_pages)
    pack(godot, stage, output)

    return {
        "path": str(output),
        "sha256": sha256(output),
        "bytes": output.stat().st_size,
        "source_files": copied,
        **imported,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path)
    parser.add_argument("--assets", type=Path, default=REPO / "client" / "assets")
    parser.add_argument("--output", type=Path, default=REPO / ".cache" / "client-release" / "assets" / "assets.pck")
    parser.add_argument("--stage", type=Path, default=REPO / ".cache" / "asset-pack-project")
    args = parser.parse_args()
    report = build_runtime_pack(args.godot, args.assets, args.output, args.stage)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

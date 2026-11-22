"""Build a synthetic Atlas and verify texture release with the GPU renderer."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
from tempfile import TemporaryDirectory

from PIL import Image


REPO = Path(__file__).resolve().parents[2]


def build_fixture(project: Path) -> None:
    assets = project / "assets"
    catalog = assets / "catalog"
    atlas = assets / "test" / "shared"
    catalog.mkdir(parents=True)
    atlas.mkdir(parents=True)
    (project / "project.godot").write_text("config_version=5\n", encoding="utf-8")
    page = atlas / "page.png"
    Image.new("RGBA", (64, 64), (20, 100, 210, 255)).save(page)
    documents = {
        catalog / "assets.json": {
            "schema": "sgtest.asset_catalog_root/v1",
            "categories": {"test": {"index": "catalog/test.json"}},
        },
        catalog / "test.json": {
            "assets": {"test/shared": {"kind": "sprite_atlas", "file": "test/shared/atlas.json"}},
        },
        atlas / "atlas.json": {
            "version": 9,
            "asset_id": "test/shared",
            "pages": ["page.png"],
            "page_sizes": [[64, 64]],
            "page_sha256": {"page.png": hashlib.sha256(page.read_bytes()).hexdigest()},
            "frames": [{"page": 0, "rect": [0, 0, 64, 64], "origin": [0, 0]}],
            "clips": {"idle": [0]},
        },
    }
    for path, document in documents.items():
        path.write_text(json.dumps(document), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path)
    godot = str(parser.parse_args().godot.resolve())
    cache = REPO / ".cache"
    cache.mkdir(exist_ok=True)
    with TemporaryDirectory(prefix="asset-store-gpu-", dir=cache) as directory:
        project = Path(directory)
        build_fixture(project)
        pack = project / "synthetic.pck"
        fixture_command = [godot, "--headless", "--rendering-method", "gl_compatibility", "--path", directory]
        subprocess.run(fixture_command + ["--editor", "--import"], check=True, timeout=60)
        packer = REPO / "tools" / "asset_converter" / "runtime_pack_packer.gd"
        subprocess.run(fixture_command + ["--script", str(packer), "--", f"--output={pack}"], check=True, timeout=60)
        result = subprocess.run(
            [godot, "--rendering-method", "gl_compatibility", "--path", str(REPO / "client"),
             "--script", "res://tests/test_asset_store_gpu.gd", "--quit-after", "1800", "--", str(pack)],
            timeout=60, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
        )
        print(result.stdout, end="")
        result.check_returncode()
        if "SYNTHETIC_ASSET_SCOPE_PASS cycles=30" not in result.stdout:
            raise SystemExit("GPU test exited before completing all cycles")


if __name__ == "__main__":
    main()

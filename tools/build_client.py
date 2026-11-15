"""Build and verify a Windows release using the shared external assets.pck.

The external asset pack is platform-independent and is also the pack consumed
by macOS builds. Only the application export itself is platform-specific here.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time

REPO = Path(__file__).resolve().parents[1]
TEMPLATE_SHA256 = {
    "windows_release_x86_64.exe": "d34d36f3be1a6c49c56525ae86469b92e4f417ddf0b43cf00dd80c385c4b0562",
    "windows_debug_x86_64.exe": "51498b72b3a237f882ebd7d1787f06a4bc1eaf0572daab93837adcfd3cfdc107",
}


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def build_asset_pack(godot: Path, assets: Path, output: Path) -> dict:
    output.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [
            str(godot), "--headless", "--path", str(REPO / "client"),
            "--script", "res://tools/pack_assets.gd", "--",
            f"--source={assets}", f"--output={output}",
        ],
        cwd=REPO,
        capture_output=True,
        text=True,
        timeout=300,
    )
    if result.returncode or not output.is_file():
        raise RuntimeError("asset pack build failed: " + result.stdout + result.stderr)
    return {
        "path": str(output),
        "sha256": sha256(output),
        "bytes": output.stat().st_size,
        "source_files": sum(1 for path in assets.rglob("*") if path.is_file() and not path.name.startswith(".")),
    }


def verify_package(executable: Path, asset_pack: Path, report_path: Path) -> dict:
    report_path.parent.mkdir(parents=True, exist_ok=True)
    started = time.time()
    checked = subprocess.run(
        [
            str(executable), "--headless", "--", "--verify-pack",
            f"--asset-pack={asset_pack}",
            f"--export-check-report={report_path}",
        ],
        cwd=REPO,
        capture_output=True,
        text=True,
        timeout=30,
    )
    if checked.returncode or not report_path.is_file() or report_path.stat().st_mtime < started:
        raise RuntimeError("export integrity check did not complete: " + checked.stdout + checked.stderr)
    report = json.loads(report_path.read_text(encoding="utf-8"))
    if (
        not report.get("passed")
        or not report.get("template_release")
        or report.get("editor_build")
        or report.get("debug_build")
        or not report.get("external_asset_pack")
    ):
        raise RuntimeError("release external asset-pack integrity failed")
    if report.get("asset_pack_sha256") != sha256(asset_pack):
        raise RuntimeError("runtime mounted asset pack differs from built pack")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path)
    parser.add_argument("--assets", type=Path, default=REPO / "client/assets")
    parser.add_argument("--output", type=Path, default=REPO / ".cache/client-release/SGTest.exe")
    args = parser.parse_args()
    godot, output, assets = args.godot.resolve(), args.output.resolve(), args.assets.resolve()
    version = subprocess.check_output([str(godot), "--version"], text=True).strip()
    if not version.startswith("4.7.2.stable."):
        parser.error("this preset requires official Godot 4.7.2 and matching templates")
    for name, digest in TEMPLATE_SHA256.items():
        path = REPO / ".cache/godot-templates" / name
        if not path.is_file() or sha256(path) != digest:
            parser.error(f"official template missing/mismatched: {path}; see docs/PERF002_MEMORY.md")
    if not (assets / "catalog/assets.json").is_file() and not (assets / "bundles/index.json").is_file():
        parser.error("build runtime assets first; expected catalog/assets.json or bundles/index.json")

    output.parent.mkdir(parents=True, exist_ok=True)
    asset_pack = output.parent / "assets" / "assets.pck"
    pack_report = build_asset_pack(godot, assets, asset_pack)

    subprocess.run(
        [str(godot), "--headless", "--path", str(REPO / "client"), "--editor", "--import"],
        cwd=REPO, check=True, timeout=180,
    )
    subprocess.run(
        [str(godot), "--headless", "--path", str(REPO / "client"),
         "--export-release", "Windows Desktop", str(output)],
        cwd=REPO, check=True, timeout=180,
    )
    report = verify_package(output, asset_pack, output.with_suffix(".integrity.json"))
    print(json.dumps({
        "passed": True,
        "executable": str(output),
        "executable_sha256": sha256(output),
        "main_package_sha256": sha256(output.with_suffix(".pck")),
        "asset_pack": pack_report,
        "runtime_markers": report["markers"],
    }))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

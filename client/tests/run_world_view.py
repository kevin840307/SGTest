"""Native Map13/Map15 viewer smoke: interaction, resource drain, and independent raster checks.

Requires a locally built private original-asset PCK and source; neither is committed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time

from run_asset_store import ProcessMetrics
from run_world import validate_capture

REPO = Path(__file__).resolve().parents[2]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path, help="Godot 4.7.2 desktop executable")
    parser.add_argument("--pack", type=Path, default=REPO / ".cache/client-release/assets/assets.pck")
    parser.add_argument("--assets", type=Path, default=REPO / "client/assets",
                        help="published manifests/terrain matching --pack (read-only raster reference)")
    parser.add_argument("--source", type=Path, default=REPO / ".cache/asset_converter/source")
    parser.add_argument("--report", type=Path, default=REPO / "reports/world_view_native.json")
    parser.add_argument("--timeout", type=int, default=180)
    parser.add_argument("--players", action="store_true", help="native B base paper doll logical view, origin/mirror and source raster gate")
    args = parser.parse_args()
    exe = args.godot.resolve()
    if exe.name.endswith("_console.exe"):
        normal = exe.with_name(exe.name.replace("_console.exe", ".exe"))
        if normal.is_file():
            exe = normal
    pack = args.pack.resolve()
    source = args.source.resolve()
    if not exe.is_file() or not pack.is_file() or not source.is_dir() or args.timeout < 1:
        parser.error("Godot executable, original source and private PCK are required")
    report = args.report.resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    folder = report.with_suffix("")
    folder.mkdir(parents=True, exist_ok=True)

    files = [
        REPO / "client/app/main.gd", REPO / "client/app/main.tscn",
        REPO / "client/project.godot",
        *sorted((REPO / "client/rendering").glob("*.gd")),
        REPO / "client/tests/test_world_view.gd",
        REPO / "client/tests/world_camera_fixture.gd",
        Path(__file__).resolve(),
    ]
    original_hashes = {
        str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files
    }

    command = [
        str(exe), "--path", str(REPO / "client"), "--rendering-method",
        "gl_compatibility", "--disable-vsync", "--script",
        "res://tests/test_world_view.gd", "--", str(pack), str(folder),
    ]
    if args.players:
        command.append("players")
    metrics = ProcessMetrics()
    start = time.monotonic()
    with (folder / "engine.log").open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=REPO, stdout=log, stderr=subprocess.STDOUT)
        while process.poll() is None and time.monotonic() - start < args.timeout:
            metrics.sample(process)
            time.sleep(0.05)
        if process.poll() is None:
            process.kill()
            process.wait()
    log_content = (folder / "engine.log").read_text(encoding="utf-8", errors="replace")
    engine_errors = [
        line for line in log_content.splitlines()
        if any(tag in line for tag in ("ERROR:", "SCRIPT ERROR:", "leaked", "still in use"))
    ]
    data_path = folder / "result.json"
    result = json.loads(data_path.read_text(encoding="utf-8")) if data_path.is_file() else {}
    originals = set()
    checks, validation_errors = [], []
    if process.returncode == 0 and not engine_errors:
        for index, capture in enumerate(result.get("captures", [])):
            try:
                checks.append(validate_capture(args.assets.resolve(), source, capture, originals))
            except Exception as exc:
                validation_errors.append({"capture": index, "message": str(exc)})
    final = result.get("final", {})
    counts_zero = all(
        final.get(key) == 0 for key in
        ("assets", "scopes", "decode_jobs", "texture_jobs", "rgba_bytes")
    )
    stable = original_hashes == {
        str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest() for p in files
    }
    passed = (
        process.returncode == 0 and not engine_errors and result.get("status") == "PASS"
        and not result.get("failures") and len(checks) == (20 if args.players else 12)
        and not validation_errors and all(c["pixels_over_tolerance"] == 0 for c in checks)
        and counts_zero and stable
    )
    summary = {
        "status": "PASS" if passed else "FAIL",
        "scope": "player_source_GPU_not_network_or_multiplayer" if args.players else "map_view_only_not_player_server_or_multiplayer",
        "exit_code": process.returncode,
        "engine_errors": engine_errors,
        "validation_errors": validation_errors,
        "view_failures": result.get("failures", []),
        "captures": len(checks),
        "original_spr_frames_checked": len(originals),
        "max_outside_sampler_envelope": max(
            (x["max_outside_sampler_envelope"] for x in checks), default=None
        ),
        "max_pixels_over_tolerance": max(
            (x["pixels_over_tolerance"] for x in checks), default=None
        ),
        "final_store": final,
        "resources_drained": counts_zero,
        "sources_unchanged": stable,
        "pck_sha256": hashlib.sha256(pack.read_bytes()).hexdigest(),
        "wall_seconds": round(time.monotonic() - start, 3),
        "cpu_seconds": metrics.cpu_seconds,
        "peak_rss_bytes": metrics.peak_rss,
        "source_sha256": original_hashes,
    }
    report.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in summary.items() if k != "source_sha256"}, ensure_ascii=False), flush=True)
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())

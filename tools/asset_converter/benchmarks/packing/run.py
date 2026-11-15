from __future__ import annotations

import json
import statistics
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
GODOT = Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
SCRIPT = Path(__file__).with_name("benchmark.gd")
MAP_ASSETS = ROOT / "client" / "assets"
CANDIDATES = ROOT / ".cache" / "asset_packing_benchmark" / "candidates"
OUTPUT = ROOT / ".cache" / "asset_packing_benchmark" / "runtime"
OUTPUT.mkdir(parents=True, exist_ok=True)

MODES = [
    ("dense_single", CANDIDATES / "dense_single"),
    ("adaptive_dense_shelf", CANDIDATES / "adaptive_dense_shelf"),
    ("adaptive_dense_maxrects", CANDIDATES / "adaptive_dense_maxrects"),
    ("adaptive_atomic", CANDIDATES / "adaptive_atomic"),
]
SCENARIOS = [("two_kinds", 2, 1000), ("twelve_kinds", 12, 1000)]


def run_trial(repeat: int, scenario: str, kinds: int, instances: int, mode: str, root: Path) -> dict:
    target = OUTPUT / f"{mode}_{scenario}_r{repeat}.json"
    command = [
        str(GODOT), "--rendering-method", "gl_compatibility",
        "--script", str(SCRIPT), "--",
        str(root), str(MAP_ASSETS), "1440", "900",
        str(kinds), str(instances), "180", str(target),
    ]
    process = subprocess.run(
        command, cwd=ROOT, capture_output=True, text=True,
        encoding="utf-8", errors="replace", timeout=90,
    )
    if process.returncode != 0 or not target.is_file():
        raise RuntimeError(
            f"{mode}/{scenario}/r{repeat} failed\n{process.stdout[-3000:]}\n{process.stderr[-3000:]}"
        )
    row = json.loads(target.read_text(encoding="utf-8"))
    if not row.get("asset_boundary_verified"):
        raise RuntimeError("benchmark asset-boundary gate failed")
    row.update(mode=mode, scenario_name=scenario, repeat=repeat)
    print(
        f"PASS r{repeat} {scenario:12} {mode:24} "
        f"pages={row['monster_pages']:3} rgba={row['monster_rgba_bytes']/1048576:7.2f}MiB "
        f"load={row['monster_load_ms']:7.1f}ms draw={row['draw_calls_p95']:5.0f} "
        f"p95={row['frame_p95_ms']:.3f}ms",
        flush=True,
    )
    return row


def median(group: list[dict], key: str) -> float:
    return statistics.median(float(row[key]) for row in group)


def main() -> int:
    rows = []
    for repeat in (1, 2, 3):
        order = MODES if repeat != 2 else list(reversed(MODES))
        for scenario, kinds, instances in SCENARIOS:
            for mode, root in order:
                rows.append(run_trial(repeat, scenario, kinds, instances, mode, root))

    metrics = [
        "map_load_ms", "map_pages", "map_rgba_bytes",
        "monster_load_ms", "monster_pages", "monster_rgba_bytes", "monster_png_bytes",
        "frame_p95_ms", "frame_p99_ms", "update_cpu_p95_ms",
        "draw_submit_cpu_mean_ms", "draw_calls_p95",
        "texture_mem_monitor_bytes", "video_mem_monitor_bytes",
    ]
    summary = []
    for scenario, _, instances in SCENARIOS:
        for mode, _ in MODES:
            group = [r for r in rows if r["scenario_name"] == scenario and r["mode"] == mode]
            item = {
                "scenario": scenario,
                "mode": mode,
                "instances": instances,
                "trials": len(group),
                "monster_ids": group[0]["monster_ids"],
                "map_visual_assets": group[0]["map_visual_assets"],
                "map_visible_objects": group[0]["map_visible_objects"],
                "asset_boundary_verified": all(r["asset_boundary_verified"] for r in group),
            }
            for metric in metrics:
                item[metric] = median(group, metric)
            summary.append(item)

    report = {
        "version": 1,
        "rules": {
            "different_assets_never_merged": True,
            "same_monster_ids": True,
            "map_and_monsters_separate": True,
            "one_mode_per_process": True,
            "repeats": 3,
        },
        "rows": summary,
    }
    (OUTPUT / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("\nSUMMARY")
    for row in summary:
        print(
            row["scenario"], row["mode"],
            f"pages={row['monster_pages']:.0f}",
            f"rgba={row['monster_rgba_bytes']/1048576:.2f}MiB",
            f"load={row['monster_load_ms']:.1f}ms",
            f"texture={row['texture_mem_monitor_bytes']/1048576:.2f}MiB",
            f"draw={row['draw_calls_p95']:.0f}",
            f"p95={row['frame_p95_ms']:.3f}ms",
            f"p99={row['frame_p99_ms']:.3f}ms",
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

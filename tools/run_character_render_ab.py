#!/usr/bin/env python3
"""Alternating A/B runner for layered character atlas candidates."""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
from pathlib import Path

METRICS = (
    "load_ms", "frame_p50_ms", "frame_p95_ms", "frame_p99_ms",
    "draw_calls_p50", "draw_calls_p95",
    "video_mem_monitor_bytes", "texture_mem_monitor_bytes",
)


def extract_result(output: str) -> dict:
    for line in reversed(output.splitlines()):
        value = line.strip()
        if not value.startswith("{"):
            continue
        try:
            row = json.loads(value)
        except json.JSONDecodeError:
            continue
        if isinstance(row, dict) and "players" in row and "frame_p95_ms" in row:
            return row
    raise ValueError("Godot output contains no character benchmark JSON")


def summarize(rows: list[dict]) -> dict:
    if not rows:
        raise ValueError("no trials")
    first = rows[0]
    stable = ("players", "races", "pages", "rgba_area_bytes", "png_bytes", "renderer")
    for row in rows[1:]:
        for key in stable:
            if row[key] != first[key]:
                raise ValueError(f"non-stable benchmark field: {key}")
    result = {key: first[key] for key in stable}
    result["trials"] = len(rows)
    for key in METRICS:
        result[key + "_median"] = statistics.median(float(row[key]) for row in rows)
    return result


def run_one(godot: Path, script: Path, candidate: Path,
            width: int, height: int, players: int) -> dict:
    command = [
        str(godot), "--rendering-method", "gl_compatibility",
        "--script", str(script), "--",
        str(candidate), str(width), str(height), str(players),
    ]
    completed = subprocess.run(
        command, capture_output=True, text=True, encoding="utf-8",
        errors="replace", timeout=180, check=False,
    )
    combined = completed.stdout + "\n" + completed.stderr
    if completed.returncode != 0:
        raise RuntimeError(
            f"Godot character benchmark failed ({completed.returncode}):\n"
            + combined[-5000:]
        )
    return extract_result(combined)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path)
    parser.add_argument("candidate_a", type=Path)
    parser.add_argument("candidate_b", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--script", type=Path,
                        default=Path(__file__).with_name("benchmark_character_render.gd"))
    parser.add_argument("--label-a", default="dense")
    parser.add_argument("--label-b", default="state_local")
    parser.add_argument("--width", type=int, default=1440)
    parser.add_argument("--height", type=int, default=900)
    parser.add_argument("--players", type=int, default=20)
    parser.add_argument("--repeats", type=int, default=3)
    args = parser.parse_args()

    for path in (args.godot, args.script):
        if not path.is_file():
            parser.error(f"missing file: {path}")
    for path in (args.candidate_a, args.candidate_b):
        if not path.is_dir():
            parser.error(f"missing candidate dir: {path}")
    if min(args.width, args.height, args.players, args.repeats) <= 0:
        parser.error("numeric arguments must be positive")

    labels = [args.label_a, args.label_b]
    paths = {
        args.label_a: args.candidate_a.resolve(),
        args.label_b: args.candidate_b.resolve(),
    }
    trials = {label: [] for label in labels}
    for repeat in range(args.repeats):
        order = labels if repeat % 2 == 0 else list(reversed(labels))
        for label in order:
            row = run_one(
                args.godot.resolve(), args.script.resolve(), paths[label],
                args.width, args.height, args.players,
            )
            row["repeat"] = repeat + 1
            row["run_order"] = order.index(label) + 1
            trials[label].append(row)
            print(json.dumps({
                "label": label,
                "repeat": repeat + 1,
                "load_ms": row["load_ms"],
                "frame_p95_ms": row["frame_p95_ms"],
                "draw_calls_p95": row["draw_calls_p95"],
                "video_mem": row["video_mem_monitor_bytes"],
            }))

    document = {
        "version": 1,
        "viewport": [args.width, args.height],
        "players": args.players,
        "repeats": args.repeats,
        "core_draw_order_verified": True,
        "animation_timing_verified": False,
        "summaries": {label: summarize(rows) for label, rows in trials.items()},
        "trials": trials,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"summaries": document["summaries"]}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

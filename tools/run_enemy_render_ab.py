#!/usr/bin/env python3
"""Run repeatable GPU/window Enemy atlas A/B tests through Godot.

Trials alternate A/B order to reduce warm-cache/order bias. This benchmark is
visual-only: it does not simulate network, server tick, combat, AOI or DB work.
"""
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
        text = line.strip()
        if not text.startswith("{"):
            continue
        try:
            value = json.loads(text)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and "frame_p95_ms" in value and "renderer" in value:
            return value
    raise ValueError("Godot output did not contain benchmark JSON")


def summarize(trials: list[dict]) -> dict:
    if not trials:
        raise ValueError("no trials")
    first = trials[0]
    stable = ("kinds", "instances", "pages", "rgba_area_bytes", "png_bytes", "renderer")
    for trial in trials[1:]:
        for key in stable:
            if trial[key] != first[key]:
                raise ValueError(f"non-stable benchmark field: {key}")
    result = {key: first[key] for key in stable}
    result["trials"] = len(trials)
    for key in METRICS:
        result[key + "_median"] = statistics.median(float(row[key]) for row in trials)
    return result


def run_one(godot: Path, script: Path, candidate: Path, width: int, height: int,
            kinds: int, instances: int) -> dict:
    command = [
        str(godot), "--rendering-method", "gl_compatibility",
        "--script", str(script), "--", str(candidate), str(width), str(height),
        str(kinds), str(instances),
    ]
    completed = subprocess.run(
        command, capture_output=True, text=True, encoding="utf-8", errors="replace",
        timeout=180, check=False,
    )
    combined = completed.stdout + "\n" + completed.stderr
    if completed.returncode != 0:
        raise RuntimeError(f"Godot benchmark failed ({completed.returncode}):\n{combined[-4000:]}")
    return extract_result(combined)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot", type=Path)
    parser.add_argument("candidate_a", type=Path)
    parser.add_argument("candidate_b", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--script", type=Path,
                        default=Path(__file__).with_name("benchmark_enemy_render.gd"))
    parser.add_argument("--label-a", default="dense")
    parser.add_argument("--label-b", default="source_locality")
    parser.add_argument("--width", type=int, default=1440)
    parser.add_argument("--height", type=int, default=900)
    parser.add_argument("--kinds", type=int, default=8)
    parser.add_argument("--instances", type=int, default=300)
    parser.add_argument("--repeats", type=int, default=3)
    args = parser.parse_args()

    if args.repeats < 1 or args.kinds < 1 or args.instances < 1:
        parser.error("repeats, kinds and instances must be positive")
    for path in (args.godot, args.script):
        if not path.is_file():
            parser.error(f"missing file: {path}")
    for path in (args.candidate_a, args.candidate_b):
        if not path.is_dir():
            parser.error(f"missing candidate directory: {path}")

    trials = {args.label_a: [], args.label_b: []}
    candidates = {
        args.label_a: args.candidate_a.resolve(),
        args.label_b: args.candidate_b.resolve(),
    }
    labels = [args.label_a, args.label_b]
    for repeat in range(args.repeats):
        order = labels if repeat % 2 == 0 else list(reversed(labels))
        for label in order:
            result = run_one(
                args.godot.resolve(), args.script.resolve(), candidates[label],
                args.width, args.height, args.kinds, args.instances,
            )
            result["repeat"] = repeat + 1
            result["run_order"] = order.index(label) + 1
            trials[label].append(result)
            print(json.dumps({
                "label": label, "repeat": repeat + 1,
                "load_ms": result["load_ms"],
                "frame_p95_ms": result["frame_p95_ms"],
                "draw_calls_p95": result["draw_calls_p95"],
                "video_mem": result["video_mem_monitor_bytes"],
            }))

    document = {
        "version": 1,
        "viewport": [args.width, args.height],
        "requested_kinds": args.kinds,
        "instances": args.instances,
        "repeats": args.repeats,
        "candidate_paths_private": True,
        "summaries": {label: summarize(rows) for label, rows in trials.items()},
        "trials": trials,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"summaries": document["summaries"]}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Summarize clean ASSET202 PERF001 runs and choose the ASSET204 base candidate."""
from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path


def percentile(values, fraction):
    values=sorted(float(value) for value in values)
    if not values:
        return None
    return values[min(len(values)-1,int((len(values)-1)*fraction))]


def summarize_candidate(candidate, lifecycle, multiplayer):
    measured=[row for row in lifecycle["records"] if not row["warmup"]]
    flatten=lambda key:[value for row in measured for value in row["loading"][key]]
    loads=[row["loading"]["load_wall_ms"] for row in measured]
    rendered=multiplayer["rendered"]
    return {
        "candidate":candidate["candidate"],
        "pages":candidate["pages"],
        "rgba_bytes":candidate["rgba_bytes"],
        "png_bytes":candidate["png_bytes"],
        "load_ms_p50":statistics.median(loads),
        "load_ms_p95":percentile(loads,.95),
        "decode_ms_p95":percentile(flatten("decode_ms"),.95),
        "upload_submit_ms_p95":percentile(flatten("upload_submit_ms"),.95),
        "gpu_sync_wait_ms_p95":percentile(flatten("gpu_sync_wait_ms"),.95),
        "lifecycle_frame_ms_p95":lifecycle["frame_ms_p95"],
        "lifecycle_frame_ms_p99":lifecycle["frame_ms_p99"],
        "all_texture_handles_released":lifecycle["all_texture_handles_released"],
        "engine_texture_recovery_passed":lifecycle["engine_texture_recovery_passed"],
        "ram_recovery_passed":lifecycle["ram_recovery"]["passed"],
        "rss_peak_bytes":lifecycle["rss_peak_bytes"],
        "multiplayer_frame_ms_p95":rendered["frame_ms_p95"],
        "multiplayer_frame_ms_p99":rendered["frame_ms_p99"],
        "draw_calls_p95":rendered["draw_calls_p95"],
        "texture_memory_bytes":rendered["texture_memory_bytes"],
        "video_memory_bytes":rendered["video_memory_bytes"],
        "multiplayer_execution_passed":multiplayer["execution_passed"],
        "multiplayer_frame_gate_passed":multiplayer["frame_p95_target_passed"] and multiplayer["frame_p99_target_passed"],
        "sync_loader_frame_gate_passed":lifecycle["frame_ms_p99"] <= 40.0,
    }


def choose(rows):
    eligible=[row for row in rows
              if row["all_texture_handles_released"]
              and row["engine_texture_recovery_passed"]
              and row["ram_recovery_passed"]
              and row["multiplayer_execution_passed"]
              and row["multiplayer_frame_gate_passed"]]
    if not eligible:
        raise ValueError("no candidate passes lifecycle recovery and multiplayer gates")
    # Synchronous load frame spikes are an ASSET204 loader problem: if every
    # eligible packing candidate fails that gate, preserve the failure and
    # select the lowest-resource base rather than pretending packing fixed it.
    return min(eligible,key=lambda row:(row["rgba_bytes"],row["video_memory_bytes"],
                                        row["multiplayer_frame_ms_p99"],row["pages"]))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index",type=Path,default=Path("reports/normalized_atlas_candidates/candidate_index.json"))
    parser.add_argument("--runtime",type=Path,default=Path("reports/runtime_asset202_clean"))
    parser.add_argument("--output",type=Path,default=Path("reports/asset202_selection.json"))
    parser.add_argument("--candidate",action="append",
                        default=["normalized_1024","normalized_memory","normalized_balanced"])
    args=parser.parse_args()
    index=json.loads(args.index.read_text(encoding="utf-8"))
    by_name={row["candidate"]:row for row in index["candidates"]}
    rows=[]
    for label in args.candidate:
        root=args.runtime/label
        rows.append(summarize_candidate(by_name[label],
                    json.loads((root/"lifecycle.json").read_text(encoding="utf-8")),
                    json.loads((root/"multiplayer.json").read_text(encoding="utf-8"))))
    selected=choose(rows)
    report={
        "version":1,
        "semantic_source":index["semantic_source"],
        "candidates":rows,
        "selected_for_asset204":selected["candidate"],
        "selection_reason":"minimum RGBA/video-memory base among candidates that pass recovery and multiplayer gates",
        "synchronous_loader_gate_passed":all(row["sync_loader_frame_gate_passed"] for row in rows),
        "synchronous_loader_status":"FAIL: packing alone does not remove load-frame spikes; ASSET204 bounded async store required",
        "selection_is_final_runtime":False,
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"selected_for_asset204":report["selected_for_asset204"],
                      "synchronous_loader_gate_passed":report["synchronous_loader_gate_passed"],
                      "rows":rows},ensure_ascii=False))
    return 0


if __name__=="__main__":
    raise SystemExit(main())

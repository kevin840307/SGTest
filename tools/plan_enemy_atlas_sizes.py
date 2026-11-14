"""Select a reproducible memory-first atlas candidate from Godot measurements."""
import argparse
import json
from pathlib import Path


def make_plan(report):
    if not report.get("headless") or not report.get("groups"):
        raise ValueError("expected nonempty Godot size benchmark")
    groups = {}
    for group in report["groups"]:
        candidates = group["candidates"]
        if {int(row["side"]) for row in candidates} != {1024, 2048, 4096}:
            raise ValueError("each group needs all three sizes")
        # Mixed-scene frame results have not shown a larger-page advantage.
        # Prefer minimum measured RGBA; expose load tradeoffs for review.
        chosen = min(candidates, key=lambda row: (row["rgba_area_bytes"], row["pages"], row["side"]))
        key = "Enemy/" + group["enemy"]
        if key in groups:
            raise ValueError("duplicate group")
        groups[key] = {"side": int(chosen["side"]), "candidates": candidates}
    return {"version": 1, "policy": "minimum_rgba_then_pages_v1",
            "runtime_validated": False, "groups": groups}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("benchmark", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    plan = make_plan(json.loads(args.benchmark.read_text(encoding="utf-8")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"groups": len(plan["groups"]), "policy": plan["policy"]}))


if __name__ == "__main__":
    main()

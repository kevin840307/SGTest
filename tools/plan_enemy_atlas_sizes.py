"""Select a reproducible memory-first atlas candidate from Godot measurements."""
import argparse
import json
from pathlib import Path


def make_plan(report, single_page_overhead=0.10):
    if single_page_overhead < 0:
        raise ValueError("memory overhead must be nonnegative")
    if not report.get("headless") or not report.get("groups"):
        raise ValueError("expected nonempty Godot size benchmark")
    groups = {}
    for group in report["groups"]:
        candidates = group["candidates"]
        if {int(row["side"]) for row in candidates} != {1024, 2048, 4096}:
            raise ValueError("each group needs all three sizes")
        # Mixed-scene frame results have not shown a larger-page advantage.
        # Prefer minimum measured RGBA; expose load tradeoffs for review.
        baseline = min(candidates, key=lambda row: (row["rgba_area_bytes"], row["pages"], row["side"]))
        single = [row for row in candidates if row["pages"] == 1 and
                  row["rgba_area_bytes"] <= baseline["rgba_area_bytes"] * (1 + single_page_overhead)]
        chosen = min(single, key=lambda row: (row["rgba_area_bytes"], row["side"])) if single else baseline
        key = "Enemy/" + group["enemy"]
        if key in groups:
            raise ValueError("duplicate group")
        groups[key] = {"side": int(chosen["side"]), "pages": int(chosen["pages"]),
                       "reason": "single_page_within_budget" if single else "memory_budget_requires_split",
                       "candidates": candidates}
    return {"version": 1, "policy": "prefer_single_page_with_memory_budget_v2",
            "single_page_memory_overhead_limit": single_page_overhead,
            "runtime_validated": False, "groups": groups}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("benchmark", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--single-page-overhead", type=float, default=0.10,
                        help="Allowed RGBA increase versus minimum-memory candidate (default 0.10)")
    args = parser.parse_args()
    plan = make_plan(json.loads(args.benchmark.read_text(encoding="utf-8")), args.single_page_overhead)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"groups": len(plan["groups"]), "policy": plan["policy"]}))


if __name__ == "__main__":
    main()

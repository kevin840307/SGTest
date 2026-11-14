#!/usr/bin/env python3
"""Audit atlas page locality for each enemy action and logical facing.

One facing may reuse the same physical SPR frames as another facing; the
report counts each logical action/facing without duplicating images.
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path


def audit_catalog(catalog: dict) -> dict:
    if catalog.get("quarantined_groups", 0):
        raise ValueError("catalog contains quarantined groups")
    results = []
    total = 0
    split = 0
    by_page_count = Counter()
    for group in catalog["groups"]:
        pages = group["pages"]
        for action in group["actions"]:
            for direction in action["directions"]:
                total += 1
                ids = {frame["page"] for frame in direction["frames"]}
                if not ids or any(not isinstance(i, int) or i < 0 or i >= len(pages) for i in ids):
                    raise ValueError("missing or invalid page reference")
                count = len(ids)
                by_page_count[count] += 1
                if count > 1:
                    split += 1
                    results.append({
                        "enemy": group["enemy"],
                        "action": action["key"],
                        "facing": direction["facing"],
                        "source_direction": direction["source_direction"],
                        "page_count": count,
                        "pages": sorted(ids),
                    })
    return {
        "version": 1,
        "groups": len(catalog["groups"]),
        "action_facings": total,
        "single_page": total - split,
        "split_across_pages": split,
        "page_count_histogram": {str(k): v for k, v in sorted(by_page_count.items())},
        "split_details": results,
        "locality_verified": split == 0,
        "visual_timing_verified": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--require-single-page", action="store_true")
    args = parser.parse_args()
    result = audit_catalog(json.loads(args.catalog.read_text(encoding="utf-8")))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "split_details"}, ensure_ascii=False))
    return 1 if args.require_single_page and not result["locality_verified"] else 0


if __name__ == "__main__":
    raise SystemExit(main())

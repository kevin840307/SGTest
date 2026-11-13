#!/usr/bin/env python3
"""Check whether paths named by original Map*.map manifests exist locally."""
from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path

MAP_KEYS = {"Tiles", "Minimap", "Array", "List", "Blocking", "Zone"}


def references(raw: bytes) -> dict[str, str]:
    result = {}
    for line in raw.decode("big5", "replace").splitlines():
        if "=" not in line:
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        if key in MAP_KEYS:
            result[key] = value.replace("\\", "/")
    return result


def scan(client: Path) -> dict:
    rows = []
    counts = defaultdict(lambda: {"references": 0, "present": 0})
    for path in sorted((client / "Map").glob("Map*.map")):
        for key, relative in references(path.read_bytes()).items():
            present = (client / relative).is_file()
            counts[key]["references"] += 1
            counts[key]["present"] += int(present)
            if not present:
                rows.append({"map": path.name, "key": key, "path": relative})
    return {"manifests": len(list((client / "Map").glob("Map*.map"))),
            "by_key": dict(sorted(counts.items())), "missing": rows}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    args = parser.parse_args()
    result = scan(args.client)
    print(json.dumps({"manifests": result["manifests"], "by_key": result["by_key"],
                      "missing_count": len(result["missing"]),
                      "missing_samples": result["missing"][:20]}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

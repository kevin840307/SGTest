#!/usr/bin/env python3
"""List explicit SPR references in local ITF files; layout fields remain unknown."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

if __package__:
    from .convert_assets import unwrap_cde
else:
    from convert_assets import unwrap_cde

SPR_NAME = re.compile(rb"[A-Za-z0-9_-]+\.spr", re.IGNORECASE)


def references(raw: bytes) -> list[str]:
    if raw.startswith(b"C.D.E."):
        raw = unwrap_cde(raw)
    return sorted({match.decode("ascii") for match in SPR_NAME.findall(raw)}, key=str.casefold)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    args = parser.parse_args()
    source = args.client / "Interface"
    if not source.is_dir():
        parser.error("missing Interface directory")
    present = {p.name.casefold() for p in source.rglob("*.spr")}
    anywhere = {p.name.casefold() for p in args.client.rglob("*.spr")}
    rows = []
    for path in sorted(source.glob("*.itf")):
        names = references(path.read_bytes())
        rows.append({"itf": path.name, "references": len(names),
                     "present_in_interface": sum(name.casefold() in present for name in names),
                     "present_anywhere": sum(name.casefold() in anywhere for name in names),
                     "unmatched_names": [name for name in names if name.casefold() not in anywhere]})
    print(json.dumps({"itf_count": len(rows), "rows": rows}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

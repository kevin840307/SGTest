"""Atomic publication of converter JSON contracts.

Owned solely by the converter. Never write a partial manifest to a live path.
"""
from __future__ import annotations

import json
from pathlib import Path


def write_json(path: Path, data: object) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    temporary.replace(path)

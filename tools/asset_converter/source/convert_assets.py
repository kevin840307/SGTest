#!/usr/bin/env python3
"""Original SPR decoder re-export and ACT/SIF/DrawOrder sidecar normalization.

The production atlas builder is only atlas/dense_atlas.py; there is no
standalone per-file atlas/packing CLI in this module.
"""
from __future__ import annotations

import hashlib
import json
import re
import struct
from pathlib import Path

from .inspect_act import parse_act

# Archive and runtime bundles use the same verified RGB565/indexed SPR decoder.
# Keep public names compatible for all converter consumers.
from .runtime_spr import Frame, decode_spr, unwrap_cde


def convert_act(source: Path, root: Path, dest: Path, force: bool = False) -> str:
    return _convert_sidecar(source, root, dest, ".act", force)


def parse_sif(raw: bytes) -> dict:
    """Preserve the observed map sidecar record without guessing its four values."""
    if raw.startswith(b"C.D.E."):
        raw = unwrap_cde(raw)
    if len(raw) < 2:
        raise ValueError("short SIF")
    name_size = raw[0]
    if not 1 <= name_size <= 127 or len(raw) != 1 + name_size + 1 + 16:
        raise ValueError("invalid SIF record size")
    name = raw[1:1 + name_size].decode("ascii")
    if not name.lower().endswith(".spr"):
        raise ValueError("SIF does not name a SPR")
    flag = raw[1 + name_size]
    values = list(struct.unpack_from("<4i", raw, 2 + name_size))
    return {"spr_name": name, "raw_flag": flag, "raw_values": values,
            "coordinate_semantics": None}


def convert_sif(source: Path, root: Path, dest: Path, force: bool = False) -> str:
    return _convert_sidecar(source, root, dest, ".sif", force)


DRAW_ROW = re.compile(r"^([A-Z0-9]+)\s*:\s*(.+)$")


def parse_draw_order(raw: bytes) -> list[dict]:
    rows = []
    keys = set()
    for number, line in enumerate(raw.decode("ascii").splitlines(), 1):
        if not line.strip():
            continue
        match = DRAW_ROW.fullmatch(line.strip())
        if not match or match[1] in keys:
            raise ValueError(f"invalid or duplicate draw-order row {number}")
        layers = [part.strip() for part in match[2].split(">")]
        if not all(layers):
            raise ValueError(f"empty draw-order layer at row {number}")
        keys.add(match[1])
        rows.append({"key": match[1], "raw_layers": layers})
    return rows


def sidecar_document(source: Path, root: Path, kind: str) -> dict:
    relative = source.relative_to(root)
    raw = source.read_bytes()
    document = {"version":4 if kind==".act" else 1,"source":relative.as_posix(),
                "sha256":hashlib.sha256(raw).hexdigest()}
    if kind==".act":
        document.update({"timing_basis":"legacy_reference_fixed_50hz_default_speed",
                         "legacy_fixed_update_hz":50,"legacy_fixed_update_ms":20,
                         "legacy_default_action_speed":25,"runtime_speed_can_override":True,
                         "action_semantics":None,"actions":parse_act(raw)})
    elif kind==".sif":
        document.update(parse_sif(raw))
    elif kind=="draw_order":
        document.update({"layer_suffix_semantics":None,"rows":parse_draw_order(raw)})
    else:
        raise ValueError("unknown sidecar kind: "+kind)
    return document


def _convert_sidecar(source: Path, root: Path, dest: Path, kind: str, force: bool) -> str:
    relative = source.relative_to(root)
    suffix = ".draw_order.json" if kind=="draw_order" else kind+".json"
    target = dest/relative.parent/(source.stem+suffix)
    document = sidecar_document(source,root,kind)
    if not force:
        try:
            if json.loads(target.read_text(encoding="utf-8")) == document:
                return "cached"
        except (OSError,ValueError,TypeError):
            pass
    target.parent.mkdir(parents=True,exist_ok=True)
    temporary = target.with_name(target.name+".tmp")
    temporary.write_text(json.dumps(document,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
    temporary.replace(target)
    return "converted"


def convert_draw_order(source: Path, root: Path, dest: Path, force: bool = False) -> str:
    return _convert_sidecar(source,root,dest,"draw_order",force)

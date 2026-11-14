#!/usr/bin/env python3
"""Convert an original SGO client SPR tree into small Godot atlases.

This converter does not depend on any recovered game's code or generated media.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

from PIL import Image
if __package__:
    from .inspect_act import parse_act
else:
    from .inspect_act import parse_act

# Archive and runtime bundles use the same verified RGB565/indexed SPR decoder.
# Keep public names compatible for all converter consumers.
from .runtime_spr import Frame, decode_spr, unwrap_cde, MAX_FILE


def pack(frames: list[Frame], max_side: int, padding: int = 2) -> tuple[list[Image.Image], list[dict]]:
    """Deterministic shelf packing with extruded edges for filter-safe regions."""
    pages: list[Image.Image] = []
    records: list[dict] = []
    page = Image.new("RGBA", (max_side, max_side))
    x = y = shelf_h = 0
    used_w = used_h = 0

    def flush() -> None:
        nonlocal page, x, y, shelf_h, used_w, used_h
        if used_w:
            pages.append(page.crop((0, 0, used_w, used_h)))
        page = Image.new("RGBA", (max_side, max_side))
        x = y = shelf_h = used_w = used_h = 0

    for frame in frames:
        bounds = frame.image.getbbox()
        if bounds is None:
            records.append({"page": -1, "rect": [0, 0, 0, 0],
                            "raw_origin": [frame.origin_x, frame.origin_y],
                            "crop": [0, 0, 0, 0],
                            "size": list(frame.image.size)})
            continue
        cropped = frame.image.crop(bounds)
        w, h = cropped.size
        if w + 2 * padding > max_side or h + 2 * padding > max_side:
            raise ValueError(f"frame {w}x{h} exceeds atlas side {max_side}")
        cell_w, cell_h = w + 2 * padding, h + 2 * padding
        if x + cell_w > max_side:
            x, y, shelf_h = 0, y + shelf_h, 0
        if y + cell_h > max_side:
            flush()
        px, py = x + padding, y + padding
        page.paste(cropped, (px, py))
        if padding:
            left = cropped.crop((0, 0, 1, h)).resize((padding, h))
            right = cropped.crop((w - 1, 0, w, h)).resize((padding, h))
            page.paste(left, (x, py))
            page.paste(right, (px + w, py))
            top = page.crop((x, py, x + cell_w, py + 1)).resize((cell_w, padding))
            bottom = page.crop((x, py + h - 1, x + cell_w, py + h)).resize((cell_w, padding))
            page.paste(top, (x, y))
            page.paste(bottom, (x, py + h))
        records.append({"page": len(pages), "rect": [px, py, w, h],
                        "raw_origin": [frame.origin_x, frame.origin_y],
                        "crop": list(bounds),
                        "size": list(frame.image.size)})
        used_w, used_h = max(used_w, x + cell_w), max(used_h, y + cell_h)
        x += cell_w
        shelf_h = max(shelf_h, cell_h)
    flush()
    return pages, records


def convert(source: Path, root: Path, dest: Path, side: int, force: bool = False,
            layout: str = "frames") -> dict:
    relative = source.relative_to(root)
    target = dest / relative.parent / source.stem
    manifest_path = target.with_name(target.name + ".json")
    raw = source.read_bytes()
    if len(raw) > MAX_FILE:
        raise ValueError("source file exceeds safety limit")
    digest = hashlib.sha256(raw).hexdigest()
    if manifest_path.exists() and not force:
        old = json.loads(manifest_path.read_text(encoding="utf-8"))
        if old.get("sha256") == digest and old.get("atlas_side") == side and old.get("layout") == layout and old.get("padding", 0) == (2 if layout == "atlas" else 0) and all(
                (target.parent / p).exists() for p in old.get("pages", [])):
            return {"source": relative.as_posix(), "status": "cached", "frames": len(old["frames"])}
    decoded = decode_spr(raw)
    if layout == "atlas":
        pages, records = pack(decoded, side)
    elif layout == "frames":
        pages = [frame.image for frame in decoded]
        records = [{"page": index, "rect": [0, 0, *frame.image.size],
                    "raw_origin": [frame.origin_x, frame.origin_y],
                    "crop": [0, 0, *frame.image.size], "size": list(frame.image.size)}
                   for index, frame in enumerate(decoded)]
    else:
        raise ValueError(f"unknown layout: {layout}")
    target.parent.mkdir(parents=True, exist_ok=True)
    names = []
    for index, page in enumerate(pages):
        name = f"{source.stem}_{index}.png"
        path = target.parent / name
        temporary = path.with_name(path.name + ".tmp")
        page.save(temporary, format="PNG", optimize=True)
        temporary.replace(path)
        names.append(name)
    document = {"version": 2, "source": relative.as_posix(), "sha256": digest,
                "atlas_side": side, "layout": layout, "padding": 2 if layout == "atlas" else 0,
                "pages": names, "frames": records,
                "animation": None, "facing": None, "frame_duration_ms": None,
                "draw_order": None, "events": None}
    temporary = manifest_path.with_name(manifest_path.name + ".tmp")
    temporary.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temporary.replace(manifest_path)
    return {"source": relative.as_posix(), "status": "converted", "frames": len(records)}


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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path, help="Original clean client directory")
    parser.add_argument("output", type=Path, help="Godot asset output directory")
    parser.add_argument("--atlas-side", type=int, default=1024, choices=(512, 1024, 2048, 4096))
    parser.add_argument("--layout", choices=("frames", "atlas"), default="frames",
                        help="Lossless separate frames (default) or experimental per-SPR atlas")
    parser.add_argument("--category", action="append", help="Convert only these first-level directories")
    parser.add_argument("--limit", type=int, help="Convert first N files, useful for a trial")
    parser.add_argument("--skip-act", action="store_true", help="Only convert SPR, without ACT raw action records")
    parser.add_argument("--skip-sif", action="store_true", help="Do not export map SIF sidecars")
    parser.add_argument("--skip-draw-order", action="store_true", help="Do not export Player draw-order tables")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    root = args.client.resolve()
    out = args.output.resolve()
    if not root.is_dir():
        parser.error(f"client directory does not exist: {root}")
    if out == root or root in out.parents:
        parser.error("output must be outside the client directory")
    categories = set(args.category or [])
    sources = sorted((p for p in root.rglob("*") if p.is_file() and not p.is_symlink() and p.suffix.lower() == ".spr"
                      and (not categories or p.relative_to(root).parts[0] in categories)),
                     key=lambda p: str(p.relative_to(root)).lower())
    if args.limit is not None:
        sources = sources[:args.limit]
    ok = cached = 0
    errors = []
    for source in sources:
        try:
            result = convert(source, root, out, args.atlas_side, args.force, args.layout)
            ok += result["status"] == "converted"
            cached += result["status"] == "cached"
        except (OSError, ValueError, struct.error, zlib.error) as exc:
            errors.append({"source": source.relative_to(root).as_posix(), "error": str(exc)})
    act_ok = act_cached = 0
    act_sources = []
    if not args.skip_act:
        act_sources = sorted((p for p in root.rglob("*") if p.is_file() and not p.is_symlink() and p.suffix.lower() == ".act"
                              and (not categories or p.relative_to(root).parts[0] in categories)),
                             key=lambda p: str(p.relative_to(root)).lower())
        if args.limit is not None:
            act_sources = act_sources[:args.limit]
        for source in act_sources:
            try:
                result = convert_act(source, root, out, args.force)
                act_ok += result == "converted"
                act_cached += result == "cached"
            except (OSError, ValueError, struct.error) as exc:
                errors.append({"source": source.relative_to(root).as_posix(), "error": str(exc)})
    draw_ok = draw_cached = 0
    sif_ok = sif_cached = 0
    sif_sources = []
    if not args.skip_sif:
        sif_sources = sorted((p for p in root.rglob("*") if p.is_file() and not p.is_symlink()
                              and p.suffix.lower() == ".sif"
                              and (not categories or p.relative_to(root).parts[0] in categories)),
                             key=lambda p: str(p.relative_to(root)).lower())
        if args.limit is not None:
            sif_sources = sif_sources[:args.limit]
        for source in sif_sources:
            try:
                result = convert_sif(source, root, out, args.force)
                sif_ok += result == "converted"
                sif_cached += result == "cached"
            except (OSError, ValueError, UnicodeError, struct.error, zlib.error) as exc:
                errors.append({"source": source.relative_to(root).as_posix(), "error": str(exc)})
    draw_sources = []
    if not args.skip_draw_order:
        draw_sources = sorted((p for p in root.rglob("*DrawOrder.dat") if p.is_file() and not p.is_symlink()
                               and (not categories or p.relative_to(root).parts[0] in categories)),
                              key=lambda p: str(p.relative_to(root)).lower())
        if args.limit is not None:
            draw_sources = draw_sources[:args.limit]
        for source in draw_sources:
            try:
                result = convert_draw_order(source, root, out, args.force)
                draw_ok += result == "converted"
                draw_cached += result == "cached"
            except (OSError, ValueError, UnicodeError) as exc:
                errors.append({"source": source.relative_to(root).as_posix(), "error": str(exc)})
    summary = {"scanned": len(sources), "converted": ok, "cached": cached,
                      "act_scanned": len(act_sources), "act_converted": act_ok, "act_cached": act_cached,
                      "sif_scanned": len(sif_sources), "sif_converted": sif_ok, "sif_cached": sif_cached,
                      "draw_order_scanned": len(draw_sources), "draw_order_converted": draw_ok,
                      "draw_order_cached": draw_cached,
                      "failed": len(errors), "errors": errors}
    out.mkdir(parents=True, exist_ok=True)
    (out / "conversion_report.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
                                                  encoding="utf-8")
    print(json.dumps({**summary, "errors": errors[:20]}, ensure_ascii=False, indent=2))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

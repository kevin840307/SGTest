#!/usr/bin/env python3
"""Build equal-content separate-image and atlas candidates for HK2 ground art."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

if __package__:
    from .convert_assets import decode_spr, pack
else:
    from convert_assets import decode_spr, pack


def sample_indices(count: int, sample_count: int) -> list[int]:
    if not 1 <= sample_count <= count:
        raise ValueError("sample count outside source frame range")
    return [index * count // sample_count for index in range(sample_count)]


def build_candidates(source: Path, output: Path, sample_count: int = 25) -> dict:
    raw = source.read_bytes()
    decoded = decode_spr(raw)
    indices = sample_indices(len(decoded), sample_count)
    chosen = [decoded[index] for index in indices]
    results = {}
    for variant in ("individual", "1024", "2048", "4096"):
        target = output / variant
        target.mkdir(parents=True, exist_ok=True)
        if variant == "individual":
            pages = [frame.image for frame in chosen]
            records = [{"page": index, "rect": [0, 0, *frame.image.size],
                        "raw_origin": [frame.origin_x, frame.origin_y],
                        "crop": [0, 0, *frame.image.size], "size": list(frame.image.size)}
                       for index, frame in enumerate(chosen)]
        else:
            pages, records = pack(chosen, int(variant))
        names = []
        for index, page in enumerate(pages):
            name = f"page_{index}.png"
            path = target / name
            temporary = target / (name + ".tmp")
            page.save(temporary, format="PNG", optimize=True)
            temporary.replace(path)
            names.append(name)
        for frame, record in zip(chosen, records):
            page_index = record["page"]
            if page_index < 0:
                if frame.image.getbbox() is not None:
                    raise ValueError("nonempty frame mapped to no page")
                continue
            x, y, w, h = record["rect"]
            actual = pages[page_index].crop((x, y, x + w, y + h))
            expected = frame.image.crop(tuple(record["crop"]))
            if actual.tobytes() != expected.tobytes():
                raise ValueError("candidate page differs from decoded source")
        document = {"version": 1, "source": "Interface/ground.spr",
                    "source_sha256": hashlib.sha256(raw).hexdigest(),
                    "variant": variant, "sample_indices": indices,
                    "pages": names, "frames": records,
                    "item_definition_mapping_verified": False}
        (target / "catalog.json").write_text(
            json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        results[variant] = {"pages": len(pages),
                            "rgba_area_bytes": sum(page.width * page.height * 4 for page in pages),
                            "png_bytes": sum((target / name).stat().st_size for name in names)}
    return {"source_frames": len(decoded), "sample_indices": indices, "variants": results}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--sample-count", type=int, default=25)
    args = parser.parse_args()
    source = (args.client.resolve() / "Interface" / "ground.spr")
    output = args.output.resolve()
    if not source.is_file() or output == args.client.resolve() or args.client.resolve() in output.parents:
        parser.error("ground.spr must exist and output must be outside the clean client")
    print(json.dumps(build_candidates(source, output, args.sample_count), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

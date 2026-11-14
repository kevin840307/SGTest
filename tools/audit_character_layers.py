"""Audit the two base character draw-order tables against reference slot rules."""
import argparse
import hashlib
import json
from pathlib import Path

SLOTS = {"head": [0], "hair": [1], "bonnet": [2], "l-shoulder": [3],
         "r-shoulder": [4], "l-arm+2": [6, 5], "r-arm+2": [8, 7],
         "body+2": [10, 9], "back": [11], "leg+2": [13, 12],
         "sword": [14], "l-gun": [14], "r-gun": [15], "shield": [15]}


def expand_layers(tokens):
    result = []
    for token in tokens:
        key = token.strip().casefold()
        if key not in SLOTS:
            raise ValueError(f"unverified character layer token: {token}")
        result.extend(SLOTS[key])
    return result


def audit_table(path):
    raw = path.read_bytes()
    rows = []
    keys = set()
    for line in raw.decode("ascii").splitlines():
        if not line.strip():
            continue
        key, layers = line.split(":", 1)
        key = key.strip()
        if key in keys:
            raise ValueError("duplicate draw-order key")
        keys.add(key)
        tokens = [part.strip() for part in layers.split(">")]
        rows.append({"key": key, "raw_layers": tokens, "draw_slots": expand_layers(tokens)})
    return {"source": path.name, "sha256": hashlib.sha256(raw).hexdigest(), "rows": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root, output = args.client.resolve(), args.output.resolve()
    if output == root or root in output.parents:
        parser.error("output must be outside the clean client")
    tables = [audit_table(root / "Player" / name)
              for name in ("CharDrawOrder.dat", "R_CharDrawOrder.dat")]
    report = {"version": 1, "reference_slot_expansion_verified": True,
              "visual_alignment_verified": False, "tables": tables}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"tables": len(tables), "rows": sum(len(t["rows"]) for t in tables)}))


if __name__ == "__main__":
    main()

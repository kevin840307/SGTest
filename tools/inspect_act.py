#!/usr/bin/env python3
"""Experimental reader for observed SGO ACT action/frame records.

Only raw keys and integer fields are exposed. Time units and event semantics
are deliberately not inferred here.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path


def parse_act(raw: bytes) -> list[dict]:
    actions = []
    at = 0
    while at < len(raw):
        if raw[at:] == b"\xff":
            break
        if raw[at] != 1 or at + 5 > len(raw):
            raise ValueError(f"unknown ACT action tag at {at}")
        name_size = struct.unpack_from("<I", raw, at + 1)[0]
        at += 5
        if not 2 <= name_size <= 64 or at + name_size > len(raw):
            raise ValueError("bad ACT action key size")
        key_raw = raw[at:at + name_size]
        if key_raw[-1] != 0:
            raise ValueError("unterminated ACT action key")
        key = key_raw[:-1].decode("ascii")
        at += name_size
        if at + 11 > len(raw) or raw[at] != 3 or raw[at + 5] not in (0, 1) or raw[at + 6] != 2:
            raise ValueError(f"unknown ACT frame header at {at}")
        unknown_value = struct.unpack_from("<I", raw, at + 1)[0]
        raw_loop_flag = raw[at + 5]
        count = struct.unpack_from("<I", raw, at + 7)[0]
        at += 11
        if count > 10000 or at + count * 8 > len(raw):
            raise ValueError("bad ACT frame count")
        frames = []
        for _ in range(count):
            index, unknown_time = struct.unpack_from("<II", raw, at)
            frames.append({"index": index, "raw_time": unknown_time})
            at += 8
        events = []
        while at < len(raw) and raw[at] == 4:
            if at + 5 > len(raw):
                raise ValueError("short ACT event header")
            length = struct.unpack_from("<I", raw, at + 1)[0]
            at += 5
            if length < 5 or length > 4096 or at + length > len(raw):
                raise ValueError("bad ACT event length")
            payload = raw[at:at + length]
            at += length
            events.append({"at_frame_raw": struct.unpack_from("<I", payload)[0],
                           "raw_payload_hex": payload[4:].hex()})
        actions.append({"key": key, "raw_value": unknown_value,
                        "raw_loop_flag": raw_loop_flag,
                        "frames": frames, "events": events})
    return actions


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("act", type=Path)
    parser.add_argument("--output", type=Path, help="Private output folder for raw ACT JSON")
    args = parser.parse_args()
    if args.output is not None and (args.output.resolve() == args.act.resolve()
                                    or args.act.resolve() in args.output.resolve().parents):
        parser.error("output must be outside source client")
    if args.act.is_dir():
        paths = sorted(args.act.rglob("*.ACT"))
        failures = []
        keys: set[str] = set()
        for path in paths:
            try:
                raw = path.read_bytes()
                actions = parse_act(raw)
                keys.update(action["key"] for action in actions)
                if args.output is not None:
                    target = args.output / path.relative_to(args.act).parent / (path.stem + ".act.json")
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_text(json.dumps({"version": 1,
                        "source": path.relative_to(args.act).as_posix(),
                        "sha256": hashlib.sha256(raw).hexdigest(),
                        "time_unit": None, "action_semantics": None,
                        "actions": actions}, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
            except (OSError, ValueError, UnicodeError) as exc:
                failures.append({"path": path.relative_to(args.act).as_posix(), "error": str(exc)})
        print(json.dumps({"scanned": len(paths), "passed": len(paths) - len(failures),
                          "action_keys": sorted(keys), "failures": failures[:20]}, ensure_ascii=False, indent=2))
        return 1 if failures else 0
    print(json.dumps(parse_act(args.act.read_bytes()), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

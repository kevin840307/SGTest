#!/usr/bin/env python3
"""Choose repeatable heavy Enemy groups and small visual proxies from clean HK2.

MS visuals are workload proxies, not verified original dropped-item icons.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

if __package__:
    from .build_atlas_groups import select_groups
    from .convert_assets import decode_spr
else:
    from build_atlas_groups import select_groups
    from convert_assets import decode_spr


def choose(root: Path, player_key: str, monster_count: int,
           proxy_count: int) -> tuple[list[str], list[dict]]:
    if monster_count < 1 or proxy_count < 1:
        raise ValueError("counts must be positive")
    enemy = select_groups(root, category="Enemy")
    scored = []
    for key, paths in enemy.items():
        pixels = frames = 0
        for path in paths:
            for frame in decode_spr(path.read_bytes()):
                bounds = frame.image.getbbox()
                if bounds is not None:
                    pixels += (bounds[2] - bounds[0]) * (bounds[3] - bounds[1])
                frames += 1
        scored.append({"key": key.as_posix(), "crop_rect_pixels": pixels,
                       "spr_files": len(paths), "frames": frames})
    scored.sort(key=lambda row: (-row["crop_rect_pixels"], row["key"].casefold()))
    monsters = scored[:monster_count]
    player = select_groups(root, category="Player", keys={player_key.casefold()})
    if len(player) != 1:
        raise ValueError("player group absent from clean source")
    proxies = list(select_groups(root, category="MS"))[:proxy_count]
    if len(monsters) != monster_count or len(proxies) != proxy_count:
        raise ValueError("not enough groups for requested workload")
    keys = [row["key"] for row in monsters] + [player_key] + [key.as_posix() for key in proxies]
    return keys, monsters


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path, help="Group key list, outside the clean client")
    parser.add_argument("--player-group", default="Player/A/Cloth/A-Back07")
    parser.add_argument("--monster-groups", type=int, default=8)
    parser.add_argument("--proxy-groups", type=int, default=25)
    args = parser.parse_args()
    root, output = args.client.resolve(), args.output.resolve()
    if not root.is_dir() or output == root or root in output.parents:
        parser.error("clean source must exist and output must be outside it")
    keys, monsters = choose(root, args.player_group, args.monster_groups, args.proxy_groups)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(keys) + "\n", encoding="utf-8")
    report = {"version": 1, "group_keys": keys, "monster_ranking": monsters,
              "proxy_visual_semantics": None}
    output.with_suffix(".report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                                                  encoding="utf-8")
    print(json.dumps({"groups": len(keys), "monsters": monsters}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

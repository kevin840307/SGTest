#!/usr/bin/env python3
"""Build the local verified character-appearance allowlist from Recovered2 assets.

The output contains only numeric race/hair/color choices and source metadata.
No Recovered2 artwork is copied into the repository.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path

DEFAULT_SOURCE = Path(r"C:\SGO\NewVersion\Recovered2")
RACES = ((0,"B"),(1,"G"),(2,"A"),(3,"R"),(4,"Y"))
COLORS = 8


def count_hair(source: Path, prefix: str) -> int:
    folder = source / "Assets" / "Player" / prefix / "Standard" / "materials"
    if not folder.is_dir():
        return 0
    bases = {
        path.name.split(".",1)[0]
        for path in folder.iterdir()
        if path.is_file() and path.name.startswith(prefix + "-Hair")
    }
    return len(bases)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    boy_count = count_hair(args.source, "B")
    choices: list[dict[str,int]] = []
    counts: dict[int,int] = {}
    for race, prefix in RACES:
        if race == 3:
            hair_count = 1
            color_count = 1
        else:
            hair_count = count_hair(args.source, prefix)
            if hair_count == 0 and prefix in {"G","A","Y"}:
                hair_count = boy_count
            if hair_count == 0:
                hair_count = 1
            if race in {0,1,2}:
                hair_count -= 1
            hair_count = max(hair_count,1)
            color_count = COLORS
        counts[race] = hair_count
        for hair in range(hair_count):
            for color in range(color_count):
                choices.append({"race":race,"hair_index":hair,"hair_color":color})

    payload = {
        "kind":"verified_character_appearance_rules",
        "version":1,
        "rules_verified":True,
        "source_reference":"Recovered2 character_select.gd + local Assets/Player hair material inventory",
        "choices":choices,
    }
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(payload,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
    print(f"CHARACTER_APPEARANCE_RULES_READY choices={len(choices)} hair_counts={counts} out={args.out}")


if __name__ == "__main__":
    main()

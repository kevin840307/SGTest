#!/usr/bin/env python3
"""Build the verified character-appearance allowlist from source evidence.

Hair/race choices follow the recovered presentation assets. Original HK2
interface/CreateCharPara.dat is the authority for independent creation-time
Upper_armor / Lower_armor selectors.
"""
from __future__ import annotations
import argparse
import configparser
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = Path(r"C:\SGO\NewVersion\Recovered2")
DEFAULT_CREATE_CHAR_PARA = ROOT/".cache/asset_converter/source/interface/createcharpara.dat"
RACES = ((0,"B"),(1,"G"),(2,"A"),(3,"R"),(4,"Y"))
COLORS = 8
EXPECTED_UPPER = [570,571,572]
EXPECTED_LOWER = [620,621,622]


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


def read_creation_armor(path: Path) -> tuple[list[int],list[int]]:
    if not path.is_file():
        raise SystemExit(f"MISSING_CREATE_CHAR_PARA {path}")
    parser = configparser.ConfigParser()
    parser.optionxform = str
    parser.read_string(path.read_text(encoding="ascii"))
    if "CreationCharCheck" not in parser:
        raise SystemExit("INVALID_CREATE_CHAR_PARA missing CreationCharCheck")
    section = parser["CreationCharCheck"]
    def values(name: str) -> list[int]:
        try:
            return [int(value.strip()) for value in section[name].split(",") if value.strip()]
        except (KeyError,ValueError) as error:
            raise SystemExit(f"INVALID_CREATE_CHAR_PARA {name}") from error
    upper,lower = values("Upper_armor"),values("Lower_armor")
    if upper != EXPECTED_UPPER or lower != EXPECTED_LOWER:
        raise SystemExit(
            f"UNVERIFIED_CREATE_ARMOR upper={upper} lower={lower}; "
            f"expected={EXPECTED_UPPER}/{EXPECTED_LOWER}")
    return upper,lower


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument("--source",type=Path,default=DEFAULT_SOURCE)
    parser.add_argument("--create-char-para",type=Path,default=DEFAULT_CREATE_CHAR_PARA)
    parser.add_argument("--out",type=Path,required=True)
    args=parser.parse_args()

    upper,lower=read_creation_armor(args.create_char_para)
    boy_count=count_hair(args.source,"B")
    choices:list[dict[str,int]]=[]
    counts:dict[int,int]={}
    for race,prefix in RACES:
        if race==3:
            hair_count=1
            color_count=1
        else:
            hair_count=count_hair(args.source,prefix)
            if hair_count==0 and prefix in {"G","A","Y"}:
                hair_count=boy_count
            if hair_count==0:
                hair_count=1
            if race in {0,1,2}:
                hair_count-=1
            hair_count=max(hair_count,1)
            color_count=COLORS
        counts[race]=hair_count
        # Original HK2 has verified armor assets/creation mappings for B/G/A/R.
        # Yas is derivative-only here, so keep its source-safe base body/leg.
        body_values=range(len(upper)) if race in {0,1,2,3} else range(1)
        leg_values=range(len(lower)) if race in {0,1,2,3} else range(1)
        for hair in range(hair_count):
            for color in range(color_count):
                for body in body_values:
                    for leg in leg_values:
                        choices.append({
                            "race":race,"hair_index":hair,"hair_color":color,
                            "body_index":body,"leg_index":leg})

    payload={
        "kind":"verified_character_appearance_rules",
        "version":1,
        "rules_verified":True,
        "source_reference":(
            "Recovered2 character_select.gd hair/race + original HK2 "
            "interface/CreateCharPara.dat Upper_armor=570,571,572 "
            "Lower_armor=620,621,622"),
        "choices":choices,
    }
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(
        json.dumps(payload,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
    print(
        "CHARACTER_APPEARANCE_RULES_READY "
        f"choices={len(choices)} hair_counts={counts} "
        f"upper={upper} lower={lower} out={args.out}")


if __name__=="__main__":
    main()

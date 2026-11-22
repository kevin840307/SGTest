#!/usr/bin/env python3
"""Build the verified character-appearance allowlist from source evidence.

Hair/race choices follow the recovered presentation assets. Original HK2
interface/CreateCharPara.dat is the authority for independent creation-time
Upper_armor / Lower_armor plus synchronized Left_hand / Right_hand selectors.
"""
from __future__ import annotations
import argparse
import configparser
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = Path(r"C:\SGO\NewVersion\Recovered2")
DEFAULT_CREATE_CHAR_PARA = ROOT/".cache/asset_converter/source/interface/createcharpara.dat"
DEFAULT_RECOVERED_ITEMS = Path(r"C:\SGO\Recovered\client\Items\equip")
RACES = ((0,"B"),(1,"G"),(2,"A"),(3,"R"),(4,"Y"))
COLORS = 8
EXPECTED_UPPER = [570,571,572]
EXPECTED_LOWER = [620,621,622]
EXPECTED_LEFT_HAND = [520,521,522]
EXPECTED_RIGHT_HAND = [1000,1001,1002]
CURRENT_UPPER = [18501,18502,18503]
CURRENT_LOWER = [18535,18536,18537]
CURRENT_LEFT_HAND = [18569,18570,18571]
CURRENT_RIGHT_HAND = [18603,18604,18605]


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


def read_creation_appearance(
        path: Path) -> tuple[list[int],list[int],list[int],list[int]]:
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
    left,right = values("Left_hand"),values("Right_hand")
    if (
        upper != EXPECTED_UPPER or lower != EXPECTED_LOWER or
        left != EXPECTED_LEFT_HAND or right != EXPECTED_RIGHT_HAND
    ):
        raise SystemExit(
            "UNVERIFIED_CREATE_APPEARANCE "
            f"upper={upper} lower={lower} left={left} right={right}")
    return upper,lower,left,right


def verify_recovered_starter_colors(root: Path) -> None:
    expected = [
        (CURRENT_UPPER, 3, ("布製服．1","布製服．2","布製服．3")),
        (CURRENT_LOWER, 4, ("布製下衣．1","布製下衣．2","布製下衣．3")),
        (CURRENT_LEFT_HAND, 1, ("小左護腕","輕左護腕","布左護腕")),
        (CURRENT_RIGHT_HAND, 2, ("小右護腕","輕右護腕","布右護腕")),
    ]
    for item_ids, slot, names in expected:
        for color_index, item_id in enumerate(item_ids):
            path = root / f"{item_id}.tres"
            if not path.is_file():
                raise SystemExit(f"MISSING_RECOVERED_STARTER_ITEM {path}")
            text = path.read_text(encoding="utf-8")
            def int_field(name: str, default: int = 0) -> int:
                match = re.search(rf"(?m)^{re.escape(name)}\s*=\s*(-?\d+)\s*$", text)
                return int(match.group(1)) if match else default
            def str_field(name: str, default: str = "") -> str:
                match = re.search(rf'(?m)^{re.escape(name)}\s*=\s*"([^"]*)"\s*$', text)
                return match.group(1) if match else default
            actual_name = str_field("item_name")
            actual_slot = int_field("equip_slot",-1)
            spr_id = int_field("spr_id",0)
            actual_color = int_field("color_index",0)
            req_race = str_field("req_race")
            if (
                actual_slot != slot or spr_id != 0 or actual_color != color_index or
                req_race != "無" or actual_name != names[color_index]
            ):
                raise SystemExit(
                    "UNVERIFIED_STARTER_COLOR "
                    f"id={item_id} name={actual_name!r} slot={actual_slot} "
                    f"spr_id={spr_id} color_index={actual_color} req_race={req_race!r}")


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument("--source",type=Path,default=DEFAULT_SOURCE)
    parser.add_argument("--create-char-para",type=Path,default=DEFAULT_CREATE_CHAR_PARA)
    parser.add_argument("--recovered-items",type=Path,default=DEFAULT_RECOVERED_ITEMS)
    parser.add_argument("--out",type=Path,required=True)
    args=parser.parse_args()

    upper,lower,left,right=read_creation_appearance(args.create_char_para)
    verify_recovered_starter_colors(args.recovered_items)
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
        # Original CreateCharPara 570..572 / 620..622 map to one Body00 /
        # Leg00 sprite with Recovered color_index 0/1/2. Do not interpret these
        # selectors as different sprite IDs.
        body_values=range(3) if race in {0,1,2} else range(1)
        leg_values=range(3) if race in {0,1,2} else range(1)
        sleeve_values=range(3) if race in {0,1,2,3} else range(1)
        for hair in range(hair_count):
            for color in range(color_count):
                for body in body_values:
                    for leg in leg_values:
                        for sleeve in sleeve_values:
                            choices.append({
                                "race":race,"hair_index":hair,"hair_color":color,
                                "body_index":body,"leg_index":leg,
                                "sleeve_color":sleeve})

    payload={
        "kind":"verified_character_appearance_rules",
        "version":1,
        "rules_verified":True,
        "source_reference":(
            "Recovered2 character_select.gd hair/race + Recovered paper_doll_engine "
            "Standard Body/Leg + Body00/Leg00 overlays; original HK2 CreateCharPara "
            "570/571/572 -> Recovered 18501/18502/18503 spr_id=0 color_index=0/1/2; "
            "620/621/622 -> 18535/18536/18537 spr_id=0 color_index=0/1/2; "
            "520/521/522 + 1000/1001/1002 -> synchronized L/R Arm00 "
            "spr_id=0 color_index=0/1/2"),
        "choices":choices,
    }
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(
        json.dumps(payload,ensure_ascii=False,separators=(",",":")),encoding="utf-8")
    print(
        "CHARACTER_APPEARANCE_RULES_READY "
        f"choices={len(choices)} hair_counts={counts} "
        f"upper={upper} lower={lower} left={left} right={right} out={args.out}")


if __name__=="__main__":
    main()

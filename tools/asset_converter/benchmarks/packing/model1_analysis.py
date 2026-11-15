from __future__ import annotations

import hashlib
import json
import math
import re
import itertools
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[4]
import sys
sys.path.insert(0, str(ROOT / "tools"))

from asset_converter.atlas.build_atlas_groups import pack_atomic_rectangles
from asset_converter.source.runtime_spr import decode_spr

SOURCE = ROOT / ".cache" / "asset_converter" / "source"
ARCHIVE = ROOT / "local-assets" / "asset_converter" / "complete" / "sprites"
OUTPUT = ROOT / ".cache" / "asset_packing_benchmark" / "model1_analysis.json"
PADDING = 2
MAX_SIDE = 4096
CLIP = re.compile(r"^(.+)_([A-Z]{2})([1-8])$", re.I)
COARSE_WIDTHS = (128, 256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096)

SAMPLES = {
    "enemy_small": "Enemy/EN032A",
    "enemy_medium": "Enemy/EN016A",
    "enemy_large": "Enemy/EN081A",
    "enemy_huge": "Enemy/EN078A",
    "weapon_small": "Player/B/Weapon/B-Dagger006",
    "weapon_medium": "Player/B/Weapon/B-Sword024",
    "weapon_large": "Player/B/Weapon/B-Stick003",
    "weapon_huge": "Player/B/Weapon/B-Stick011",
    "equipment_small": "Player/G/Cloth/G-Bonnet92",
    "equipment_medium": "Player/A/Cloth/A-Back23",
    "equipment_large": "Player/R/Cloth/R-Back29",
    "equipment_huge": "Player/R/Cloth/R-Back26",
    "scenery_small": "Map/Object/Water_Object/Water_Object_01",
    "scenery_medium": "Map/Object/Hold_Object/R015_Hole_Object_C_L",
    "scenery_large": "Map/Object/Plant&Tree/A008_Star_River_E_R",
    "scenery_huge": "Map/Object/Hold_Object/A_Bross_Room",
}
FIXED_ENEMIES = [
    "Enemy/EN114A","Enemy/EN089A","Enemy/EN121A","Enemy/EN116A",
    "Enemy/EN023A","Enemy/EN084A","Enemy/EN111A","Enemy/EN125A",
    "Enemy/EN050C","Enemy/EN080A","Enemy/EN093A","Enemy/EN079A",
]
for group in FIXED_ENEMIES:
    SAMPLES.setdefault("mixed_" + group.split("/")[-1].lower(), group)


def _case_child(parent: Path, name: str) -> Path:
    direct = parent / name
    if direct.exists():
        return direct
    folded = name.casefold()
    for child in parent.iterdir():
        if child.name.casefold() == folded:
            return child
    raise FileNotFoundError(parent / name)


def _manifest(group: str) -> Path:
    path = ARCHIVE
    for part in group.split("/"):
        path = _case_child(path, part)
    return path / "atlas.json"


def _source_path(relative: str) -> Path:
    path = SOURCE
    for part in Path(relative).parts:
        path = _case_child(path, part)
    return path


def _clip_info(source: str):
    stem = Path(source).stem
    match = CLIP.fullmatch(stem)
    if match:
        return match.group(2).upper(), match.group(3)
    return "DEFAULT", "default"


def load_asset(group: str) -> dict:
    doc = json.loads(_manifest(group).read_text(encoding="utf-8"))
    frames = []
    for source_row in doc["sources"]:
        relative = source_row["path"]
        action, direction = _clip_info(relative)
        for source_frame, frame in enumerate(decode_spr(_source_path(relative).read_bytes())):
            bounds = frame.image.getbbox()
            if bounds is None:
                continue
            cropped = frame.image.crop(bounds)
            raw = cropped.tobytes()
            mirror_raw = ImageOps.mirror(cropped).tobytes()
            digest = hashlib.sha256(raw).digest()
            mirror_digest = hashlib.sha256(mirror_raw).digest()
            frames.append({
                "source": relative, "source_frame": source_frame,
                "action": action, "direction": direction,
                "w": cropped.width, "h": cropped.height, "area": cropped.width * cropped.height,
                "hash": digest, "mirror_hash": mirror_digest,
            })
    return {"group": group, "frames": frames}


def unique_direction(frames: list[dict]) -> list[dict]:
    seen = set()
    result = []
    for row in frames:
        key = (row["direction"], row["w"], row["h"], row["hash"])
        if key in seen:
            continue
        seen.add(key)
        result.append(row)
    return result


def dedupe_stats(frames: list[dict]) -> dict:
    by_direction = {(r["direction"], r["w"], r["h"], r["hash"]) for r in frames}
    global_exact = {(r["w"], r["h"], r["hash"]) for r in frames}
    global_mirror = {
        (r["w"], r["h"], min(r["hash"], r["mirror_hash"]))
        for r in frames
    }
    area_for = lambda keys: sum(w*h for *_, w, h in [])  # unused helper marker
    direction_area = sum(w*h for _,w,h,_ in by_direction)
    exact_area = sum(w*h for w,h,_ in global_exact)
    mirror_area = sum(w*h for w,h,_ in global_mirror)
    return {
        "raw_frames": len(frames),
        "direction_unique": len(by_direction),
        "global_exact_unique": len(global_exact),
        "global_mirror_unique": len(global_mirror),
        "direction_pixel_area": direction_area,
        "global_exact_pixel_area": exact_area,
        "global_mirror_pixel_area": mirror_area,
        "exact_saving_vs_direction": 1.0 - exact_area / max(1, direction_area),
        "mirror_saving_vs_direction": 1.0 - mirror_area / max(1, direction_area),
    }


def order_key(name: str, row: dict):
    if name == "height":
        return (-row["h"], -row["w"], row["source"], row["source_frame"])
    if name == "width":
        return (-row["w"], -row["h"], row["source"], row["source_frame"])
    if name == "area":
        return (-row["area"], -row["h"], -row["w"], row["source"], row["source_frame"])
    if name == "max_side":
        return (-max(row["w"], row["h"]), -row["area"], row["source"], row["source_frame"])
    if name == "action_local":
        return (row["action"], -row["h"], -row["w"], row["source_frame"])
    if name == "height_action":
        return (-row["h"], row["action"], -row["w"], row["source_frame"])
    if name == "aspect":
        ratio = row["w"] / max(1, row["h"])
        bucket = 0 if ratio >= 1.6 else 1 if ratio >= 0.625 else 2
        return (bucket, -max(row["w"],row["h"]), -row["area"], row["source_frame"])
    raise KeyError(name)


def shelf_layout(rows: list[dict], width: int, order: str):
    ordered = sorted(rows, key=lambda r: order_key(order, r))
    shelves = []  # [used_x,y,height]
    used_w = used_h = 0
    placements = {}
    for index, row in enumerate(ordered):
        w, h = row["w"] + PADDING*2, row["h"] + PADDING*2
        if w > width or h > MAX_SIDE:
            return None
        fits = [
            (shelf[2]-h, width-shelf[0]-w, shelf_id)
            for shelf_id,shelf in enumerate(shelves)
            if shelf[2] >= h and shelf[0]+w <= width
        ]
        if fits:
            _,_,shelf_id = min(fits)
        else:
            y = used_h
            if y+h > MAX_SIDE:
                return None
            shelf_id = len(shelves)
            shelves.append([0,y,h])
            used_h += h
        shelf = shelves[shelf_id]
        placements[id(row)] = (shelf[0], shelf[1], w, h)
        shelf[0] += w
        used_w = max(used_w, shelf[0])
    return {"w":used_w,"h":used_h,"area":used_w*used_h,"placements":placements}


def best_bucket(rows: list[dict], orders: tuple[str,...], fine_width: bool):
    min_width = max(row["w"] + PADDING*2 for row in rows)
    widths = range(max(32, math.ceil(min_width/32)*32), MAX_SIDE+1, 32) if fine_width else COARSE_WIDTHS
    best = None
    for order in orders:
        for width in widths:
            if width < min_width:
                continue
            layout = shelf_layout(rows, width, order)
            if layout is None:
                continue
            key = (layout["area"], max(layout["w"],layout["h"]), layout["w"], layout["h"], order)
            if best is None or key < best["key"]:
                best = {"key":key,"order":order,**layout}
    return best


def pack_blocks_shelf(blocks: list[dict], side: int):
    rows = [{"w":b["w"],"h":b["h"],"area":b["w"]*b["h"],"source":b["direction"],"source_frame":i,
             "action":"BLOCK","direction":"BLOCK"} for i,b in enumerate(blocks)]
    return shelf_layout(rows, side, "height")


def pack_blocks_maxrects(blocks: list[dict], side: int):
    rectangles=[(i,b["w"],b["h"]) for i,b in enumerate(blocks)]
    if any(w>side or h>side for _,w,h in rectangles):
        return None
    placements,bounds=pack_atomic_rectangles(rectangles,side)
    if len(bounds)!=1:
        return None
    w,h=bounds[0]
    return {"w":w,"h":h,"area":w*h}


def model1(frames: list[dict], *, fine_width=False, orders=("height",), block_mode="shelf"):
    unique=unique_direction(frames)
    groups=defaultdict(list)
    for row in unique:
        groups[row["direction"]].append(row)
    blocks=[]
    orders_used={}
    for direction in sorted(groups):
        block=best_bucket(groups[direction],orders,fine_width)
        if block is None:
            return None
        block["direction"]=direction
        blocks.append(block)
        orders_used[direction]=block["order"]
    best=None
    for side in (1024,1536,2048,2560,3072,3584,4096):
        page=(pack_blocks_shelf(blocks,side) if block_mode=="shelf" else pack_blocks_maxrects(blocks,side))
        if page is None or page["w"]>4096 or page["h"]>4096:
            continue
        key=(page["area"],max(page["w"],page["h"]),page["w"],page["h"],side)
        if best is None or key<best["key"]:
            best={"key":key,"page":page,"side":side}
    if best is None:
        return None
    pixel=sum(row["area"] for row in unique)
    return {
        "w":best["page"]["w"],"h":best["page"]["h"],"area":best["page"]["area"],
        "rgba_bytes":best["page"]["area"]*4,
        "unique_frames":len(unique),"pixel_area":pixel,
        "waste":1.0-pixel/max(1,best["page"]["area"]),
        "orders":orders_used,
    }


def bucket_frontier(rows: list[dict], orders=("height","width","area","max_side","height_action","action_local","aspect")):
    min_width=max(row["w"]+PADDING*2 for row in rows)
    layouts={}
    for order in orders:
        for width in range(max(32,math.ceil(min_width/32)*32),MAX_SIDE+1,32):
            layout=shelf_layout(rows,width,order)
            if layout is None:
                continue
            key=(layout["w"],layout["h"])
            candidate={"order":order,**layout}
            current=layouts.get(key)
            if current is None or candidate["area"]<current["area"]:
                layouts[key]=candidate
    values=list(layouts.values())
    frontier=[]
    for candidate in values:
        if any(other is not candidate
               and other["w"]<=candidate["w"] and other["h"]<=candidate["h"]
               and (other["w"]<candidate["w"] or other["h"]<candidate["h"])
               for other in values):
            continue
        frontier.append(candidate)
    # Keep aspect diversity, not merely the smallest local areas.
    frontier.sort(key=lambda r:(r["area"],max(r["w"],r["h"]),r["w"]))
    if len(frontier)<=10:
        return frontier
    chosen=frontier[:4]
    by_ratio=sorted(frontier,key=lambda r:r["w"]/max(1,r["h"]))
    chosen.extend([by_ratio[0],by_ratio[len(by_ratio)//4],by_ratio[len(by_ratio)//2],
                   by_ratio[(len(by_ratio)*3)//4],by_ratio[-1]])
    unique={}
    for row in chosen:
        unique[(row["w"],row["h"])]=row
    return list(unique.values())


def pack_shape_combo(blocks: list[dict]):
    best=None
    for side in (512,768,1024,1280,1536,1792,2048,2304,2560,2816,3072,3328,3584,3840,4096):
        shelf=pack_blocks_shelf(blocks,side)
        if shelf is not None and shelf["w"]<=4096 and shelf["h"]<=4096:
            key=(shelf["area"],max(shelf["w"],shelf["h"]),shelf["w"],shelf["h"],"shelf")
            if best is None or key<best["key"]:
                best={"key":key,"page":shelf,"mode":"shelf"}
        try:
            rect=pack_blocks_maxrects(blocks,side)
        except ValueError:
            rect=None
        if rect is not None and rect["w"]<=4096 and rect["h"]<=4096:
            key=(rect["area"],max(rect["w"],rect["h"]),rect["w"],rect["h"],"maxrects")
            if best is None or key<best["key"]:
                best={"key":key,"page":rect,"mode":"maxrects"}
    return best


def model1_global_shape(frames: list[dict]):
    unique=unique_direction(frames)
    groups=defaultdict(list)
    for row in unique:
        groups[row["direction"]].append(row)
    directions=sorted(groups)
    frontiers={direction:bucket_frontier(groups[direction]) for direction in directions}
    if any(not rows for rows in frontiers.values()):
        return None
    combination_count=1
    for direction in directions:
        combination_count*=len(frontiers[direction])
    best=None
    # Usually 3 stored directions; cap pathological character cases with a beam
    # of locally diverse candidates.
    choices=[frontiers[d] for d in directions]
    if combination_count>50000:
        choices=[sorted(rows,key=lambda r:r["area"])[:6] for rows in choices]
    for combo in itertools.product(*choices):
        blocks=[]
        orders={}
        for direction,shape in zip(directions,combo):
            blocks.append({"w":shape["w"],"h":shape["h"],"direction":direction})
            orders[direction]=shape["order"]
        packed=pack_shape_combo(blocks)
        if packed is None:
            continue
        key=packed["key"]+(sum(shape["area"] for shape in combo),)
        if best is None or key<best["key"]:
            best={"key":key,"packed":packed,"combo":combo,"orders":orders}
    if best is None:
        return None
    pixel=sum(row["area"] for row in unique)
    page=best["packed"]["page"]
    return {
        "w":page["w"],"h":page["h"],"area":page["area"],"rgba_bytes":page["area"]*4,
        "unique_frames":len(unique),"pixel_area":pixel,
        "waste":1.0-pixel/max(1,page["area"]),
        "orders":best["orders"],"block_mode":best["packed"]["mode"],
        "shape_candidates":{d:len(frontiers[d]) for d in directions},
        "combinations":combination_count,
    }


CANDIDATES = {
    "m1_base": dict(fine_width=False,orders=("height",),block_mode="shelf"),
    "m1_fine_width": dict(fine_width=True,orders=("height",),block_mode="shelf"),
    "m1_action_local": dict(fine_width=True,orders=("action_local",),block_mode="shelf"),
    "m1_height_action": dict(fine_width=True,orders=("height_action",),block_mode="shelf"),
    "m1_adaptive_order": dict(fine_width=True,orders=("height","width","area","max_side","height_action","aspect"),block_mode="shelf"),
    "m1_block_maxrects": dict(fine_width=True,orders=("height",),block_mode="maxrects"),
    "m1_adaptive_maxrects": dict(fine_width=True,orders=("height","width","area","max_side","height_action","aspect"),block_mode="maxrects"),
}


def main():
    assets=[]
    for label,group in SAMPLES.items():
        print("decode",label,group,flush=True)
        asset=load_asset(group)
        item={"label":label,"group":group,"dedupe":dedupe_stats(asset["frames"]),"candidates":{}}
        for name,options in CANDIDATES.items():
            item["candidates"][name]=model1(asset["frames"],**options)
        item["candidates"]["m1_global_shape"]=model1_global_shape(asset["frames"])
        assets.append(item)
    report={"version":1,"padding":PADDING,"max_side":MAX_SIDE,"assets":assets}
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    OUTPUT.write_text(json.dumps(report,indent=2,default=lambda o:o.hex() if isinstance(o,bytes) else o),encoding="utf8")
    print("\nRESULTS")
    for item in assets:
        print("\n",item["label"],item["group"],"dedupe exact%",round(item["dedupe"]["exact_saving_vs_direction"]*100,2),
              "mirror%",round(item["dedupe"]["mirror_saving_vs_direction"]*100,2))
        base=item["candidates"]["m1_base"]
        reference_area=base["area"] if base is not None else None
        for name,row in item["candidates"].items():
            if row is None:
                print(name,"NO FIT");continue
            delta=((row["area"]/reference_area-1.0)*100) if reference_area else None
            delta_text=f"delta={delta:+.2f}%" if delta is not None else "delta=n/a"
            print(name,f"{row['w']}x{row['h']}",f"{row['rgba_bytes']/1048576:.2f}MiB",
                  f"waste={row['waste']*100:.2f}%",delta_text)
    return 0

if __name__=="__main__":
    raise SystemExit(main())

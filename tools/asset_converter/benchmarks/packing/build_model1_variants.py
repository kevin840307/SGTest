from __future__ import annotations

import json, shutil, sys
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[4]
sys.path.insert(0,str(ROOT/"tools"))
from asset_converter.atlas.build_atlas_groups import select_groups, pack_atomic_rectangles
from asset_converter.source.runtime_spr import decode_spr

SOURCE=ROOT/".cache/asset_converter/source"
OUTPUT=ROOT/".cache/asset_packing_benchmark/model1_variants"

SAMPLES={
    "enemy_small":"enemy/en032a",
    "enemy_medium":"enemy/en016a",
    "enemy_large":"enemy/en081a",
    "enemy_huge":"enemy/en078a",
    "weapon_small":"player/b/weapon/b-dagger006",
    "weapon_medium":"player/b/weapon/b-sword024",
    "weapon_large":"player/b/weapon/b-stick003",
    "weapon_huge":"player/b/weapon/b-stick011",
    "equipment_small":"player/g/cloth/g-bonnet92",
    "equipment_medium":"player/a/cloth/a-back23",
    "equipment_large":"player/r/cloth/r-back29",
    "equipment_huge":"player/r/cloth/r-back26",
    "scenery_small":"map/object/water_object/water_object_01",
    "scenery_medium":"map/object/hold_object/r015_hole_object_c_l",
    "scenery_large":"map/object/plant&tree/a008_star_river_e_r",
    "scenery_huge":"map/object/hold_object/a_bross_room",
}
FIXED_12=["en114a","en089a","en121a","en116a","en023a","en084a","en111a","en125a","en050c","en080a","en093a","en079a"]
for monster in FIXED_12:
    SAMPLES.setdefault("mixed_"+monster,"enemy/"+monster)

WIDTHS=(128,160,192,224,256,320,384,448,512,640,768,896,1024,1152,1280,1408,1536,1664,1792,1920,2048,2304,2560,2816,3072,3328,3584,3840,4096)
POT_WIDTHS=(128,256,512,1024,2048,4096)
POT_HEIGHTS=(64,128,256,512,1024,2048,4096)

def group_index():
    return {k.as_posix().casefold():(k,v) for k,v in select_groups(SOURCE).items()}

def decode(paths):
    frames=[]; metadata=[]
    for p in paths:
        rel=p.relative_to(SOURCE).as_posix()
        for index,frame in enumerate(decode_spr(p.read_bytes())):
            frames.append(frame); metadata.append((rel,index))
    return frames,metadata

def rows(frames):
    result=[]
    for index,frame in enumerate(frames):
        bbox=frame.image.getbbox()
        if bbox is None: continue
        w,h=bbox[2]-bbox[0],bbox[3]-bbox[1]
        result.append({"id":index,"bbox":bbox,"w":w,"h":h,"area":w*h,"max":max(w,h)})
    return result

def order_height(values):
    return sorted(values,key=lambda r:(-r["h"],-r["w"],r["id"]))

def best_height(frames,padding):
    values=order_height(rows(frames)); best=None
    for width in WIDTHS:
        if any(r["w"]+padding*2>width for r in values): continue
        x=y=shelf_h=0; placements={}; used_w=0
        for r in values:
            cw,ch=r["w"]+padding*2,r["h"]+padding*2
            if x and x+cw>width:
                y+=shelf_h; x=0; shelf_h=0
            placements[r["id"]]=(x,y)
            x+=cw; shelf_h=max(shelf_h,ch); used_w=max(used_w,x)
        used_h=y+shelf_h
        if used_h>4096: continue
        candidate=(used_w*used_h,max(used_w,used_h),used_w,used_h,placements)
        if best is None or candidate[:4]<best[:4]: best=candidate
    if best is None: raise ValueError("height packing failed")
    _,_,w,h,placements=best
    return render(frames,values,w,h,placements,padding)

def align4(value):
    return (value + 3) & ~3

def best_height_bc4(frames, extrude_edges):
    values=order_height(rows(frames)); best=None
    for width in WIDTHS:
        if any(align4(r["w"])>width for r in values):
            continue
        x=y=shelf_h=0; placements={}; used_w=0
        valid=True
        for r in values:
            cell_w=align4(r["w"]); cell_h=align4(r["h"])
            if x and x+cell_w>width:
                y+=shelf_h; x=0; shelf_h=0
            if y+cell_h>4096:
                valid=False
                break
            placements[r["id"]]=(x,y)
            x+=cell_w
            shelf_h=max(shelf_h,cell_h)
            used_w=max(used_w,x)
        if not valid:
            continue
        used_h=y+shelf_h
        candidate=(used_w*used_h,max(used_w,used_h),used_w,used_h,placements)
        if best is None or candidate[:4]<best[:4]:
            best=candidate
    if best is None:
        raise ValueError("BC4-aligned height packing failed")
    _,_,w,h,placements=best
    return render_bc4(frames,values,w,h,placements,extrude_edges)

def render_bc4(frames, values, w, h, placements, extrude_edges):
    page=Image.new("RGBA",(w,h))
    records=[{"page":-1,"rect":[0,0,0,0],"trim":[0,0],"origin":[f.origin_x,f.origin_y],"size":list(f.image.size)} for f in frames]
    for r in values:
        x,y=placements[r["id"]]
        crop=frames[r["id"]].image.crop(r["bbox"])
        page.paste(crop,(x,y))
        cell_w=align4(crop.width); cell_h=align4(crop.height)
        if extrude_edges:
            right=cell_w-crop.width
            bottom=cell_h-crop.height
            if right>0:
                page.paste(crop.crop((crop.width-1,0,crop.width,crop.height)).resize((right,crop.height)),(x+crop.width,y))
            if bottom>0:
                last_row=page.crop((x,y+crop.height-1,x+cell_w,y+crop.height))
                page.paste(last_row.resize((cell_w,bottom)),(x,y+crop.height))
        records[r["id"]].update(page=0,rect=[x,y,crop.width,crop.height],trim=[r["bbox"][0],r["bbox"][1]])
    return page,records

def best_height_pot(frames,padding):
    values=order_height(rows(frames)); best=None
    for width in POT_WIDTHS:
        if any(r["w"]+padding*2>width for r in values):
            continue
        x=y=shelf_h=0; placements={}
        for r in values:
            cw,ch=r["w"]+padding*2,r["h"]+padding*2
            if x and x+cw>width:
                y+=shelf_h; x=0; shelf_h=0
            placements[r["id"]]=(x,y)
            x+=cw; shelf_h=max(shelf_h,ch)
        used_h=y+shelf_h
        height=next((candidate for candidate in POT_HEIGHTS if candidate>=used_h),None)
        if height is None:
            continue
        candidate=(width*height,max(width,height),width,height,placements,used_h)
        if best is None or candidate[:4]<best[:4]:
            best=candidate
    if best is None:
        raise ValueError("power-of-two rectangular height packing failed")
    _,_,w,h,placements,_=best
    return render(frames,values,w,h,placements,padding)

def best_maxrects(frames,padding):
    values=rows(frames); best=None
    for side in (256,384,512,640,768,896,1024,1280,1536,1792,2048,2304,2560,2816,3072,3328,3584,3840,4096):
        rects=[(r["id"],r["w"]+padding*2,r["h"]+padding*2) for r in values]
        if any(w>side or h>side for _,w,h in rects): continue
        try: placements,bounds=pack_atomic_rectangles(rects,side)
        except ValueError: continue
        if len(bounds)!=1: continue
        w,h=bounds[0]
        candidate=(w*h,max(w,h),w,h,placements)
        if best is None or candidate[:4]<best[:4]: best=candidate
    if best is None: raise ValueError("single-page maxrects failed")
    _,_,w,h,raw=best
    placements={idx:(x,y) for idx,(_,x,y) in raw.items()}
    return render(frames,values,w,h,placements,padding)

def extrude(page,crop,x,y,padding):
    w,h=crop.size; px,py=x+padding,y+padding
    page.paste(crop,(px,py))
    if padding<=0:return
    page.paste(crop.crop((0,0,1,h)).resize((padding,h)),(x,py))
    page.paste(crop.crop((w-1,0,w,h)).resize((padding,h)),(px+w,py))
    full_w=w+padding*2
    top=page.crop((x,py,x+full_w,py+1)).resize((full_w,padding))
    bottom=page.crop((x,py+h-1,x+full_w,py+h)).resize((full_w,padding))
    page.paste(top,(x,y));page.paste(bottom,(x,py+h))

def render(frames,values,w,h,placements,padding):
    page=Image.new("RGBA",(w,h))
    records=[{"page":-1,"rect":[0,0,0,0],"trim":[0,0],"origin":[f.origin_x,f.origin_y],"size":list(f.image.size)} for f in frames]
    for r in values:
        x,y=placements[r["id"]]; crop=frames[r["id"]].image.crop(r["bbox"])
        extrude(page,crop,x,y,padding)
        records[r["id"]].update(page=0,rect=[x+padding,y+padding,crop.width,crop.height],trim=[r["bbox"][0],r["bbox"][1]])
    return page,records

def clip_name(relative):
    return Path(relative).stem.upper()

def save(strategy,name,group,page,records,meta):
    folder=OUTPUT/strategy/name;folder.mkdir(parents=True,exist_ok=True)
    page_name="page_0.png";page.save(folder/page_name,format="PNG",compress_level=3)
    clips={}
    for idx,(record,(source,source_frame)) in enumerate(zip(records,meta)):
        record["source"]=source;record["source_frame"]=source_frame
        clips.setdefault(clip_name(source),[]).append(idx)
    doc={"version":3,"group":group,"packing_policy":strategy,"pages":[page_name],
         "page_sizes":[list(page.size)],"frames":records,"clips":clips,"aliases":{},"act":{},"logical_views":{}}
    (folder/"atlas.json").write_text(json.dumps(doc,separators=(",",":")),encoding="utf8")
    return {"strategy":strategy,"name":name,"group":group,"pages":1,"w":page.width,"h":page.height,
            "rgba_bytes":page.width*page.height*4,"png_bytes":(folder/page_name).stat().st_size}

def main():
    if OUTPUT.exists():shutil.rmtree(OUTPUT)
    groups=group_index();report=[]
    for name,lookup in SAMPLES.items():
        key,paths=groups[lookup.casefold()]
        frames,meta=decode(paths)
        for strategy,padding,packer in [
            ("height_p0",0,"height"),
            ("height_p1",1,"height"),
            ("height_p2",2,"height"),
            ("height_bc4_transparent",0,"bc4_transparent"),
            ("height_bc4_extrude",0,"bc4_extrude"),
            ("height_pot_p0",0,"height_pot"),
            ("height_pot_p1",1,"height_pot"),
            ("maxrects_p1",1,"maxrects"),
        ]:
            if packer=="height":
                page,records=best_height(frames,padding)
            elif packer=="bc4_transparent":
                page,records=best_height_bc4(frames,False)
            elif packer=="bc4_extrude":
                page,records=best_height_bc4(frames,True)
            elif packer=="height_pot":
                page,records=best_height_pot(frames,padding)
            else:
                page,records=best_maxrects(frames,padding)
            report.append(save(strategy,name,key.as_posix(),page,records,meta))
        print(name,"done",flush=True)
    out=OUTPUT.parent/"model1_variant_build.json";out.write_text(json.dumps({"samples":SAMPLES,"rows":report},indent=2),encoding="utf8")
    print(out)
if __name__=="__main__":main()

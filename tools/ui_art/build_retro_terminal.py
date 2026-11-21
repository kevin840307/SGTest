#!/usr/bin/env python3
"""Author-owned Retro Terminal UI texture generator (not game asset converter).

Generates small PNG skins from original geometry, with no recovered/private
game texture dependencies. Deterministic output for Git and visual QA.
"""
from __future__ import annotations

import argparse
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

INK = (20, 23, 23, 255)
PANEL = (25, 28, 28, 252)
FIELD = (37, 41, 41, 255)
EDGE = (112, 92, 61, 255)
AMBER = (244, 176, 82, 255)
LIGHT = (236, 230, 215, 255)
FADE = (103, 105, 102, 255)
OUT = Path(__file__).resolve().parents[2] / "client" / "ui" / "skins" / "retro_terminal" / "art"


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    names = [
        r"C:\Windows\Fonts\CascadiaMono.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
    ]
    for name in names:
        if Path(name).exists():
            return ImageFont.truetype(name, size)
    return ImageFont.load_default()


def image(name: str, img: Image.Image) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / name, optimize=True)


def bevel(draw: ImageDraw.ImageDraw, box, border=EDGE, fill=PANEL, step=9, width=1):
    x0, y0, x1, y1 = box
    pts = [(x0 + step, y0), (x1 - step, y0), (x1, y0 + step),
           (x1, y1 - step), (x1 - step, y1), (x0 + step, y1),
           (x0, y1 - step), (x0, y0 + step)]
    draw.polygon(pts, fill=fill)
    draw.line(pts + [pts[0]], fill=border, width=width)
    for tx, ty, sx, sy in [
        (x0+3, y0+step+4, 0, 16), (x1-3, y0+step+4, 0, 16),
        (x0+3, y1-step-4, 0, -16), (x1-3, y1-step-4, 0, -16),
    ]:
        draw.line((tx, ty, tx+sx, ty+sy), fill=AMBER, width=2)


def gradient_face(im: Image.Image, box, top, bottom, step=9, inset=4):
    """Clip a satin-metal gradient inside an existing bevel without erasing it."""
    x0,y0,x1,y1=box
    x0+=inset; y0+=inset; x1-=inset; y1-=inset
    mask=Image.new("L",im.size,0)
    md=ImageDraw.Draw(mask)
    md.polygon([(x0+step,y0),(x1-step,y0),(x1,y0+step),
                (x1,y1-step),(x1-step,y1),(x0+step,y1),
                (x0,y1-step),(x0,y0+step)],fill=255)
    layer=Image.new("RGBA",im.size)
    d=ImageDraw.Draw(layer)
    height=max(1,y1-y0)
    for y in range(y0,y1+1):
        t=(y-y0)/height
        # Glossy lip at the top, broad soft falloff, reflective bottom rim.
        sheen=15*math.exp(-((t-0.12)/0.075)**2)+4*math.exp(-((t-0.84)/0.17)**2)
        c=tuple(max(0,min(255,int(top[k]*(1-t)+bottom[k]*t+sheen))) for k in range(3))
        d.line((x0,y,x1,y),fill=c+(255,),width=1)
    im.paste(layer,(0,0),mask)


def create_panel() -> None:
    im = Image.new("RGBA", (112,112))
    d=ImageDraw.Draw(im)
    bevel(d, (2,2,109,109), fill=PANEL, step=12, width=2)
    gradient_face(im,(2,2,109,109),(53,57,55,255),(15,19,20,255),step=10,inset=3)
    d.line((20,13,91,13),fill=(129,115,82,255),width=1)
    d.line((20,96,91,96),fill=(14,16,16,255),width=2)
    d.rectangle((14,7,98,9), fill=(178,118,52,255))
    d.line((14,101,98,101),fill=(77,68,52,255),width=1)
    d.rectangle((11,20,13,93),fill=(50,49,43,255))
    d.rectangle((99,20,101,93),fill=(50,49,43,255))
    image("window_panel.png",im)


def create_button(name: str, fill, border, bright=False, disabled=False) -> None:
    im=Image.new("RGBA",(112,48))
    d=ImageDraw.Draw(im)
    bevel(d,(1,1,110,46),fill=fill,border=border,step=8,width=2 if bright else 1)
    if not disabled:
        top=(108,87,53,255) if bright else (65,66,61,255)
        bottom=(39,38,34,255) if bright else (31,34,33,255)
        gradient_face(im,(1,1,110,46),top,bottom,step=7,inset=3)
    else:
        gradient_face(im,(1,1,110,46),(43,45,42,255),(25,26,26,255),step=7,inset=3)
    d.line((12,6,100,6),fill=AMBER if bright else EDGE,width=1)
    d.line((12,40,100,40),fill=(62,59,48,255))
    if bright:
        d.rectangle((12,36,36,38),fill=AMBER)
        d.rectangle((42,36,60,38),fill=(173,121,57,255))
    if disabled:
        d.rectangle((12,6,100,7),fill=(57,58,56,255))
    image(name,im)


def create_field() -> None:
    im=Image.new("RGBA",(112,48))
    d=ImageDraw.Draw(im)
    bevel(d,(1,2,110,45),fill=FIELD,border=(80,72,59,255),step=5)
    gradient_face(im,(1,2,110,45),(18,22,22,255),(48,50,46,255),step=4,inset=3)
    d.line((12,8,99,8),fill=(12,14,14,255),width=2)
    d.rectangle((6,10,8,38),fill=(180,125,55,255))
    d.line((16,42,98,42),fill=(79,78,69,255))
    image("input_field.png",im)


def create_bar() -> None:
    for name, fill in [("bar_track.png",(36,39,37,255)),("bar_fill.png",AMBER)]:
        im=Image.new("RGBA",(80,24))
        d=ImageDraw.Draw(im)
        bevel(d,(1,1,78,22),fill=fill,border=(89,86,67,255),step=4)
        if name=="bar_fill.png":
            gradient_face(im,(1,1,78,22),(255,224,155,255),(180,96,34,255),step=3,inset=2)
        else:
            gradient_face(im,(1,1,78,22),(30,34,32,255),(13,15,15,255),step=3,inset=2)
        if name=="bar_fill.png":
            for x in range(8,80,10):
                d.line((x,5,x-7,19),fill=(250,201,121,160),width=1)
        image(name,im)


def create_brand() -> None:
    im=Image.new("RGBA",(160,160))
    d=ImageDraw.Draw(im)
    d.rounded_rectangle((13,13,146,146),radius=21,outline=AMBER,width=4,fill=(24,27,27,230))
    d.rectangle((32,37,128,121),outline=(170,116,52,255),width=2)
    for x,y in [(13,32),(13,127),(146,32),(146,127)]:
        d.ellipse((x-3,y-3,x+3,y+3),fill=AMBER)
    d.line([(50,59),(67,77),(50,95)],fill=AMBER,width=7,joint="curve")
    d.line((78,102,113,102),fill=AMBER,width=7)
    d.rectangle((40,130,120,133),fill=(76,74,60,255))
    image("terminal_mark.png",im)


def create_avatar() -> None:
    im=Image.new("RGBA",(280,300))
    d=ImageDraw.Draw(im)
    d.rectangle((1,1,278,298),outline=(118,91,57,255),width=2,fill=(25,28,28,250))
    for y in range(26,300,22):
        d.line((6,y,272,y),fill=(44,45,41,255))
    for x in range(16,280,22):
        d.line((x,6,x,294),fill=(43,45,40,255))
    d.ellipse((62,24,218,180),outline=(133,102,59,255),width=2)
    d.ellipse((100,76,180,164),fill=(82,79,69,255),outline=AMBER,width=2)
    d.rounded_rectangle((64,174,216,253),radius=27,fill=(68,69,62,255),outline=AMBER,width=2)
    d.polygon([(98,172),(183,172),(202,218),(84,218)],fill=(44,46,43,255))
    d.line((140,21,140,275),fill=(91,87,64,255))
    d.line((21,148,260,148),fill=(76,78,59,255))
    d.rectangle((15,15,48,18),fill=AMBER)
    d.rectangle((232,280,266,283),fill=AMBER)
    d.text((16,268),"ID::NO PREVIEW",font=font(14),fill=AMBER)
    image("identity_scan.png",im)


def create_divider() -> None:
    im=Image.new("RGBA",(512,12))
    d=ImageDraw.Draw(im)
    d.line((0,4,512,4),fill=EDGE,width=1)
    d.line((0,5,512,5),fill=(41,44,42,255),width=2)
    for x in [0,12,20,475,487,496]:
        d.rectangle((x,2,x+5,8),fill=AMBER)
    image("divider.png",im)


def create_background() -> None:
    w,h=1280,720
    im=Image.new("RGB",(w,h),(17,19,20))
    d=ImageDraw.Draw(im)
    r=random.Random(1418)
    for y in range(h):
        mid=(36,38,37) if y<125 else (18,21,22)
        sc=0.05*math.sin(y*0.14) + 0.035*math.sin(y*0.008)
        d.line((0,y,w,y), fill=tuple(max(0,min(255,int(c*(1+sc)))) for c in mid))
    for x in range(0,w,40):
        d.line((x,0,x,h),fill=(37,38,35),width=1)
    for y in range(0,h,40):
        d.line((0,y,w,y),fill=(33,35,33),width=1)
    for y in range(2,h,4):
        d.line((0,y,w,y),fill=(18,20,20),width=1)
    for x in range(0,w,160):
        d.line((x,0,x,16),fill=(121,92,46),width=2)
        d.line((x,h-16,x,h),fill=(121,92,46),width=2)
    for x in (40,w-40):
        d.line((x,36,x,h-36),fill=(90,73,46),width=1)
    d.line((40,48,w-40,48),fill=(179,119,51),width=2)
    d.line((40,h-48,w-40,h-48),fill=(179,119,51),width=2)
    for x in (66,w-66):
        for y in (75,h-75):
            d.rectangle((x-4,y-4,x+4,y+4),fill=(163,106,47))
    for i in range(84):
        x=r.randint(80,w-80)
        y=r.randint(100,h-100)
        c=r.randint(38,72)
        d.point((x,y),fill=(c,c-4,c-9))
    for cx in (145,1135):
        for rad in (65,95,128):
            d.arc((cx-rad,360-rad,cx+rad,360+rad),40,285,fill=(82,65,41),width=1)
        for rad in (28,34):
            d.ellipse((cx-rad,360-rad,cx+rad,360+rad),outline=(142,99,44),width=1)
        d.line((cx-142,360,cx+142,360),fill=(80,67,46),width=1)
        d.line((cx,210,cx,510),fill=(80,67,46),width=1)
    d.text((70,72),"SGTEST // LOCAL TERMINAL",font=font(17),fill=(183,135,65))
    d.text((70,97),"GATEWAY   /   CHARACTER AUTHORITY   /   WORLD TELEMETRY",font=font(11),fill=(116,116,104))
    d.text((70,h-97),"SYSTEM   :  RETRO-01         STATUS  :  LOCAL INTERFACE",font=font(12),fill=(180,141,84))
    d.text((995,h-96),"SGTEST / 2026",font=font(12),fill=(160,128,80))
    image("terminal_backdrop.png",im.convert("RGBA"))


def main() -> None:
    argparse.ArgumentParser(description=__doc__).parse_args()
    create_panel()
    create_button("button_normal.png",(44,47,44,255),(110,85,53,255))
    create_button("button_hover.png",(67,56,40,255),AMBER,True)
    create_button("button_pressed.png",(88,65,36,255),AMBER,True)
    create_button("button_disabled.png",(33,35,34,255),(63,64,60,255),disabled=True)
    create_field()
    create_bar()
    create_brand()
    create_avatar()
    create_divider()
    create_background()
    for path in sorted(OUT.glob("*.png")):
        print(f"RETRO_ART {path.name} {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()

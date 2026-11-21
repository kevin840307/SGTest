#!/usr/bin/env python3
"""Create shared SGTest cursor icons for all UI skins (original pixel art)."""
from pathlib import Path
from PIL import Image, ImageDraw

TARGET = Path(__file__).resolve().parents[2] / "client" / "ui" / "cursors"
S = 3
SIZE = 42
IVORY = (244, 233, 210, 255)
GOLD = (249, 177, 75, 255)
PALE = (255, 212, 132, 255)
SHADOW = (10, 12, 12, 255)
GRAY = (125, 143, 147, 255)


def render(name, draw_fn):
    im = Image.new("RGBA", (SIZE*S, SIZE*S))
    dr = ImageDraw.Draw(im)
    def poly(points, fill, outline=None, width=1):
        dr.polygon([(int(x*S), int(y*S)) for x,y in points], fill=fill)
        if outline is not None:
            dr.line([(int(x*S), int(y*S)) for x,y in points+[points[0]]],
                    fill=outline, width=width*S, joint="curve")
    def line(points, fill, width=1):
        dr.line([(int(x*S), int(y*S)) for x,y in points], fill=fill, width=width*S, joint="curve")
    def ellipse(box, fill=None, outline=None, width=1):
        dr.ellipse(tuple(int(v*S) for v in box), fill=fill, outline=outline, width=width*S)
    draw_fn(poly, line, ellipse)
    TARGET.mkdir(parents=True, exist_ok=True)
    im.resize((SIZE,SIZE),Image.Resampling.LANCZOS).save(TARGET/(name+".png"),optimize=True)


def arrow(poly,line,ellipse):
    pts=[(3,3),(5,34),(13,26),(19,39),(25,37),(18,23),(31,22)]
    poly(pts,IVORY,SHADOW,2)
    line([(7,10),(9,28),(14,22),(24,21)],GOLD,2)
    line([(20,29),(23,35)],PALE,1)


def point(poly,line,ellipse):
    # Raised index-finger hand; click hotspot is the fingertip at x=17,y=3.
    poly([(15,2),(19,2),(21,6),(21,17),(26,14),(29,16),(32,18),
          (36,21),(35,30),(30,38),(17,39),(10,33),(7,27),(9,24),
          (12,23),(15,27)],IVORY,SHADOW,2)
    line([(17,6),(17,28),(12,25)],GOLD,2)
    line([(23,21),(29,20),(32,24)],GRAY,1)


def drag(poly,line,ellipse):
    poly([(8,13),(13,11),(16,13),(16,8),(21,7),(23,12),
          (24,8),(29,9),(31,15),(33,13),(38,16),(37,27),
          (31,36),(17,39),(8,34),(4,22)],IVORY,SHADOW,2)
    for x in (17,24,31):
        line([(x,14),(x,22)],GOLD,1)


def cross(poly,line,ellipse):
    ellipse((4,4,38,38),outline=SHADOW,width=4)
    ellipse((4,4,38,38),outline=GOLD,width=2)
    ellipse((14,14,28,28),outline=IVORY,width=2)
    line([(21,0),(21,14)],GOLD,3)
    line([(21,28),(21,42)],GOLD,3)
    line([(0,21),(14,21)],GOLD,3)
    line([(28,21),(42,21)],GOLD,3)


def move(poly,line,ellipse):
    ellipse((6,6,36,36),outline=SHADOW,width=4)
    ellipse((6,6,36,36),outline=GOLD,width=2)
    poly([(21,1),(14,14),(28,14)],PALE,SHADOW,1)
    poly([(21,41),(14,28),(28,28)],PALE,SHADOW,1)
    poly([(1,21),(14,14),(14,28)],PALE,SHADOW,1)
    poly([(41,21),(28,14),(28,28)],PALE,SHADOW,1)
    ellipse((18,18,24,24),fill=GOLD,outline=SHADOW,width=1)


def inspect(poly,line,ellipse):
    ellipse((5,5,27,27),fill=(41,50,47,255),outline=SHADOW,width=4)
    ellipse((5,5,27,27),outline=IVORY,width=2)
    line([(24,25),(35,36)],SHADOW,6)
    line([(24,25),(35,36)],GOLD,3)
    line([(10,15),(14,19),(22,11)],GOLD,2)


def wait(poly,line,ellipse):
    poly([(9,5),(33,5),(30,10),(30,13),(23,21),(30,29),
          (30,33),(33,37),(9,37),(12,33),(12,29),(19,21),
          (12,13),(12,10)],(40,45,44,255),GOLD,2)
    line([(15,8),(27,8)],IVORY,2)
    line([(17,31),(25,31)],IVORY,2)
    line([(19,22),(23,22)],GOLD,3)


def text(poly,line,ellipse):
    line([(15,5),(27,5)],SHADOW,4)
    line([(21,5),(21,36)],SHADOW,5)
    line([(15,36),(27,36)],SHADOW,4)
    line([(15,5),(27,5)],IVORY,2)
    line([(21,5),(21,36)],GOLD,2)
    line([(15,36),(27,36)],IVORY,2)


def forbidden(poly,line,ellipse):
    ellipse((6,6,36,36),fill=(45,40,38,255),outline=SHADOW,width=4)
    ellipse((6,6,36,36),outline=GOLD,width=3)
    line([(10,32),(32,10)],GOLD,4)


def main():
    for name,renderer in [
        ("arrow",arrow),("point",point),("drag",drag),
        ("attack",cross),("move",move),("inspect",inspect),
        ("wait",wait),("text",text),("forbidden",forbidden),
    ]:
        render(name,renderer)
        print("SHARED_CURSOR",name,(TARGET/(name+".png")).stat().st_size)

if __name__ == "__main__":
    main()

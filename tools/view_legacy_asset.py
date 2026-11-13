#!/usr/bin/env python3
"""Inspect one clean-client SPR or converted manifest without importing a game project.

The playback interval is a manual visual aid, not the original ACT clock.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageTk

if __package__:
    from .convert_assets import Frame, decode_spr
else:
    from convert_assets import Frame, decode_spr


def read_frames(path: Path) -> tuple[list[Frame], str]:
    if path.suffix.lower() == ".spr":
        return decode_spr(path.read_bytes()), path.name
    if path.suffix.lower() != ".json":
        raise ValueError("select a .spr or converted SPR .json")
    document = json.loads(path.read_text(encoding="utf-8"))
    if "frames" not in document or "pages" not in document:
        raise ValueError("not a converted SPR manifest")
    pages = [Image.open(path.parent / name).convert("RGBA") for name in document["pages"]]
    frames = []
    for record in document["frames"]:
        size = tuple(record["size"])
        canvas = Image.new("RGBA", size)
        if record["page"] >= 0:
            x, y, w, h = record["rect"]
            crop = record["crop"]
            fragment = pages[record["page"]].crop((x, y, x + w, y + h))
            canvas.paste(fragment, (crop[0], crop[1]))
        frames.append(Frame(canvas, *record["raw_origin"]))
    return frames, document.get("source", path.name)


def render_preview(frame: Frame, scale: int, mirrored: bool, show_origin: bool) -> Image.Image:
    from PIL import ImageDraw, ImageOps

    image = ImageOps.mirror(frame.image) if mirrored else frame.image
    checker = Image.new("RGB", image.size, "#d0d0d0")
    draw = ImageDraw.Draw(checker)
    for y in range(0, image.height, 8):
        for x in range(0, image.width, 8):
            if (x // 8 + y // 8) % 2:
                draw.rectangle((x, y, x + 7, y + 7), fill="#f0f0f0")
    checker.paste(image, mask=image.getchannel("A"))
    if show_origin:
        ox = image.width - 1 - frame.origin_x if mirrored else frame.origin_x
        oy = frame.origin_y
        if 0 <= ox < image.width and 0 <= oy < image.height:
            draw.line((max(0, ox - 8), oy, min(image.width - 1, ox + 8), oy), fill="#ff0000")
            draw.line((ox, max(0, oy - 8), ox, min(image.height - 1, oy + 8)), fill="#ff0000")
    if scale > 1:
        checker = checker.resize((image.width * scale, image.height * scale), Image.Resampling.NEAREST)
    return checker


def main() -> int:
    import tkinter as tk
    from tkinter import filedialog, messagebox

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("asset", nargs="?", type=Path, help="Original .spr or converted SPR .json")
    args = parser.parse_args()
    window = tk.Tk()
    window.title("SGO Legacy Asset Viewer")
    state: dict = {"frames": [], "index": 0, "name": "", "photo": None, "playing": False}
    toolbar = tk.Frame(window)
    toolbar.pack(fill="x")
    info = tk.StringVar(value="Open an original SPR or converted manifest")
    tk.Label(window, textvariable=info, anchor="w").pack(fill="x")
    canvas = tk.Canvas(window, width=700, height=560, bg="#444444")
    canvas.pack(fill="both", expand=True)
    mirror = tk.BooleanVar(value=False)
    origin = tk.BooleanVar(value=True)
    scale = tk.IntVar(value=2)

    def redraw() -> None:
        if not state["frames"]:
            return
        frame = state["frames"][state["index"]]
        preview = render_preview(frame, scale.get(), mirror.get(), origin.get())
        state["photo"] = ImageTk.PhotoImage(preview)
        canvas.delete("all")
        canvas.create_image(max(canvas.winfo_width() // 2, preview.width // 2),
                            max(canvas.winfo_height() // 2, preview.height // 2),
                            image=state["photo"])
        info.set(f'{state["name"]}  frame {state["index"] + 1}/{len(state["frames"])}  '
                 f'{frame.image.width}×{frame.image.height}  raw origin '
                 f'({frame.origin_x}, {frame.origin_y})  fixed preview interval 100 ms')

    def load(path: Path) -> None:
        try:
            frames, name = read_frames(path)
            if not frames:
                raise ValueError("asset contains no frames")
        except (OSError, ValueError, KeyError, IndexError) as exc:
            messagebox.showerror("Cannot open asset", str(exc))
            return
        state.update(frames=frames, index=0, name=name, playing=False)
        window.title(f"SGO Legacy Asset Viewer — {path.name}")
        redraw()

    def open_file() -> None:
        chosen = filedialog.askopenfilename(filetypes=[("SPR or manifest", "*.spr *.json")])
        if chosen:
            load(Path(chosen))

    def step(delta: int) -> None:
        if state["frames"]:
            state["index"] = (state["index"] + delta) % len(state["frames"])
            redraw()

    def tick() -> None:
        if state["playing"]:
            step(1)
            window.after(100, tick)

    def toggle_play() -> None:
        state["playing"] = not state["playing"]
        if state["playing"]:
            window.after(100, tick)

    tk.Button(toolbar, text="Open", command=open_file).pack(side="left")
    tk.Button(toolbar, text="◀", command=lambda: step(-1)).pack(side="left")
    tk.Button(toolbar, text="▶", command=lambda: step(1)).pack(side="left")
    tk.Button(toolbar, text="Play/Pause", command=toggle_play).pack(side="left")
    tk.Checkbutton(toolbar, text="Mirror", variable=mirror, command=redraw).pack(side="left")
    tk.Checkbutton(toolbar, text="Origin", variable=origin, command=redraw).pack(side="left")
    tk.OptionMenu(toolbar, scale, 1, 2, 3, 4, command=lambda _value: redraw()).pack(side="left")
    window.bind("<Left>", lambda _event: step(-1))
    window.bind("<Right>", lambda _event: step(1))
    if args.asset:
        load(args.asset)
    window.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

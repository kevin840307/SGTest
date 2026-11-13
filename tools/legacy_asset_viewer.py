#!/usr/bin/env python3
"""Standalone Tkinter viewer for decoder-generated PNG + animation.json.

No original asset is packaged. Unknown frame timings use a preview-only delay;
not a claim about original client animation cadence.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
try:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
except ModuleNotFoundError:  # headless CI: data/filter tests require no GUI
    tk = None
    filedialog = messagebox = ttk = None


def select_frames(meta: dict, direction: str = "(all)", action: str = "(all)") -> list[dict]:
    return [frame for frame in meta["frames"]
            if (direction == "(all)" or str(frame.get("direction")) == direction)
            and (action == "(all)" or str(frame.get("action")) == action)]


class Viewer(tk.Tk if tk is not None else object):
    def __init__(self, document: Path):
        super().__init__()
        self.title("Legacy Asset Viewer — 未驗證動畫欄位")
        self.meta_path = document.resolve(strict=True)
        self.meta = json.loads(self.meta_path.read_text(encoding="utf-8"))
        self.records = self.meta["frames"]
        self.idx = 0
        self.playing = False
        self.timer = None
        self.photo = None
        controls = ttk.Frame(self)
        controls.pack(fill="x", padx=8, pady=8)
        self.direction = tk.StringVar(value="(all)")
        self.action = tk.StringVar(value="(all)")
        for caption, value, key in [("方向", self.direction, "direction"), ("動作", self.action, "action")]:
            ttk.Label(controls, text=caption).pack(side="left")
            options = ["(all)"] + sorted({str(f[key]) for f in self.meta["frames"] if f.get(key) is not None})
            combo = ttk.Combobox(controls, textvariable=value, state="readonly", width=15, values=options)
            combo.pack(side="left", padx=4)
            combo.bind("<<ComboboxSelected>>", lambda _: self.refresh_filter())
        ttk.Button(controls, text="上一格", command=lambda: self.step(-1)).pack(side="left")
        ttk.Button(controls, text="下一格", command=lambda: self.step(1)).pack(side="left")
        self.play_button = ttk.Button(controls, text="預覽播放", command=self.toggle)
        self.play_button.pack(side="left")
        self.canvas = tk.Canvas(self, width=500, height=420, bg="#333333")
        self.canvas.pack(padx=8, pady=6)
        self.status = ttk.Label(self, text="", justify="left")
        self.status.pack(fill="x", padx=8, pady=6)
        self.protocol("WM_DELETE_WINDOW", self.close)
        self.draw()

    def refresh_filter(self):
        self.records = select_frames(self.meta, self.direction.get(), self.action.get())
        self.idx = 0
        self.draw()

    def step(self, by: int):
        if not self.records:
            return
        self.idx = (self.idx + by) % len(self.records)
        self.draw()

    def draw(self):
        self.canvas.delete("all")
        if not self.records:
            self.status.configure(text="無符合方向/動作的影格")
            return
        frame = self.records[self.idx]
        name = Path(frame["png"]).name
        png = (self.meta_path.parent / name).resolve(strict=True)
        if png.parent != self.meta_path.parent:
            raise ValueError("unsafe PNG path")
        self.photo = tk.PhotoImage(file=str(png))
        self.canvas.create_image(10, 10, anchor="nw", image=self.photo)
        self.status.configure(text=(
            f"Frame {frame['frame_index']} / {len(self.records)} | {frame['width']}×{frame['height']}\n"
            f"原始錨點 ({frame.get('raw_anchor_x')}, {frame.get('raw_anchor_y')}) — 座標定義未驗證\n"
            f"方向={frame.get('direction')} 動作={frame.get('action')} 速度={frame.get('duration_ms')}ms\n"
            f"Offset=({frame.get('offset_x')}, {frame.get('offset_y')}) Layer={frame.get('z_order')}\n"
            f"SHA256={self.meta.get('source_sha256')}\n"
            "缺少原版播放時間時固定 100ms 預覽，不代表原版速度"))

    def toggle(self):
        self.playing = not self.playing
        self.play_button.configure(text="停止" if self.playing else "預覽播放")
        if self.playing:
            self.schedule()
        elif self.timer:
            self.after_cancel(self.timer)
            self.timer = None

    def schedule(self):
        if not self.playing or not self.records:
            return
        frame = self.records[self.idx]
        delay = frame.get("duration_ms")
        if delay is None:
            delay = 100  # preview only; metadata remains null
        if not isinstance(delay, int) or not 10 <= delay <= 60000:
            messagebox.showerror("錯誤", "非法的影格時間")
            self.toggle()
            return
        self.timer = self.after(delay, self.tick)

    def tick(self):
        self.timer = None
        if self.playing:
            self.step(1)
            self.schedule()

    def close(self):
        if self.timer:
            self.after_cancel(self.timer)
        self.destroy()


def main() -> int:
    if tk is None:
        raise RuntimeError('Tkinter is required to launch Legacy Asset Viewer (desktop Python installation)')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("animation_json", type=Path, nargs="?", help="decoder output animation.json")
    args = parser.parse_args()
    if args.animation_json is None:
        chooser = tk.Tk()
        chooser.withdraw()
        filename = filedialog.askopenfilename(title="選擇 animation.json", filetypes=[("JSON", "*.json")])
        chooser.destroy()
        if not filename:
            return 0
        args.animation_json = Path(filename)
    Viewer(args.animation_json).mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

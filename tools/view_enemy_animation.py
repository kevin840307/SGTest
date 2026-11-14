#!/usr/bin/env python3
"""Inspect audited Enemy ACT actions and logical facings from a private atlas catalog.

Verified v2 catalogs play each frame using the audited HK2 default duration.
Older/unverified catalogs fall back to a fixed 100ms inspection interval. Only
pages for the selected action and facing are decoded at a time.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageTk

if __package__:
    from .convert_assets import Frame
    from .view_legacy_asset import render_preview
else:
    from convert_assets import Frame
    from view_legacy_asset import render_preview


def read_catalog(path: Path) -> dict:
    catalog = json.loads(path.read_text(encoding="utf-8"))
    if catalog.get("version") not in (1, 2) or not catalog.get("groups"):
        raise ValueError("not a supported nonempty Enemy atlas catalog")
    names = [group["enemy"] for group in catalog["groups"]]
    if len(names) != len(set(names)):
        raise ValueError("duplicate Enemy catalog name")
    return catalog


def load_selection(path: Path, group: dict, action_key: str, facing: int) -> tuple[list[dict], dict]:
    """Restore raw-size frames while retaining source timing and mirror metadata."""
    action = next((row for row in group["actions"] if row["key"] == action_key), None)
    if action is None:
        raise ValueError(f"unknown action {action_key}")
    direction = next((row for row in action["directions"] if row["facing"] == facing), None)
    if direction is None:
        raise ValueError(f"unknown logical facing {facing}")
    atlas_root = path.parent.resolve()
    pages: dict[int, Image.Image] = {}
    steps = []
    for row in direction["frames"]:
        page_index = row["page"]
        canvas = Image.new("RGBA", tuple(row["size"]))
        if page_index >= 0:
            if page_index >= len(group["pages"]):
                raise ValueError("page index outside group")
            if page_index not in pages:
                page = (atlas_root / group["pages"][page_index]).resolve()
                if atlas_root not in page.parents:
                    raise ValueError("atlas page outside private root")
                with Image.open(page) as image:
                    pages[page_index] = image.convert("RGBA")
            x, y, width, height = row["rect"]
            left, top, right, bottom = row["crop"]
            if width != right - left or height != bottom - top:
                raise ValueError("atlas rectangle does not match crop")
            fragment = pages[page_index].crop((x, y, x + width, y + height))
            canvas.paste(fragment, (left, top))
        steps.append({"frame": Frame(canvas, *row["raw_origin"]),
                      "source_frame": row["source_frame"],
                      "raw_time": row["raw_time"],
                      "legacy_default_update_calls": row["legacy_default_update_calls"],
                      "legacy_reference_default_duration_ms": row.get("legacy_reference_default_duration_ms"),
                      "flip_x": direction["flip_x"]})
    if not steps:
        raise ValueError("action has no playback steps")
    return steps, action


def playback_delay_ms(step: dict, timing_verified: bool) -> int:
    if timing_verified:
        value = step.get("legacy_reference_default_duration_ms")
        if not isinstance(value, int) or value <= 0:
            raise ValueError("verified timing step has no positive default duration")
        return value
    return 100


def main() -> int:
    import tkinter as tk
    from tkinter import messagebox, ttk

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path, help="Private enemy_catalog.json")
    args = parser.parse_args()
    catalog = read_catalog(args.catalog)
    groups = {group["enemy"]: group for group in catalog["groups"]}

    window = tk.Tk()
    timing_verified = bool(catalog.get("default_timing_verified", False))
    window.title("SGO Enemy ACT Viewer — verified default timing" if timing_verified
                 else "SGO Enemy ACT Viewer — fallback preview timing")
    window.geometry("1000x700")
    window.minsize(800, 600)
    window.maxsize(1440, 900)
    toolbar = ttk.Frame(window)
    toolbar.pack(fill="x")
    enemy_name = tk.StringVar(value=next(iter(groups)))
    action_key = tk.StringVar()
    facing_name = tk.StringVar(value="1")
    scale = tk.IntVar(value=2)
    show_origin = tk.BooleanVar(value=True)
    info = tk.StringVar()
    state = {"steps": [], "action": None, "index": 0, "photo": None,
             "playing": False, "timer": None}
    enemy_box = ttk.Combobox(toolbar, textvariable=enemy_name, values=list(groups), state="readonly", width=11)
    action_box = ttk.Combobox(toolbar, textvariable=action_key, state="readonly", width=5)
    facing_box = ttk.Combobox(toolbar, textvariable=facing_name,
                             values=[str(number) for number in range(1, 9)], state="readonly", width=3)
    for widget in (enemy_box, action_box, facing_box):
        widget.pack(side="left")
    canvas = tk.Canvas(window, bg="#444444")
    canvas.pack(fill="both", expand=True)
    ttk.Label(window, textvariable=info, anchor="w", wraplength=980).pack(fill="x")
    events_info = tk.StringVar()
    ttk.Label(window, textvariable=events_info, anchor="w", wraplength=980).pack(fill="x")

    def redraw() -> None:
        if not state["steps"]:
            return
        step = state["steps"][state["index"]]
        preview = render_preview(step["frame"], scale.get(), step["flip_x"], show_origin.get())
        state["photo"] = ImageTk.PhotoImage(preview)
        canvas.delete("all")
        canvas.create_image(max(canvas.winfo_width() // 2, preview.width // 2),
                            max(canvas.winfo_height() // 2, preview.height // 2),
                            image=state["photo"])
        action = state["action"]
        events_info.set("ACT events (raw frame positions): " +
                        ", ".join(f'{event["at_frame_raw"]}:{event["token"]}'
                                  for event in action["events"]))
        info.set(f'{enemy_name.get()} {action_key.get()} facing {facing_name.get()} '
                 f'step {state["index"] + 1}/{len(state["steps"])} '
                 f'source frame {step["source_frame"]} flip_x={step["flip_x"]} '
                 f'origin=({step["frame"].origin_x},{step["frame"].origin_y}) '
                 f'raw_time={step["raw_time"]} default Update calls={step["legacy_default_update_calls"]} '
                 f'default_ms={step.get("legacy_reference_default_duration_ms")} '
                 f'events={len(action["events"])} H_special={action["special_init_path"]} '
                 f'playback={"verified default" if timing_verified else "100ms fallback"}')

    def select() -> None:
        state["playing"] = False
        if state["timer"] is not None:
            window.after_cancel(state["timer"])
            state["timer"] = None
        try:
            steps, action = load_selection(args.catalog, groups[enemy_name.get()],
                                           action_key.get(), int(facing_name.get()))
        except (OSError, ValueError, KeyError, IndexError) as exc:
            messagebox.showerror("Cannot load animation", str(exc))
            return
        state.update(steps=steps, action=action, index=0)
        redraw()

    def choose_enemy(_event=None) -> None:
        keys = [action["key"] for action in groups[enemy_name.get()]["actions"]]
        action_box.configure(values=keys)
        action_key.set(keys[0])
        select()

    def step(delta: int) -> None:
        if state["steps"]:
            state["index"] = (state["index"] + delta) % len(state["steps"])
            redraw()

    def schedule_current() -> None:
        current = state["steps"][state["index"]]
        state["timer"] = window.after(playback_delay_ms(current, timing_verified), tick)

    def tick() -> None:
        state["timer"] = None
        if state["playing"]:
            step(1)
            schedule_current()

    def toggle_play() -> None:
        state["playing"] = not state["playing"]
        if state["playing"]:
            schedule_current()
        elif state["timer"] is not None:
            window.after_cancel(state["timer"])
            state["timer"] = None

    ttk.Button(toolbar, text="◀", command=lambda: step(-1)).pack(side="left")
    ttk.Button(toolbar, text="▶", command=lambda: step(1)).pack(side="left")
    ttk.Button(toolbar, text="Play/Pause", command=toggle_play).pack(side="left")
    ttk.Checkbutton(toolbar, text="Origin", variable=show_origin, command=redraw).pack(side="left")
    scale_box = ttk.Combobox(toolbar, textvariable=scale, values=[1, 2, 3, 4],
                             state="readonly", width=2)
    scale_box.pack(side="left")
    scale_box.bind("<<ComboboxSelected>>", lambda _event: redraw())
    enemy_box.bind("<<ComboboxSelected>>", choose_enemy)
    action_box.bind("<<ComboboxSelected>>", lambda _event: select())
    facing_box.bind("<<ComboboxSelected>>", lambda _event: select())
    window.bind("<Left>", lambda _event: step(-1))
    window.bind("<Right>", lambda _event: step(1))
    choose_enemy()
    window.mainloop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

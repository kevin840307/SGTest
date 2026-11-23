"""Render the fixed build 119 login captions on the local Windows host."""
from __future__ import annotations

import ctypes
from ctypes import wintypes

from PIL import Image


class BitmapInfoHeader(ctypes.Structure):
    _fields_ = [
        ("size", wintypes.DWORD),
        ("width", wintypes.LONG),
        ("height", wintypes.LONG),
        ("planes", wintypes.WORD),
        ("bit_count", wintypes.WORD),
        ("compression", wintypes.DWORD),
        ("image_size", wintypes.DWORD),
        ("x_pixels_per_meter", wintypes.LONG),
        ("y_pixels_per_meter", wintypes.LONG),
        ("colors_used", wintypes.DWORD),
        ("colors_important", wintypes.DWORD),
    ]


# TextOut uses the top-left ITF coordinates and GDI's own line metrics.
CAPTIONS = (
    ("帳戶名稱", 281, 257),
    ("帳戶密碼", 281, 281),
    ("確定", 399, 318),
    ("離開", 494, 318),
)


def render_hk_login_captions() -> Image.Image:
    gdi = ctypes.WinDLL("gdi32", use_last_error=True)

    def bind(name, result, *arguments):
        function = getattr(gdi, name)
        function.restype = result
        function.argtypes = arguments
        return function

    handle = wintypes.HANDLE
    integer = ctypes.c_int
    dword = wintypes.DWORD
    pointer = ctypes.c_void_p
    create_dc = bind("CreateCompatibleDC", handle, handle)
    create_bitmap = bind("CreateDIBSection", handle, handle, pointer,
                         wintypes.UINT, ctypes.POINTER(pointer), handle, dword)
    create_font = bind("CreateFontW", handle, *([integer] * 5),
                       *([dword] * 8), wintypes.LPCWSTR)
    select_object = bind("SelectObject", handle, handle, handle)
    text_color = bind("SetTextColor", dword, handle, dword)
    background_color = bind("SetBkColor", dword, handle, dword)
    text_out = bind("TextOutW", wintypes.BOOL, handle, integer, integer,
                    wintypes.LPCWSTR, integer)
    flush = bind("GdiFlush", wintypes.BOOL)
    delete_object = bind("DeleteObject", wintypes.BOOL, handle)
    delete_dc = bind("DeleteDC", wintypes.BOOL, handle)

    width, height = 800, 600
    dc = create_dc(None)
    if not dc:
        raise OSError("GDI could not create the HK login caption canvas")
    bitmap = font = previous_bitmap = previous_font = None
    try:
        bitmap_header = BitmapInfoHeader(
            ctypes.sizeof(BitmapInfoHeader), width, -height, 1, 32, 0, 0, 0, 0, 0, 0
        )
        buffer = pointer()
        bitmap = create_bitmap(dc, ctypes.byref(bitmap_header), 0, ctypes.byref(buffer), None, 0)
        font = create_font(12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, "MingLiU")
        if not bitmap or not font:
            raise OSError("GDI could not create the HK login bitmap or font")
        previous_bitmap = select_object(dc, bitmap)
        previous_font = select_object(dc, font)
        ctypes.memset(buffer, 0, width * height * 4)
        text_color(dc, 0xFFFFFF)
        background_color(dc, 0)
        for text, x, y in CAPTIONS:
            if not text_out(dc, x, y, text, len(text)):
                raise OSError(f"GDI could not draw HK login caption {text!r}")
        flush()
        raw = ctypes.string_at(buffer, width * height * 4)
        mask = Image.frombytes("RGB", (width, height), raw, "raw", "BGRX").convert("L")
        overlay = Image.new("RGBA", (width, height), (255, 255, 255, 0))
        overlay.putalpha(mask)
        return overlay
    finally:
        if previous_font:
            select_object(dc, previous_font)
        if previous_bitmap:
            select_object(dc, previous_bitmap)
        if font:
            delete_object(font)
        if bitmap:
            delete_object(bitmap)
        delete_dc(dc)

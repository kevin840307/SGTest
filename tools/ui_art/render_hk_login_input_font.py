"""Generate the HK login edit-control bitmap font from the local Windows PMingLiU.

The generated atlas/descriptor are private local build artifacts under
client/ui/private_ie_hk2 and must never be committed.  Only this generator is
public.  It follows the HK client's 12px, charset=1, quality=4 16-bit GDI
raster path so Godot can reproduce the legacy glyph pixels without shipping
the proprietary Windows font file.
"""
from __future__ import annotations

import ctypes
from ctypes import wintypes
from pathlib import Path

from PIL import Image


FONT_FACE = "PMingLiU"
FONT_HEIGHT = 12
FIRST_CHAR = 32
LAST_CHAR = 126
CELL_WIDTH = 20
CELL_HEIGHT = 16
COLUMNS = 16
ATLAS_NAME = "ACCOUNT_LOGIN_HK_INPUT_FONT.png"
DESCRIPTOR_NAME = "ACCOUNT_LOGIN_HK_INPUT_FONT.fnt"


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


class TextMetricW(ctypes.Structure):
    _fields_ = [
        ("height", wintypes.LONG),
        ("ascent", wintypes.LONG),
        ("descent", wintypes.LONG),
        ("internal_leading", wintypes.LONG),
        ("external_leading", wintypes.LONG),
        ("average_char_width", wintypes.LONG),
        ("max_char_width", wintypes.LONG),
        ("weight", wintypes.LONG),
        ("overhang", wintypes.LONG),
        ("digitized_aspect_x", wintypes.LONG),
        ("digitized_aspect_y", wintypes.LONG),
        ("first_char", wintypes.WCHAR),
        ("last_char", wintypes.WCHAR),
        ("default_char", wintypes.WCHAR),
        ("break_char", wintypes.WCHAR),
        ("italic", wintypes.BYTE),
        ("underlined", wintypes.BYTE),
        ("struck_out", wintypes.BYTE),
        ("pitch_and_family", wintypes.BYTE),
        ("char_set", wintypes.BYTE),
    ]


class Size(ctypes.Structure):
    _fields_ = [("cx", wintypes.LONG), ("cy", wintypes.LONG)]


def _bind(gdi, name, result, *arguments):
    function = getattr(gdi, name)
    function.restype = result
    function.argtypes = arguments
    return function


def write_hk_login_input_font(output_dir: Path) -> tuple[Path, Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    gdi = ctypes.WinDLL("gdi32", use_last_error=True)
    handle = wintypes.HANDLE
    integer = ctypes.c_int
    dword = wintypes.DWORD
    pointer = ctypes.c_void_p

    create_dc = _bind(gdi, "CreateCompatibleDC", handle, handle)
    create_bitmap = _bind(
        gdi, "CreateDIBSection", handle, handle, pointer,
        wintypes.UINT, ctypes.POINTER(pointer), handle, dword
    )
    create_font = _bind(
        gdi, "CreateFontW", handle, *([integer] * 5), *([dword] * 8), wintypes.LPCWSTR
    )
    select_object = _bind(gdi, "SelectObject", handle, handle, handle)
    set_bk_mode = _bind(gdi, "SetBkMode", integer, handle, integer)
    text_color = _bind(gdi, "SetTextColor", dword, handle, dword)
    text_out = _bind(
        gdi, "TextOutW", wintypes.BOOL, handle, integer, integer,
        wintypes.LPCWSTR, integer
    )
    text_extent = _bind(
        gdi, "GetTextExtentPoint32W", wintypes.BOOL, handle,
        wintypes.LPCWSTR, integer, ctypes.POINTER(Size)
    )
    text_metrics = _bind(
        gdi, "GetTextMetricsW", wintypes.BOOL, handle,
        ctypes.POINTER(TextMetricW)
    )
    flush = _bind(gdi, "GdiFlush", wintypes.BOOL)
    delete_object = _bind(gdi, "DeleteObject", wintypes.BOOL, handle)
    delete_dc = _bind(gdi, "DeleteDC", wintypes.BOOL, handle)

    rows = ((LAST_CHAR - FIRST_CHAR + 1) + COLUMNS - 1) // COLUMNS
    atlas_width = COLUMNS * CELL_WIDTH
    atlas_height = rows * CELL_HEIGHT
    atlas = Image.new("RGBA", (atlas_width, atlas_height), (255, 255, 255, 0))

    dc = create_dc(None)
    if not dc:
        raise OSError("GDI could not create the HK login bitmap-font DC")

    bitmap = font = previous_bitmap = previous_font = None
    try:
        header = BitmapInfoHeader(
            ctypes.sizeof(BitmapInfoHeader), CELL_WIDTH, -CELL_HEIGHT,
            1, 16, 0, 0, 0, 0, 0, 0
        )
        buffer = pointer()
        bitmap = create_bitmap(
            dc, ctypes.byref(header), 0, ctypes.byref(buffer), None, 0
        )
        font = create_font(
            FONT_HEIGHT, 0, 0, 0, 400, 0, 0, 0,
            1, 0, 0, 4, 0, FONT_FACE
        )
        if not bitmap or not font:
            raise OSError("GDI could not create the HK login bitmap/font")

        previous_bitmap = select_object(dc, bitmap)
        previous_font = select_object(dc, font)
        set_bk_mode(dc, 1)  # TRANSPARENT
        text_color(dc, 0xFFFFFF)

        metrics = TextMetricW()
        if not text_metrics(dc, ctypes.byref(metrics)):
            raise OSError("GDI could not read PMingLiU text metrics")
        if metrics.height != FONT_HEIGHT:
            raise RuntimeError(
                f"Unexpected {FONT_FACE} metric height {metrics.height}; expected {FONT_HEIGHT}"
            )

        pixels = (ctypes.c_uint16 * (CELL_WIDTH * CELL_HEIGHT)).from_address(buffer.value)
        glyph_lines: list[str] = []

        for index, codepoint in enumerate(range(FIRST_CHAR, LAST_CHAR + 1)):
            for i in range(CELL_WIDTH * CELL_HEIGHT):
                pixels[i] = 0

            character = chr(codepoint)
            extent = Size()
            if not text_extent(dc, character, 1, ctypes.byref(extent)):
                raise OSError(f"GDI could not measure U+{codepoint:04X}")
            if not text_out(dc, 0, 0, character, 1):
                raise OSError(f"GDI could not render U+{codepoint:04X}")
            flush()

            points = [
                (x, y)
                for y in range(CELL_HEIGHT)
                for x in range(CELL_WIDTH)
                if pixels[y * CELL_WIDTH + x] != 0
            ]
            cell_x = (index % COLUMNS) * CELL_WIDTH
            cell_y = (index // COLUMNS) * CELL_HEIGHT
            if points:
                min_x = min(x for x, _ in points)
                min_y = min(y for _, y in points)
                max_x = max(x for x, _ in points) + 1
                max_y = max(y for _, y in points) + 1
                width = max_x - min_x
                height = max_y - min_y
                for x, y in points:
                    atlas.putpixel(
                        (cell_x + x - min_x, cell_y + y - min_y),
                        (255, 255, 255, 255),
                    )
                glyph_x = cell_x
                glyph_y = cell_y
                xoffset = min_x
                yoffset = min_y
            else:
                width = height = 0
                glyph_x = cell_x
                glyph_y = cell_y
                xoffset = yoffset = 0

            glyph_lines.append(
                "char id={id} x={x} y={y} width={w} height={h} "
                "xoffset={xo} yoffset={yo} xadvance={advance} page=0 chnl=15".format(
                    id=codepoint,
                    x=glyph_x,
                    y=glyph_y,
                    w=width,
                    h=height,
                    xo=xoffset,
                    yo=yoffset,
                    advance=extent.cx,
                )
            )

        atlas_path = output_dir / ATLAS_NAME
        descriptor_path = output_dir / DESCRIPTOR_NAME
        atlas.save(atlas_path)
        descriptor = "\n".join(
            [
                f'info face="{FONT_FACE}" size={FONT_HEIGHT} bold=0 italic=0 charset="" '
                "unicode=1 stretchH=100 smooth=0 aa=1 padding=0,0,0,0 spacing=0,0",
                f"common lineHeight={metrics.height} base={metrics.ascent} "
                f"scaleW={atlas_width} scaleH={atlas_height} pages=1 packed=0",
                f'page id=0 file="{ATLAS_NAME}"',
                f"chars count={len(glyph_lines)}",
                *glyph_lines,
                "kernings count=0",
                "",
            ]
        )
        descriptor_path.write_text(descriptor, encoding="utf-8")
        return atlas_path, descriptor_path
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

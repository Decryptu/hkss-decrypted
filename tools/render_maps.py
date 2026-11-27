"""Render illustrated ink-and-parchment cartographic maps from data/map_geometry.json.

Produces high-resolution illustrated maps for:
  - Hallownest (HK overview)
  - Pharloom (SS overview)
  - White Palace & Path of Pain (HK submap)

Used by both the Player Hub (site/assets/maps/) and PopTracker (tools/export_poptracker.py),
sharing the exact same world-to-pixel projection so pins align to rooms with zero drift.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO_ROOT = Path(__file__).resolve().parent.parent
GEOMETRY_PATH = REPO_ROOT / "data/map_geometry.json"
DEFAULT_OUT_DIR = REPO_ROOT / "site/assets/maps"

# Curated biome palettes: (fill_rgb, stroke_rgb, wash_rgb)
HK_PALETTE: dict[str, tuple[tuple[int, int, int], tuple[int, int, int], tuple[int, int, int]]] = {
    "Howling Cliffs":       ((44, 52, 66),   (150, 174, 206), (72, 92, 122)),
    "Dirtmouth":            ((54, 60, 74),   (186, 198, 218), (94, 108, 130)),
    "Crystal Peak":         ((68, 42, 74),   (212, 134, 214), (122, 62, 132)),
    "Greenpath":            ((34, 62, 54),   (120, 198, 162), (48, 110, 86)),
    "Forgotten Crossroads": ((42, 56, 78),   (138, 174, 218), (64, 96, 138)),
    "Resting Grounds":      ((66, 56, 44),   (208, 184, 132), (118, 96, 62)),
    "Queen's Gardens":      ((32, 64, 48),   (114, 198, 138), (44, 114, 72)),
    "Fog Canyon":           ((58, 44, 72),   (188, 138, 214), (104, 70, 132)),
    "Fungal Wastes":        ((64, 56, 38),   (198, 176, 108), (112, 94, 52)),
    "City of Tears":        ((34, 52, 82),   (118, 168, 228), (48, 86, 146)),
    "Deepnest":             ((44, 40, 56),   (146, 136, 178), (74, 64, 98)),
    "Royal Waterways":      ((34, 58, 66),   (112, 182, 196), (48, 98, 114)),
    "Kingdom's Edge":       ((54, 62, 70),   (184, 204, 218), (90, 108, 124)),
    "Ancient Basin":        ((46, 48, 56),   (166, 172, 190), (78, 82, 98)),
    "White Palace":         ((68, 66, 60),   (228, 222, 198), (132, 126, 104)),
}

SS_PALETTE: dict[str, tuple[tuple[int, int, int], tuple[int, int, int], tuple[int, int, int]]] = {
    "Mosslands":         ((36, 62, 48),   (126, 198, 146), (52, 108, 74)),
    "The Marrow":        ((68, 54, 46),   (212, 176, 144), (118, 88, 66)),
    "Wormways":          ((58, 52, 42),   (186, 168, 128), (98, 84, 62)),
    "Deep Docks":        ((76, 42, 34),   (232, 138, 96),  (138, 64, 44)),
    "Hunter's March":    ((74, 38, 40),   (226, 118, 118), (132, 54, 58)),
    "Far Fields":        ((72, 48, 38),   (220, 152, 112), (126, 76, 52)),
    "Greymoor":          ((48, 52, 62),   (164, 174, 196), (82, 90, 110)),
    "Bellhart":          ((64, 46, 46),   (208, 148, 138), (114, 72, 68)),
    "Shellwood":         ((42, 58, 52),   (144, 188, 164), (66, 102, 84)),
    "Blasted Steps":     ((72, 56, 40),   (218, 176, 122), (128, 94, 58)),
    "Sands of Karak":    ((76, 52, 48),   (228, 158, 138), (136, 82, 72)),
    "The Slab":          ((48, 54, 66),   (162, 180, 208), (80, 94, 118)),
    "Mount Fay":         ((40, 56, 76),   (146, 194, 232), (62, 102, 144)),
    "Grand Gate":        ((70, 58, 42),   (220, 188, 128), (124, 98, 62)),
    "Choral Chambers":   ((74, 62, 44),   (232, 198, 134), (134, 108, 68)),
    "Songclave":         ((78, 66, 46),   (240, 210, 146), (144, 118, 72)),
    "Underworks":        ((54, 44, 44),   (182, 140, 132), (94, 70, 66)),
    "Whiteward":         ((62, 62, 68),   (206, 206, 220), (112, 112, 126)),
    "Whispering Vaults": ((58, 46, 66),   (194, 152, 212), (102, 74, 122)),
    "Cogwork Core":      ((74, 56, 36),   (230, 180, 108), (132, 94, 52)),
    "High Halls":        ((72, 60, 48),   (228, 196, 148), (130, 104, 74)),
    "Memorium":          ((44, 60, 58),   (148, 198, 186), (68, 108, 102)),
    "The Cradle":        ((76, 46, 52),   (234, 146, 156), (136, 68, 78)),
    "Sinner's Road":     ((52, 54, 44),   (168, 174, 136), (88, 92, 68)),
    "Bilewater":         ((42, 58, 44),   (138, 186, 132), (64, 102, 64)),
    "Putrified Ducts":   ((44, 56, 52),   (144, 182, 166), (68, 96, 86)),
    "Verdania":          ((36, 66, 54),   (124, 208, 164), (50, 118, 86)),
    "Weavenest":         ((64, 42, 52),   (208, 132, 156), (114, 64, 84)),
    "The Abyss":         ((36, 38, 54),   (138, 146, 194), (58, 64, 102)),
    "Unmapped":          ((50, 48, 56),   (170, 166, 184), (84, 80, 96)),
}

WP_PALETTE = {
    "wp":  ((54, 60, 74), (206, 218, 236), (92, 112, 142)),
    "pop": ((70, 58, 38), (234, 202, 132), (136, 108, 60)),
}

# Decorative shaft sprites in Silksong whose vertical extent is purely visual connecting art.
SS_CLAMP_HEIGHT_ROOMS = {"Abyss_09", "Abyss_09_top", "Abyss_09_up_connection"}


def load_font(size: int, bold: bool = False):
    candidates = (
        ("georgiab.ttf", "palab.ttf", "DejaVuSerif-Bold.ttf", "timesbd.ttf")
        if bold
        else ("georgia.ttf", "pala.ttf", "DejaVuSerif.ttf", "times.ttf")
    )
    for name in candidates:
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def _seed_rng(key: str) -> random.Random:
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    return random.Random(int.from_bytes(digest[:8], "big"))


def _hand_drawn_room_poly(
    x0: float, y0: float, x1: float, y1: float, key: str, jitter: float = 1.6
) -> list[tuple[float, float]]:
    """Build a slightly beveled, organically inked polygon for a room bounding box."""
    rng = _seed_rng(key)
    w = max(2.0, x1 - x0)
    h = max(2.0, y1 - y0)
    chamfer = min(w, h) * 0.14
    chamfer = max(1.0, min(chamfer, 6.0))
    j = min(jitter, min(w, h) * 0.08)

    def pt(px: float, py: float) -> tuple[float, float]:
        return (px + rng.uniform(-j, j), py + rng.uniform(-j, j))

    # Subdivide longer edges so large halls feel gently hand-inked instead of mechanical.
    pts: list[tuple[float, float]] = []
    # Top edge
    pts.append(pt(x0 + chamfer, y0))
    if w > 36:
        pts.append(pt((x0 + x1) * 0.5, y0))
    pts.append(pt(x1 - chamfer, y0))
    # Right edge
    pts.append(pt(x1, y0 + chamfer))
    if h > 36:
        pts.append(pt(x1, (y0 + y1) * 0.5))
    pts.append(pt(x1, y1 - chamfer))
    # Bottom edge
    pts.append(pt(x1 - chamfer, y1))
    if w > 36:
        pts.append(pt((x0 + x1) * 0.5, y1))
    pts.append(pt(x0 + chamfer, y1))
    # Left edge
    pts.append(pt(x0, y1 - chamfer))
    if h > 36:
        pts.append(pt(x0, (y0 + y1) * 0.5))
    pts.append(pt(x0, y0 + chamfer))
    return pts


def _make_parchment_backdrop(width: int, height: int, accent: tuple[int, int, int], seed: str) -> Image.Image:
    """Create a dark vellum/slate-parchment canvas with vignette, subtle grain, and cartographic rules."""
    base = Image.new("RGBA", (width, height), (14, 15, 22, 255))

    # Subtle radial warm/cool atmospheric glow in the center.
    glow = Image.new("RGBA", (width // 4, height // 4), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    cx, cy = glow.width // 2, glow.height // 2
    max_r = int(math.hypot(cx, cy))
    for step in range(18, 0, -1):
        frac = step / 18.0
        r = int(max_r * frac)
        alpha = int((1.0 - frac) * 26)
        gd.ellipse(
            (cx - r, cy - r, cx + r, cy + r),
            fill=(accent[0], accent[1], accent[2], alpha),
        )
    glow = glow.resize((width, height), Image.Resampling.BILINEAR)
    base = Image.alpha_composite(base, glow)

    # Subtle parchment paper grain (deterministic, lightweight tiled noise).
    tile_sz = 128
    rng = _seed_rng(f"grain:{seed}")
    tile = Image.new("RGBA", (tile_sz, tile_sz), (0, 0, 0, 0))
    px = tile.load()
    for y in range(tile_sz):
        for x in range(tile_sz):
            v = rng.randint(-10, 10)
            if v > 0:
                px[x, y] = (225, 218, 200, v)
            elif v < 0:
                px[x, y] = (0, 0, 4, -v)
    grain = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for y in range(0, height, tile_sz):
        for x in range(0, width, tile_sz):
            grain.paste(tile, (x, y))
    base = Image.alpha_composite(base, grain)

    # Faint cartographic grid hairlines.
    grid = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(grid)
    step = max(90, width // 16)
    line_col = (195, 198, 214, 11)
    for x in range(step, width, step):
        draw.line(((x, 24), (x, height - 24)), fill=line_col, width=1)
    for y in range(step, height, step):
        draw.line(((24, y), (width - 24, y)), fill=line_col, width=1)

    # Edge vignette.
    vig = Image.new("RGBA", (width // 4, height // 4), (0, 0, 0, 0))
    vd = ImageDraw.Draw(vig)
    vw, vh = vig.size
    for i in range(16):
        a = int(((16 - i) / 16.0) ** 2 * 95)
        vd.rectangle((i, i, vw - 1 - i, vh - 1 - i), outline=(5, 5, 9, a), width=1)
    vig = vig.resize((width, height), Image.Resampling.BILINEAR)
    base = Image.alpha_composite(base, grid)
    base = Image.alpha_composite(base, vig)
    return base


def _draw_ornamental_frame(draw: ImageDraw.ImageDraw, width: int, height: int, accent: tuple[int, int, int]) -> None:
    m1, m2 = 16, 24
    outer = (accent[0], accent[1], accent[2], 95)
    inner = (210, 204, 190, 48)
    draw.rectangle((m1, m1, width - m1, height - m1), outline=outer, width=2)
    draw.rectangle((m2, m2, width - m2, height - m2), outline=inner, width=1)
    # Corner diamonds.
    d = 7
    for cx, cy in ((m1, m1), (width - m1, m1), (m1, height - m1), (width - m1, height - m1)):
        draw.polygon(((cx, cy - d), (cx + d, cy), (cx, cy + d), (cx - d, cy)),
                     fill=(18, 20, 28, 255), outline=(accent[0], accent[1], accent[2], 185))


def _draw_compass_rose(draw: ImageDraw.ImageDraw, cx: int, cy: int, radius: int, accent: tuple[int, int, int]) -> None:
    ring = (accent[0], accent[1], accent[2], 115)
    bright = (232, 224, 206, 175)
    dim = (accent[0], accent[1], accent[2], 70)
    draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), outline=ring, width=2)
    r_in = int(radius * 0.72)
    draw.ellipse((cx - r_in, cy - r_in, cx + r_in, cy + r_in), outline=dim, width=1)
    # Minor diagonal points
    d_len = int(radius * 0.78)
    d_w = max(3, int(radius * 0.14))
    for angle in (45, 135, 225, 315):
        rad = math.radians(angle)
        px = cx + math.cos(rad) * d_len
        py = cy + math.sin(rad) * d_len
        perp = rad + math.pi / 2
        draw.polygon(
            (
                (cx + math.cos(perp) * d_w, cy + math.sin(perp) * d_w),
                (px, py),
                (cx - math.cos(perp) * d_w, cy - math.sin(perp) * d_w),
            ),
            fill=dim,
            outline=ring,
        )
    # Cardinal points
    c_len = int(radius * 1.15)
    c_w = max(4, int(radius * 0.18))
    for idx, angle in enumerate((-90, 0, 90, 180)):
        rad = math.radians(angle)
        px = cx + math.cos(rad) * c_len
        py = cy + math.sin(rad) * c_len
        perp = rad + math.pi / 2
        draw.polygon(
            (
                (cx, cy),
                (cx + math.cos(perp) * c_w, cy + math.sin(perp) * c_w),
                (px, py),
            ),
            fill=bright if idx == 0 else ring,
        )
        draw.polygon(
            (
                (cx, cy),
                (cx - math.cos(perp) * c_w, cy - math.sin(perp) * c_w),
                (px, py),
            ),
            fill=(28, 30, 40, 220),
            outline=ring,
        )
    f = load_font(max(11, int(radius * 0.34)), bold=True)
    draw.text((cx, cy - c_len - 12), "N", fill=bright, font=f, anchor="mm")


def _draw_title_cartouche(draw: ImageDraw.ImageDraw, title: str, subtitle: str, x: int, y: int, accent: tuple[int, int, int]) -> None:
    title_font = load_font(26, bold=True)
    sub_font = load_font(13, bold=False)
    draw.text((x, y), title, fill=(236, 230, 216, 235), font=title_font, anchor="lt")
    bbox = draw.textbbox((x, y), title, font=title_font, anchor="lt")
    rule_y = bbox[3] + 7
    draw.line(((x, rule_y), (bbox[2] + 24, rule_y)), fill=(accent[0], accent[1], accent[2], 155), width=2)
    if subtitle:
        draw.text((x, rule_y + 7), subtitle, fill=(182, 178, 170, 195), font=sub_font, anchor="lt")


def _compute_projection(
    rooms: list[dict], width: int, height: int, pad_x: int = 86, pad_top: int = 86, pad_bottom: int = 68
) -> dict:
    min_x = min(r["x"] - r["w"] * 0.5 for r in rooms)
    max_x = max(r["x"] + r["w"] * 0.5 for r in rooms)
    min_y = min(r["y"] - r["h"] * 0.5 for r in rooms)
    max_y = max(r["y"] + r["h"] * 0.5 for r in rooms)
    span_x = max(1e-3, max_x - min_x)
    span_y = max(1e-3, max_y - min_y)
    avail_w = width - 2 * pad_x
    avail_h = height - pad_top - pad_bottom
    scale = min(avail_w / span_x, avail_h / span_y)
    offset_x = pad_x + (avail_w - span_x * scale) * 0.5 - min_x * scale
    # Unity map Y is up; image Y is down.
    offset_y = pad_top + (avail_h - span_y * scale) * 0.5 + max_y * scale
    return {
        "width": width,
        "height": height,
        "scale": round(scale, 4),
        "offsetX": round(offset_x, 3),
        "offsetY": round(offset_y, 3),
        "minX": round(min_x, 3),
        "maxX": round(max_x, 3),
        "minY": round(min_y, 3),
        "maxY": round(max_y, 3),
    }


def world_to_px(proj: dict, wx: float, wy: float) -> tuple[float, float]:
    return (proj["offsetX"] + wx * proj["scale"], proj["offsetY"] - wy * proj["scale"])


def _normalize_rooms_for_render(game: str, geom: dict) -> list[dict]:
    rooms = []
    for r in geom[game]["rooms"]:
        item = dict(r)
        if game == "SS" and item["id"] in SS_CLAMP_HEIGHT_ROOMS:
            # Keep vertical shaft slim & bounded so it doesn't distort map bounds.
            item["h"] = min(item["h"], 2.4)
            if item["id"] == "Abyss_09_top":
                item["y"] = -16.2
        rooms.append(item)

    if game == "HK":
        # Add a compact White Palace & Path of Pain inset below Ancient Basin on the HK overview.
        anchor = geom["HK"]["whitePalaceAnchor"]
        wp_scale = 0.48
        for wr in geom["HK"]["whitePalace"]["rooms"]:
            rooms.append({
                "id": wr["id"],
                "area": "White Palace",
                "x": round(anchor["x"] + wr["x"] * wp_scale, 3),
                "y": round(anchor["y"] + wr["y"] * wp_scale, 3),
                "w": round(wr["w"] * wp_scale, 3),
                "h": round(wr["h"] * wp_scale, 3),
                "group": wr.get("group", "wp"),
            })
    return rooms


def _draw_area_washes(
    width: int, height: int, rooms: list[dict], proj: dict, palette: dict
) -> Image.Image:
    # Draw soft biome washes at 1/4 resolution and blur for watercolor/cartography feel.
    q = 4
    layer = Image.new("RGBA", (width // q, height // q), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    by_area: dict[str, list[dict]] = {}
    for r in rooms:
        by_area.setdefault(r["area"], []).append(r)
    for area, area_rooms in by_area.items():
        _, _, wash = palette.get(area, ((50, 50, 62), (160, 160, 180), (80, 80, 100)))
        for r in area_rooms:
            cx, cy = world_to_px(proj, r["x"], r["y"])
            rx = max(14, (r["w"] * proj["scale"] * 0.85) / q)
            ry = max(14, (r["h"] * proj["scale"] * 0.85) / q)
            draw.ellipse(
                (cx / q - rx, cy / q - ry, cx / q + rx, cy / q + ry),
                fill=(wash[0], wash[1], wash[2], 32),
            )
    layer = layer.filter(ImageFilter.GaussianBlur(radius=14))
    return layer.resize((width, height), Image.Resampling.BILINEAR)


def _draw_label_with_halo(
    draw: ImageDraw.ImageDraw, x: float, y: float, text: str, font, fill: tuple[int, int, int, int]
) -> None:
    halo = (10, 11, 16, 220)
    for ox, oy in ((-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, -1), (-1, 1), (1, 1)):
        draw.text((x + ox, y + oy), text, fill=halo, font=font, anchor="mm")
    draw.text((x, y), text, fill=fill, font=font, anchor="mm")


def _compute_area_centroids(rooms: list[dict], proj: dict) -> dict[str, tuple[float, float]]:
    by_area: dict[str, list[dict]] = {}
    for r in rooms:
        by_area.setdefault(r["area"], []).append(r)
    centroids: dict[str, tuple[float, float]] = {}
    for area, area_rooms in by_area.items():
        total_w = sum(max(0.2, r["w"] * r["h"]) for r in area_rooms)
        wx = sum(r["x"] * max(0.2, r["w"] * r["h"]) for r in area_rooms) / total_w
        wy = sum(r["y"] * max(0.2, r["w"] * r["h"]) for r in area_rooms) / total_w
        centroids[area] = world_to_px(proj, wx, wy)
    return centroids


def render_overview(
    game: str,
    geom: dict,
    out_path: Path,
    width: int = 1800,
    height: int = 1220,
    show_labels: bool = True,
) -> dict:
    rooms = _normalize_rooms_for_render(game, geom)
    proj = _compute_projection(rooms, width, height)
    palette = HK_PALETTE if game == "HK" else SS_PALETTE
    accent = (138, 178, 226) if game == "HK" else (222, 116, 110)

    canvas = _make_parchment_backdrop(width, height, accent, f"{game}:{width}x{height}")
    washes = _draw_area_washes(width, height, rooms, proj, palette)
    canvas = Image.alpha_composite(canvas, washes)

    # Render rooms at 2x supersampling for crisp hand-inked edges.
    ss = 2
    hi = Image.new("RGBA", (width * ss, height * ss), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)

    # Shadow pass
    for r in rooms:
        cx, cy = world_to_px(proj, r["x"], r["y"])
        rw = max(5.0, r["w"] * proj["scale"])
        rh = max(5.0, r["h"] * proj["scale"])
        x0, y0 = (cx - rw * 0.5) * ss + 4, (cy - rh * 0.5) * ss + 5
        x1, y1 = (cx + rw * 0.5) * ss + 4, (cy + rh * 0.5) * ss + 5
        poly = _hand_drawn_room_poly(x0, y0, x1, y1, f"{game}:{r['id']}", jitter=1.8)
        hd.polygon(poly, fill=(6, 7, 11, 135))

    # Room fill + subtle inner parchment highlight + inked outline pass
    for r in rooms:
        cx, cy = world_to_px(proj, r["x"], r["y"])
        rw = max(5.0, r["w"] * proj["scale"])
        rh = max(5.0, r["h"] * proj["scale"])
        x0, y0 = (cx - rw * 0.5) * ss, (cy - rh * 0.5) * ss
        x1, y1 = (cx + rw * 0.5) * ss, (cy + rh * 0.5) * ss
        if r.get("group") == "pop":
            fill, stroke, _ = WP_PALETTE["pop"]
        else:
            fill, stroke, _ = palette.get(r["area"], ((48, 52, 66), (168, 178, 200), (80, 88, 108)))
        poly = _hand_drawn_room_poly(x0, y0, x1, y1, f"{game}:{r['id']}", jitter=1.8)
        hd.polygon(poly, fill=(fill[0], fill[1], fill[2], 232))
        # Subtle inner bevel line on top-left
        hd.line(poly + [poly[0]], fill=(stroke[0], stroke[1], stroke[2], 215), width=2)

    rooms_layer = hi.resize((width, height), Image.Resampling.LANCZOS)
    canvas = Image.alpha_composite(canvas, rooms_layer)

    overlay = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    _draw_ornamental_frame(od, width, height, accent)
    title = "HALLOWNEST" if game == "HK" else "PHARLOOM"
    subtitle = "Hollow Knight Atlas" if game == "HK" else "Silksong Atlas"
    _draw_title_cartouche(od, title, subtitle, 48, 40, accent)
    _draw_compass_rose(od, width - 92, height - 92, 42, accent)

    centroids = _compute_area_centroids(rooms, proj)
    if game == "HK" and "White Palace" in centroids:
        wp_rooms = [r for r in rooms if r["area"] == "White Palace"]
        wp_bottom = max(world_to_px(proj, r["x"], r["y"])[1] + r["h"] * proj["scale"] * 0.5 for r in wp_rooms)
        wp_top = min(world_to_px(proj, r["x"], r["y"])[1] - r["h"] * proj["scale"] * 0.5 for r in wp_rooms)
        wp_cx = centroids["White Palace"][0]
        # Subtle divider rule between Ancient Basin and the White Palace dream inset.
        od.line(((wp_cx - 86, wp_top - 12), (wp_cx + 86, wp_top - 12)), fill=(198, 210, 232, 95), width=1)
        centroids["White Palace"] = (wp_cx, wp_bottom + 14)

    if show_labels:
        label_font = load_font(15 if game == "HK" else 13, bold=True)
        for area, (lx, ly) in sorted(centroids.items()):
            _, stroke, _ = palette.get(area, ((50, 52, 64), (210, 210, 220), (80, 80, 96)))
            tint = (
                min(248, stroke[0] + 34),
                min(244, stroke[1] + 32),
                min(238, stroke[2] + 28),
                242,
            )
            _draw_label_with_halo(od, lx, ly, area, label_font, tint)

    canvas = Image.alpha_composite(canvas, overlay)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(out_path, optimize=True)

    return {
        "projection": proj,
        "areas": {area: [round(x, 1), round(y, 1)] for area, (x, y) in sorted(centroids.items())},
        "rooms": {
            r["id"]: {
                "area": r["area"],
                "x": round(world_to_px(proj, r["x"], r["y"])[0], 1),
                "y": round(world_to_px(proj, r["x"], r["y"])[1], 1),
                "w": round(max(6.0, r["w"] * proj["scale"]), 1),
                "h": round(max(6.0, r["h"] * proj["scale"]), 1),
            }
            for r in rooms
        },
    }


def render_white_palace(
    geom: dict,
    out_path: Path,
    width: int = 1480,
    height: int = 1040,
    show_labels: bool = True,
) -> dict:
    wp = geom["HK"]["whitePalace"]
    rooms = wp["rooms"]
    routes = wp["routes"]
    proj = _compute_projection(rooms, width, height, pad_x=110, pad_top=110, pad_bottom=80)
    accent = (198, 210, 232)
    pop_accent = (232, 198, 128)

    canvas = _make_parchment_backdrop(width, height, accent, f"WP:{width}x{height}")

    # Soft halos: silver-blue under Palace, pale gold under Path of Pain.
    q = 4
    wash = Image.new("RGBA", (width // q, height // q), (0, 0, 0, 0))
    wd = ImageDraw.Draw(wash)
    for r in rooms:
        cx, cy = world_to_px(proj, r["x"], r["y"])
        _, _, col = WP_PALETTE[r.get("group", "wp")]
        rx = max(24, (r["w"] * proj["scale"] * 0.9) / q)
        ry = max(20, (r["h"] * proj["scale"] * 0.9) / q)
        wd.ellipse((cx / q - rx, cy / q - ry, cx / q + rx, cy / q + ry), fill=(col[0], col[1], col[2], 38))
    wash = wash.filter(ImageFilter.GaussianBlur(radius=18)).resize((width, height), Image.Resampling.BILINEAR)
    canvas = Image.alpha_composite(canvas, wash)

    ss = 2
    hi = Image.new("RGBA", (width * ss, height * ss), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi)
    by_id = {r["id"]: r for r in rooms}

    # Passage corridors between connected rooms.
    for src, dst in routes:
        if src not in by_id or dst not in by_id:
            continue
        r1, r2 = by_id[src], by_id[dst]
        p1 = world_to_px(proj, r1["x"], r1["y"])
        p2 = world_to_px(proj, r2["x"], r2["y"])
        is_pop = r1.get("group") == "pop" and r2.get("group") == "pop"
        line_col = (214, 182, 114, 155) if is_pop else (142, 168, 206, 145)
        hd.line(((p1[0] * ss, p1[1] * ss), (p2[0] * ss, p2[1] * ss)), fill=line_col, width=5)

    # Rooms with hand-inked contours.
    for r in rooms:
        cx, cy = world_to_px(proj, r["x"], r["y"])
        rw = r["w"] * proj["scale"]
        rh = r["h"] * proj["scale"]
        x0, y0 = (cx - rw * 0.5) * ss, (cy - rh * 0.5) * ss
        x1, y1 = (cx + rw * 0.5) * ss, (cy + rh * 0.5) * ss
        fill, stroke, _ = WP_PALETTE[r.get("group", "wp")]
        shadow = _hand_drawn_room_poly(x0 + 6, y0 + 8, x1 + 6, y1 + 8, f"wp:{r['id']}", jitter=2.2)
        hd.polygon(shadow, fill=(6, 7, 11, 155))
        poly = _hand_drawn_room_poly(x0, y0, x1, y1, f"wp:{r['id']}", jitter=2.2)
        hd.polygon(poly, fill=(fill[0], fill[1], fill[2], 238))
        # Subtle architectural inner border + faint diagonal hatch lines for illustrated depth.
        inner = _hand_drawn_room_poly(x0 + 8, y0 + 8, x1 - 8, y1 - 8, f"wp-in:{r['id']}", jitter=1.0)
        hd.line(inner + [inner[0]], fill=(stroke[0], stroke[1], stroke[2], 52), width=1)
        hd.line(poly + [poly[0]], fill=(stroke[0], stroke[1], stroke[2], 230), width=3)

    canvas = Image.alpha_composite(canvas, hi.resize((width, height), Image.Resampling.LANCZOS))

    overlay = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    _draw_ornamental_frame(od, width, height, accent)
    _draw_title_cartouche(od, "WHITE PALACE", "Palace & Path of Pain Floorplan", 48, 40, accent)
    _draw_compass_rose(od, width - 86, height - 86, 38, accent)

    if show_labels:
        label_font = load_font(12, bold=True)
        for r in rooms:
            cx, cy = world_to_px(proj, r["x"], r["y"])
            rh = r["h"] * proj["scale"]
            is_pop = r.get("group") == "pop"
            col = (242, 224, 176, 240) if is_pop else (228, 234, 246, 240)
            _draw_label_with_halo(od, cx, cy + rh * 0.5 - 15, r["label"], label_font, col)

        # Wing legend
        leg_font = load_font(13, bold=False)
        od.text((48, height - 48), "Blue/Silver: White Palace    ·    Gold: Path of Pain",
                fill=(pop_accent[0], pop_accent[1], pop_accent[2], 195), font=leg_font, anchor="lb")

    canvas = Image.alpha_composite(canvas, overlay)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(out_path, optimize=True)

    return {
        "projection": proj,
        "rooms": {
            r["id"]: {
                "label": r["label"],
                "group": r.get("group", "wp"),
                "x": round(world_to_px(proj, r["x"], r["y"])[0], 1),
                "y": round(world_to_px(proj, r["x"], r["y"])[1], 1),
                "w": round(r["w"] * proj["scale"], 1),
                "h": round(r["h"] * proj["scale"], 1),
            }
            for r in rooms
        },
        "routes": routes,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--geometry", type=Path, default=GEOMETRY_PATH)
    parser.add_argument("--out-dir", type=Path, default=DEFAULT_OUT_DIR)
    args = parser.parse_args()

    geom = json.loads(args.geometry.read_text(encoding="utf-8"))
    hk_meta = render_overview("HK", geom, args.out_dir / "hk.png")
    ss_meta = render_overview("SS", geom, args.out_dir / "ss.png")
    wp_meta = render_white_palace(geom, args.out_dir / "hk_white_palace.png")

    meta_path = args.out_dir / "maps_meta.json"
    meta = {"HK": hk_meta, "SS": ss_meta, "HK_WP": wp_meta}
    meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Rendered HK, SS, and White Palace maps + metadata -> {args.out_dir}")


if __name__ == "__main__":
    main()

"""Build the HKSS PopTracker pack from the packaged Archipelago world and the hub catalog.

The pack has one item grid per game, one overview map per game with pins of up to 12 checks per
area (every check is a section), Archipelago auto-tracking for items and checked locations, and
logic: each check is in or out of logic for the items received, the same way the world decides it
(see tools/poptracker/ and tools/poptracker_logic.py). Images are drawn here, so no game assets are
shipped.
"""

from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from PIL import Image, ImageDraw, ImageFont

from export_hub_data import load_world
from poptracker_logic import hk_data, seal_data, ss_data, to_lua
from test_apworld import DEFAULT_AP_DIR, DEFAULT_APWORLD, REPO_ROOT

CATALOG = REPO_ROOT / "site/world.json"
LUA_SOURCES = REPO_ROOT / "tools/poptracker"
OUTPUT = REPO_ROOT / "local/poptracker"
DEFAULT_ZIP = REPO_ROOT / "dist/hkss-poptracker.zip"
GAMES = {"HK": "Hallownest", "SS": "Pharloom"}
COLOURS = {"HK": (88, 150, 220), "SS": (200, 70, 70)}

# Approximate west/east and elevation, as on the hub's map page (site/map.js).
LAYOUTS = {
    "HK": {
        "Howling Cliffs": (0, 0), "Dirtmouth": (2, 0), "Crystal Peak": (4, 0),
        "Greenpath": (0, 1), "Forgotten Crossroads": (2, 1), "Resting Grounds": (4, 1),
        "Queen's Gardens": (0, 2), "Fog Canyon": (1, 2), "City of Tears": (4, 2),
        "Deepnest": (0, 3), "Fungal Wastes": (2, 3), "Royal Waterways": (4, 3), "Kingdom's Edge": (5, 3),
        "White Palace": (2, 4), "Ancient Basin": (4, 4),
    },
    "SS": {
        "Mount Fay": (0, 0), "The Slab": (1, 0), "The Cradle": (4, 0), "High Halls": (5, 0),
        "Sands of Karak": (0, 1), "Cogwork Core": (3, 1), "Songclave": (4, 1), "Memorium": (5, 1),
        "Blasted Steps": (0, 2), "Grand Gate": (2, 2), "Choral Chambers": (3, 2),
        "Whispering Vaults": (4, 2), "Putrified Ducts": (5, 2),
        "Shellwood": (0, 3), "Bellhart": (1, 3), "Underworks": (2, 3), "Whiteward": (3, 3), "Bilewater": (5, 3),
        "Wormways": (0, 4), "Greymoor": (2, 4), "Sinner's Road": (4, 4), "Verdania": (5, 4),
        "Mosslands": (0, 5), "The Marrow": (2, 5), "Hunter's March": (3, 5), "Far Fields": (4, 5),
        "Weavenest": (0, 6), "Deep Docks": (3, 6), "The Abyss": (4, 6), "Unmapped": (5, 6),
    },
}
CELL_W, CELL_H, MARGIN = 230, 110, 40
CHUNK = 12  # checks per pin, so a pin's popup fits on screen
PIN, PIN_STEP, PINS_PER_ROW = 16, 19, 10

WHITE_PALACE_MAP = "White Palace"
WHITE_PALACE_POP_COLOUR = (216, 185, 122)
WHITE_PALACE_PASSAGE_COLOUR = (78, 84, 102)
WHITE_PALACE_ROOMS = {
    "White_Palace_11": ("Palace Entry", (1, 4), False),
    "White_Palace_01": ("Entrance Hall", (2, 4), False),
    "White_Palace_02": ("Entrance Wing", (3, 4), False),
    "White_Palace_03_hub": ("Central Hub", (2, 3), False),
    "White_Palace_04": ("Left Wing Lower", (0, 4), False),
    "White_Palace_14": ("Left Wing Upper", (0, 3), False),
    "White_Palace_15": ("Right Wing Entry", (3, 3), False),
    "White_Palace_05": ("Right Wing Sawblades", (4, 3), False),
    "White_Palace_16": ("Right Wing Upper", (5, 3), False),
    "White_Palace_06": ("Central Atrium", (2, 2), False),
    "White_Palace_07": ("Upper Ascent", (3, 2), False),
    "White_Palace_12": ("Upper Corridor", (3, 1), False),
    "White_Palace_13": ("Throne Tower", (4, 0), False),
    "White_Palace_08": ("Palace Workshop", (5, 0), False),
    "White_Palace_09": ("Throne Room", (3, 0), False),
    "White_Palace_18": ("PoP: Entrance", (0, 2), True),
    "White_Palace_17": ("PoP: Lever Room", (0, 1), True),
    "White_Palace_19": ("PoP: Vertical Shaft", (1, 1), True),
    "White_Palace_20": ("PoP: Final Gauntlet", (1, 0), True),
}
WHITE_PALACE_ROUTES = [
    ("White_Palace_11", "White_Palace_01"),
    ("White_Palace_01", "White_Palace_02"),
    ("White_Palace_01", "White_Palace_03_hub"),
    ("White_Palace_03_hub", "White_Palace_04"),
    ("White_Palace_03_hub", "White_Palace_14"),
    ("White_Palace_04", "White_Palace_14"),
    ("White_Palace_03_hub", "White_Palace_15"),
    ("White_Palace_15", "White_Palace_05"),
    ("White_Palace_05", "White_Palace_16"),
    ("White_Palace_03_hub", "White_Palace_06"),
    ("White_Palace_06", "White_Palace_07"),
    ("White_Palace_07", "White_Palace_12"),
    ("White_Palace_12", "White_Palace_13"),
    ("White_Palace_13", "White_Palace_09"),
    ("White_Palace_13", "White_Palace_08"),
    ("White_Palace_06", "White_Palace_18"),
    ("White_Palace_18", "White_Palace_17"),
    ("White_Palace_17", "White_Palace_19"),
    ("White_Palace_19", "White_Palace_20"),
]

# Tracked items: (Archipelago item name without the game prefix, label, maximum count).
# A maximum of 1 is a toggle; anything else counts up as copies arrive.
TRACKED = {
    "HK": [
        ("Mothwing_Cloak", "Mothwing Cloak", 1), ("Left_Mothwing_Cloak", "Left Cloak", 1),
        ("Right_Mothwing_Cloak", "Right Cloak", 1), ("Shade_Cloak", "Shade Cloak", 1),
        ("Mantis_Claw", "Mantis Claw", 1), ("Left_Mantis_Claw", "Left Claw", 1),
        ("Right_Mantis_Claw", "Right Claw", 1), ("Crystal_Heart", "Crystal Heart", 1),
        ("Left_Crystal_Heart", "Left C-Heart", 1), ("Right_Crystal_Heart", "Right C-Heart", 1),
        ("Monarch_Wings", "Monarch Wings", 1), ("Isma's_Tear", "Isma's Tear", 1), ("Swim", "Swim", 1),
        ("Vengeful_Spirit", "Vengeful Spirit", 1), ("Shade_Soul", "Shade Soul", 1),
        ("Desolate_Dive", "Desolate Dive", 1), ("Descending_Dark", "Descending Dark", 1),
        ("Howling_Wraiths", "Howling Wraiths", 1), ("Abyss_Shriek", "Abyss Shriek", 1),
        ("Focus", "Focus", 1), ("Cyclone_Slash", "Cyclone Slash", 1), ("Dash_Slash", "Dash Slash", 1),
        ("Great_Slash", "Great Slash", 1), ("Dream_Nail", "Dream Nail", 1), ("Dream_Gate", "Dream Gate", 1),
        ("Awoken_Dream_Nail", "Awoken Dream Nail", 1), ("Lumafly_Lantern", "Lumafly Lantern", 1),
        ("Tram_Pass", "Tram Pass", 1), ("City_Crest", "City Crest", 1), ("Elegant_Key", "Elegant Key", 1),
        ("Love_Key", "Love Key", 1), ("Shopkeeper's_Key", "Shopkeeper's Key", 1),
        ("King's_Brand", "King's Brand", 1), ("Simple_Key", "Simple Key", 4),
        ("Mask_Shard", "Mask Shard", 16), ("Vessel_Fragment", "Vessel Fragment", 9),
        ("Charm_Notch", "Charm Notch", 8), ("Pale_Ore", "Pale Ore", 6), ("Grub", "Grub", 46),
        ("Rancid_Egg", "Rancid Egg", 21), ("Dreamer", "Dreamer", 3), ("Lurien", "Lurien", 1),
        ("Monomon", "Monomon", 1), ("Herrah", "Herrah", 1), ("King_Fragment", "King Fragment", 1),
        ("Queen_Fragment", "Queen Fragment", 1), ("Void_Heart", "Void Heart", 1),
        ("Grimmkin_Flame", "Grimmkin Flame", 10),
    ],
    "SS": [
        ("Swift Step", "Swift Step", 1), ("Swift Step (Left)", "Left Swift Step", 1),
        ("Swift Step (Right)", "Right Swift Step", 1), ("Progressive Swift Step", "Swift Step+", 2),
        ("Cling Grip", "Cling Grip", 1), ("Cling Grip (Left)", "Left Cling Grip", 1),
        ("Cling Grip (Right)", "Right Cling Grip", 1), ("Clawline", "Clawline", 1),
        ("Clawline (Left)", "Left Clawline", 1), ("Clawline (Right)", "Right Clawline", 1),
        ("Dash (Left)", "Left Dash", 1), ("Dash (Right)", "Right Dash", 1),
        ("Ledge Grab", "Ledge Grab", 1), ("Drifter's Cloak", "Drifter's Cloak", 1),
        ("Faydown Cloak", "Faydown Cloak", 1), ("Silk Soar", "Silk Soar", 1), ("Needolin", "Needolin", 1),
        ("Swim", "Swim", 1), ("Sprint", "Sprint", 1), ("Needle Strike", "Needle Strike", 1),
        ("Elegy of the Deep", "Elegy of the Deep", 1), ("Beastling Call", "Beastling Call", 1),
        ("Sylphsong", "Sylphsong", 1), ("Silkspear", "Silkspear", 1), ("Thread Storm", "Thread Storm", 1),
        ("Cross Stitch", "Cross Stitch", 1), ("Sharpdart", "Sharpdart", 1), ("Rune Rage", "Rune Rage", 1),
        ("Pale Nails", "Pale Nails", 1), ("Crest: Hunter", "Hunter Crest", 1), ("Crest: Reaper", "Reaper Crest", 1),
        ("Crest: Wanderer", "Wanderer Crest", 1), ("Crest: Beast", "Beast Crest", 1),
        ("Crest: Witch", "Witch Crest", 1), ("Crest: Architect", "Architect Crest", 1),
        ("Crest: Shaman", "Shaman Crest", 1), ("Architect's Key", "Architect's Key", 1),
        ("Diving Bell Key", "Diving Bell Key", 1), ("Surgeon's Key", "Surgeon's Key", 1),
        ("White Key", "White Key", 1), ("Key of Apostate", "Key of Apostate", 1),
        ("Key of Heretic", "Key of Heretic", 1), ("Key of Indolent", "Key of Indolent", 1),
        ("Conductor's Melody", "Conductor's Melody", 1), ("Architect's Melody", "Architect's Melody", 1),
        ("Vaultkeeper's Melody", "Vaultkeeper's Melody", 1), ("Everbloom", "Everbloom", 1),
        ("Mask Shard", "Mask Shard", 20), ("Spool Fragment", "Spool Fragment", 18),
        ("Progressive Needle Upgrade", "Needle Upgrade", 4), ("Progressive Silkheart", "Silkheart", 3),
        ("Progressive Tool Pouch", "Tool Pouch", 4), ("Progressive Crafting Kit", "Crafting Kit", 4),
        ("Flea", "Flea", 30), ("Craftmetal", "Craftmetal", 8), ("Pale Oil", "Pale Oil", 3),
        ("Memory Locket", "Memory Locket", 20), ("Simple Key (Deep Docks)", "Simple Key (Docks)", 1),
        ("Simple Key (Wormways)", "Simple Key (Wormways)", 1),
        ("Simple Key (Green Prince)", "Simple Key (Prince)", 1),
        ("Simple Key (Rosary Bank)", "Simple Key (Bank)", 1),
    ],
}


def code(game: str, name: str) -> str:
    return f"{game.lower()}_" + "".join(c if c.isalnum() else "_" for c in name.lower()).strip("_")


def font(size: int):
    for candidate in ("georgia.ttf", "DejaVuSerif.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def badge(path: Path, game: str, label: str) -> None:
    """A 64x64 icon: the game's colour ring and the label's initials."""
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((2, 2, 61, 61), fill=(18, 18, 26, 255), outline=COLOURS[game] + (255,), width=4)
    words = [w for w in label.replace("(", " ").replace(")", " ").replace("-", " ").split() if w[0].isalnum()]
    initials = "".join(w[0] for w in words[:3]).upper() or label[:2].upper()
    if label.endswith("+"):
        initials += "+"
    text_font = font(24 if len(initials) <= 2 else 18)
    draw.text((32, 32), initials, fill=(235, 230, 220, 255), font=text_font, anchor="mm")
    image.save(path)


def overview_map(path: Path, game: str, positions: dict[str, tuple[int, int]], routes) -> None:
    columns = max(x for x, _ in positions.values()) + 1
    rows = max(y for _, y in positions.values()) + 1
    image = Image.new("RGB", (columns * CELL_W + 2 * MARGIN, rows * CELL_H + 2 * MARGIN), (12, 12, 18))
    draw = ImageDraw.Draw(image)

    def centre(area):
        x, y = positions[area]
        return MARGIN + x * CELL_W + CELL_W // 2, MARGIN + y * CELL_H + CELL_H // 2

    for source, target in routes:
        if source in positions and target in positions:
            draw.line((centre(source), centre(target)), fill=(70, 70, 86), width=3)
    label_font = font(15)
    for area in positions:
        cx, cy = centre(area)
        draw.rounded_rectangle((cx - 105, cy - 30, cx + 105, cy + 30), radius=10,
                               fill=(26, 26, 36), outline=COLOURS[game], width=2)
        draw.text((cx, cy + 20), area, fill=(225, 220, 210), font=label_font, anchor="mm")
    image.save(path)


def white_palace_map(path: Path, active_rooms: set[str]) -> None:
    columns = max(pos[0] for _, pos, _ in WHITE_PALACE_ROOMS.values()) + 1
    rows = max(pos[1] for _, pos, _ in WHITE_PALACE_ROOMS.values()) + 1
    image = Image.new("RGB", (columns * CELL_W + 2 * MARGIN, rows * CELL_H + 2 * MARGIN), (12, 12, 18))
    draw = ImageDraw.Draw(image)

    def centre(room_id):
        _, (x, y), _ = WHITE_PALACE_ROOMS[room_id]
        return MARGIN + x * CELL_W + CELL_W // 2, MARGIN + y * CELL_H + CELL_H // 2

    for source, target in WHITE_PALACE_ROUTES:
        if source in WHITE_PALACE_ROOMS and target in WHITE_PALACE_ROOMS:
            is_pop = WHITE_PALACE_ROOMS[source][2] and WHITE_PALACE_ROOMS[target][2]
            draw.line((centre(source), centre(target)),
                      fill=(120, 105, 76) if is_pop else (70, 70, 86), width=3)
    label_font = font(14)
    for room_id, (label, _, is_pop) in WHITE_PALACE_ROOMS.items():
        cx, cy = centre(room_id)
        has_checks = room_id in active_rooms
        outline = WHITE_PALACE_POP_COLOUR if is_pop else (COLOURS["HK"] if has_checks else WHITE_PALACE_PASSAGE_COLOUR)
        fill = (30, 28, 34) if is_pop else ((26, 26, 36) if has_checks else (20, 20, 28))
        text_colour = (235, 224, 196) if is_pop else ((225, 220, 210) if has_checks else (145, 148, 162))
        draw.rounded_rectangle((cx - 105, cy - 30, cx + 105, cy + 30), radius=10,
                               fill=fill, outline=outline, width=2)
        draw.text((cx, cy + (18 if has_checks else 0)), label, fill=text_colour, font=label_font, anchor="mm")
    image.save(path)


def build(archive: Path, ap_dir: Path, out: Path) -> dict:
    world = load_world(archive, ap_dir)
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    version = catalog["version"]

    if out.exists():
        shutil.rmtree(out)
    for folder in ("images/items", "images/maps", "items", "layouts", "locations", "maps", "scripts"):
        (out / folder).mkdir(parents=True)

    # Items.
    items, item_mapping, grids, missing = [], {}, {}, []
    for game, tracked in TRACKED.items():
        grids[game] = []
        for name, label, maximum in tracked:
            full = f"{game} {name}"
            item_id = world.item_name_to_id.get(full)
            if item_id is None:
                missing.append(full)
                continue
            item_code = code(game, name)
            badge(out / f"images/items/{item_code}.png", game, label)
            image = f"images/items/{item_code}.png"
            if maximum == 1:
                items.append({"name": label, "type": "toggle", "img": image, "codes": item_code})
                item_mapping[item_id] = [item_code, "toggle"]
            else:
                items.append({"name": label, "type": "consumable", "img": image, "codes": item_code,
                              "max_quantity": maximum, "min_quantity": 0, "increment": 1,
                              "decrement": 1, "initial_quantity": 0})
                item_mapping[item_id] = [item_code, "consumable"]
            grids[game].append(item_code)
    # Hidden counter the auto-tracker bumps so PopTracker re-evaluates logic for items without icons.
    items.append({"name": "Logic", "type": "consumable", "codes": "hkss_logic", "max_quantity": 100000,
                  "min_quantity": 0, "initial_quantity": 0, "img": f"images/items/{grids['HK'][0]}.png"})
    (out / "items/items.json").write_text(json.dumps(items, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    # Maps and locations: pins of up to CHUNK checks per area, one section per check.
    maps, locations, location_mapping, location_names = [], [], {}, {}
    for game, layout in LAYOUTS.items():
        checks = [check for check in catalog["checks"] if check["game"] == game]
        positions = dict(layout)
        extra = sorted({check["areas"][0] for check in checks if check["areas"]} - positions.keys())
        rows = max(y for _, y in positions.values()) + 1
        for index, area in enumerate(extra):
            positions[area] = (index % 6, rows + index // 6)
        map_name = GAMES[game]
        overview_map(out / f"images/maps/{game.lower()}.png", game, positions,
                     [tuple(route) for route in catalog["routes"][game]])
        maps.append({"name": map_name, "location_size": PIN, "location_border_thickness": 2,
                     "img": f"images/maps/{game.lower()}.png"})

        by_area: dict[str, list[dict]] = {}
        for check in checks:
            by_area.setdefault(check["areas"][0] if check["areas"] else "Unmapped", []).append(check)
        children = []
        for area in sorted(by_area, key=lambda a: positions.get(a, (99, 99))[::-1]):
            if area not in positions:
                positions[area] = (5, rows + len(extra))
            x, y = positions[area]
            ordered = sorted(by_area[area], key=lambda c: c["name"].lower())
            names = [c["name"] for c in ordered]
            if len(names) != len(set(names)):
                raise SystemExit(f"Duplicate check names in {area}")
            chunks = [ordered[i:i + CHUNK] for i in range(0, len(ordered), CHUNK)]
            cx, cy = MARGIN + x * CELL_W + CELL_W // 2, MARGIN + y * CELL_H + CELL_H // 2
            for index, chunk in enumerate(chunks):
                location_name = f"{map_name}: {area}" + (f" ({index + 1}/{len(chunks)})" if len(chunks) > 1 else "")
                row, column = divmod(index, PINS_PER_ROW)
                in_row = min(PINS_PER_ROW, len(chunks) - row * PINS_PER_ROW)
                sections = []
                for check in chunk:
                    sections.append({"name": check["name"], "item_count": 1,
                                     "access_rules": [f"$hkss_reach|{check['id']}"]})
                    location_names[check["id"]] = check["key"]
                    location_mapping.setdefault(check["id"], []).append(f"@{map_name}/{location_name}/{check['name']}")
                children.append({
                    "name": location_name,
                    "sections": sections,
                    "map_locations": [{"map": map_name,
                                       "x": cx + round((column - (in_row - 1) / 2) * PIN_STEP),
                                       "y": cy - 16 + row * PIN_STEP}],
                })
        locations.append({"name": map_name, "children": children})

    # White Palace & Path of Pain room-by-room sub-map.
    wp_by_room: dict[str, list[dict]] = {}
    for check in catalog["checks"]:
        if check["game"] == "HK" and check.get("room") in WHITE_PALACE_ROOMS:
            wp_by_room.setdefault(check["room"], []).append(check)
    white_palace_map(out / "images/maps/hk_white_palace.png", set(wp_by_room))
    maps.append({"name": WHITE_PALACE_MAP, "location_size": PIN, "location_border_thickness": 2,
                 "img": "images/maps/hk_white_palace.png"})
    wp_children = []
    for room_id in sorted(wp_by_room, key=lambda r: WHITE_PALACE_ROOMS[r][1][::-1]):
        label, (x, y), _ = WHITE_PALACE_ROOMS[room_id]
        ordered = sorted(wp_by_room[room_id], key=lambda c: c["name"].lower())
        location_name = f"{WHITE_PALACE_MAP}: {label}"
        cx, cy = MARGIN + x * CELL_W + CELL_W // 2, MARGIN + y * CELL_H + CELL_H // 2
        sections = []
        for check in ordered:
            sections.append({"name": check["name"], "item_count": 1,
                             "access_rules": [f"$hkss_reach|{check['id']}"]})
            location_mapping.setdefault(check["id"], []).append(f"@{WHITE_PALACE_MAP}/{location_name}/{check['name']}")
        wp_children.append({
            "name": location_name,
            "sections": sections,
            "map_locations": [{"map": WHITE_PALACE_MAP, "x": cx, "y": cy - 12}],
        })
    locations.append({"name": WHITE_PALACE_MAP, "children": wp_children})

    (out / "maps/maps.json").write_text(json.dumps(maps, indent=2) + "\n", encoding="utf-8")
    (out / "locations/locations.json").write_text(
        json.dumps(locations, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    # Layouts: item grids beside a tab per game map.
    def grid(game):
        rows = [grids[game][i:i + 8] for i in range(0, len(grids[game]), 8)]
        return {"type": "group", "header": GAMES[game], "content": {
            "type": "itemgrid", "item_margin": "2,2", "item_size": 40, "rows": rows}}

    tracker = {
        "tracker_default": {"type": "container", "background": "#0c0c12", "content": {
            "type": "dock", "dropshadow": True, "content": [
                {"type": "dock", "dock": "left", "v_alignment": "stretch", "content": [
                    {"type": "array", "orientation": "vertical", "content": [grid("HK"), grid("SS")]},
                ]},
                {"type": "tabbed", "tabs": [
                    {"title": GAMES["HK"], "content": {"type": "map", "maps": [GAMES["HK"]]}},
                    {"title": WHITE_PALACE_MAP, "content": {"type": "map", "maps": [WHITE_PALACE_MAP]}},
                    {"title": GAMES["SS"], "content": {"type": "map", "maps": [GAMES["SS"]]}},
                ]},
            ]}},
        "tracker_broadcast": {"type": "array", "orientation": "vertical", "content": [grid("HK"), grid("SS")]},
    }
    (out / "layouts/tracker.json").write_text(json.dumps(tracker, indent=2) + "\n", encoding="utf-8")

    # Manifest and scripts.
    manifest = {
        "name": "HKSS Decrypted",
        "game_name": catalog["game"],
        "package_version": version,
        "package_uid": "hkss_decrypted",
        "platform": "pc",
        "author": "Decrypt",
        "variants": {"standard": {"display_name": "Map Tracker", "flags": ["ap"]}},
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    import importlib
    cross_game_items_mod = importlib.import_module(f"{world.__module__}.cross_game_items")
    CROSS_GAME_ITEMS = cross_game_items_mod.CROSS_GAME_ITEMS

    grid_names = {code(game, name): f"{game} {name}" for game, tracked in TRACKED.items()
                  for name, _, _ in tracked if f"{game} {name}" in world.item_name_to_id}
    shared_items = {
        spec.name: {
            "id": spec.code,
            "hk": spec.hk_item,
            "ss": spec.ss_item,
            "codes": [
                item_code for item_code in (code("HK", spec.hk_item), code("SS", spec.ss_item))
                if item_code in grid_names
            ],
        }
        for spec in CROSS_GAME_ITEMS
    }
    names_lua = "\n".join((
        "-- Generated by tools/export_poptracker.py.",
        "ITEM_MAPPING = " + to_lua(item_mapping),
        "ITEM_NAMES = " + to_lua({item_id: name for name, item_id in world.item_name_to_id.items()}),
        "CROSS_GAME_ITEMS = " + to_lua(shared_items),
        "GRID_ITEMS = " + to_lua(grid_names),
        "LOCATION_MAPPING = " + to_lua(location_mapping),
        "LOCATION_NAMES = " + to_lua(location_names),
    )) + "\n"
    (out / "scripts/data_names.lua").write_text(names_lua, encoding="utf-8")
    (out / "scripts/data_hk.lua").write_text(
        "-- Generated by tools/export_poptracker.py from the Hollow Knight world's rules.\n"
        "HK_LOGIC = " + to_lua(hk_data(world)) + "\n", encoding="utf-8")
    ss_tables, ss_constants = ss_data(world)
    (out / "scripts/data_ss.lua").write_text(
        "-- Generated by tools/export_poptracker.py from the Silksong world's map logic.\n"
        "SS_LOGIC = " + to_lua(ss_tables) + "\n"
        "SS_LOGIC_CONSTANTS = " + to_lua(ss_constants) + "\n", encoding="utf-8")
    (out / "scripts/data_seals.lua").write_text(
        "-- Generated by tools/export_poptracker.py: scenes and areas used by cross-game seals.\n"
        "SEAL_DATA = " + to_lua(seal_data(world)) + "\n", encoding="utf-8")
    for source in sorted(LUA_SOURCES.glob("*.lua")):
        shutil.copyfile(source, out / "scripts" / source.name)
    (out / "README.md").write_text(README.format(version=version), encoding="utf-8")

    return {"version": version, "items": len(items), "checks": len(location_mapping), "missing": missing}


README = """# HKSS Decrypted PopTracker pack {version}

A [PopTracker](https://github.com/black-sliver/PopTracker) pack for the Hollow Knight x Silksong
Archipelago world. Generated by `tools/export_poptracker.py`; do not edit by hand.

- Item grids for Hallownest and Pharloom progression, filled automatically from Archipelago.
- One overview map per game with pins of up to 12 checks per area, plus a room-by-room
  White Palace & Path of Pain sub-map tab; checked locations, and checks that are not in your
  slot, are cleared automatically.
- Logic for both games: a check is green when the items you have received can reach it, and red
  when they cannot. It follows your slot's options (skips, split abilities, entrance
  randomization, ...) from the slot data, so connect to see your seed's logic. Without a
  connection, the item grid drives it with default options.

Install: put the `.zip` in PopTracker's `packs` folder, open it, then use the AP button to connect
with your server address, slot name and password.
"""


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apworld", type=Path, default=DEFAULT_APWORLD)
    parser.add_argument("--ap-dir", type=Path, default=DEFAULT_AP_DIR)
    parser.add_argument("--out", type=Path, default=OUTPUT)
    parser.add_argument("--zip", type=Path, default=DEFAULT_ZIP, help="Zip to attach to a release")
    args = parser.parse_args()
    result = build(args.apworld.resolve(), args.ap_dir.resolve(), args.out.resolve())
    if result["missing"]:
        raise SystemExit("Tracked items missing from the world: " + ", ".join(result["missing"]))
    args.zip.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(args.zip, "w", ZIP_DEFLATED) as package:
        for path in sorted(args.out.rglob("*")):
            if path.is_file():
                package.write(path, path.relative_to(args.out).as_posix())
    print(f"PopTracker pack {result['version']}: {result['items']} items, {result['checks']} checks")


if __name__ == "__main__":
    main()

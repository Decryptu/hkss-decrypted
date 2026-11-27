"""Extract room-accurate map geometry from local Hollow Knight and Silksong installs.

Reads only room positions, bounding boxes, and area groupings from the games'
GameMap prefabs (no textures, sprites, or copyrighted assets are extracted or
saved). Writes data/map_geometry.json as the single source of truth for the
player hub, PopTracker pack, and in-game unmapped area maps.
"""

from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path

import UnityPy

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_STEAM_COMMON = Path(
    os.environ.get("STEAM_COMMON", r"C:\Program Files (x86)\Steam\steamapps\common")
)
DEFAULT_HK_ROOMS = (
    REPO_ROOT / "local/reference/hk-randomizermod/RandomizerMod/Resources/Data/rooms.json"
)
DEFAULT_OUTPUT = REPO_ROOT / "data/map_geometry.json"

HK_AREA_NAMES = {
    "Crossroads": "Forgotten Crossroads",
    "Waterways": "Royal Waterways",
    "Cliffs": "Howling Cliffs",
    "Kingdoms_Edge": "Kingdom's Edge",
    "Green_Path": "Greenpath",
    "Fog_Canyon": "Fog Canyon",
    "Fungal Wastes": "Fungal Wastes",
    "Queens_Gardens": "Queen's Gardens",
    "Deepnest": "Deepnest",
    "Town_Tutorial": "Dirtmouth",
    "Resting_Grounds": "Resting Grounds",
    "Crystal Peak": "Crystal Peak",
    "Ancient Basin": "Ancient Basin",
    "City of Tears": "City of Tears",
}

HK_SKIP_PARENTS = {
    "Dream_Gate_Pin",
    "Compass Icon",
    "Shade Pos",
    "Flame Pins",
    "Dreamer Pins",
    "Map Markers",
}

SS_AREA_NAMES = {
    "Tut": "Mosslands",
    "Bonetown": "Mosslands",
    "Bone": "The Marrow",
    "Crawl": "Wormways",
    "Dock": "Deep Docks",
    "Weavehome": "Weavenest",
    "Ant": "Hunter's March",
    "Wilds": "Far Fields",
    "Greymoor": "Greymoor",
    "Dust": "Sinner's Road",
    "Dust Maze": "Sinner's Road",
    "Wisp": "Greymoor",
    "Swamp": "Bilewater",
    "Aqueduct": "Putrified Ducts",
    "Belltown": "Bellhart",
    "Shellwood": "Shellwood",
    "Blasted_Steps": "Blasted Steps",
    "Coral_Caves": "Sands of Karak",
    "Slab": "The Slab",
    "Peak": "Mount Fay",
    "Song": "Choral Chambers",
    "Song_Gate": "Grand Gate",
    "Under": "Underworks",
    "Library": "Whispering Vaults",
    "Cog": "Cogwork Core",
    "Ward": "Whiteward",
    "Hang": "High Halls",
    "Arborium": "Memorium",
    "Cradle": "The Cradle",
    "Clover": "Verdania",
    "Abyss": "The Abyss",
    "Surface": "The Cradle",
}

SS_SKIP_PARENTS = {
    "Pan Audio Loop",
    "Main Quest Pins",
    "Compass Icon",
    "Shade Pos",
    "Map Markers",
    "Flea Tracker Markers",
}

SKIP_CHILD_SUBSTRINGS = (
    "Area Name",
    "Sub Area Name",
    "Pin_",
    "pin_",
    "Marker",
    "Thread_Target",
    "Compass",
    "Shade",
    "Stag",
    "Bench",
    "Shop",
    "Flea",
    "Bellway_Icon",
    "Ventrica",
    "Tube",
    "Icon",
    "Glow",
    "Text",
    "Label",
    "Hot_Spring",
    "Whispering_Root",
    "Dream_Tree",
    "Cocoon",
    "Corpse",
    "Tram_Pin",
)

# Interior / sub-scenes in Hollow Knight that map onto a parent room on Game_Map.
# Mirrors HkCheckMap.InteriorToMapRoom.
HK_INTERIOR_TO_MAP_ROOM = {
    "Room_shop": "Town",
    "Room_mapper": "Town",
    "Room_Ouiji": "Town",
    "Room_Sly_Storeroom": "Town",
    "Room_Town_Stag_Station": "Town",
    "Room_Bretta": "Town",
    "Room_Jinn": "Town",
    "Grimm_Main_Tent": "Town",
    "Grimm_Divine": "Town",
    "Room_Charm_Shop": "Crossroads_04",
    "Room_Mender_House": "Crossroads_04",
    "Room_ruinhouse": "Crossroads_04",
    "Crossroads_ShamanTemple": "Crossroads_06",
    "Room_temple": "Crossroads_02",
    "Room_Final_Boss_Atrium": "Crossroads_02",
    "Room_Mansion": "RestingGrounds_05",
    "Dream_Nailcollection": "RestingGrounds_04",
    "Room_Colosseum_01": "Deepnest_East_09",
    "Room_Colosseum_02": "Deepnest_East_09",
    "Room_Colosseum_Spectate": "Deepnest_East_09",
    "GG_Lurker": "Deepnest_East_09",
    "Room_Wyrm": "Deepnest_East_12",
    "Room_nailsmith": "Ruins1_04",
    "Room_Queen": "Fungus3_48",
    "Room_Mask_Maker": "Deepnest_35",
    "Room_Slug_Shrine": "Fungus1_26",
    "Room_Fungus_Shaman": "Fungus3_26",
    "Room_Nailmaster": "Cliffs_02",
    "Room_Nailmaster_02": "Fungus1_15",
    "Room_Nailmaster_03": "Deepnest_East_06",
    "Room_GG_Shortcut": "Waterways_09",
    "GG_Waterways": "Waterways_09",
    "GG_Pipeway": "Waterways_08",
    "Room_spider_small": "Deepnest_10",
    "Deepnest_Spider_Town": "Deepnest_10",
    "Ruins_House_01": "Ruins2_04",
    "Ruins_House_02": "Ruins2_04",
    "Ruins_House_03": "Ruins2_04",
    "Ruins_Elevator": "Ruins2_04",
    "Ruins_Bathhouse": "Ruins2_04",
    "Ruins2_Watcher_Room": "Ruins2_03",
    "Fungus3_archive": "Fungus3_47",
    "Fungus3_archive_02": "Fungus3_47",
    "Room_Final_Boss_Core": "Crossroads_02",
    "Dream_01_False_Knight": "Crossroads_10",
    "Dream_02_Mage_Lord": "Ruins1_24",
    "Dream_03_Infected_Knight": "Abyss_19",
    "Dream_04_White_Defender": "Waterways_15",
    "Dream_Mighty_Zote": "Room_Bretta",
    "Room_Colosseum_Bronze": "Deepnest_East_09",
    "Room_Colosseum_Silver": "Deepnest_East_09",
    "Room_Colosseum_Gold": "Deepnest_East_09",
    "Abyss_06_Core": "Abyss_06_Core",
    "Abyss_15": "Abyss_06_Core",
    "Abyss_16": "Abyss_06_Core",
    "Abyss_Lighthouse_room": "Abyss_09",
    "Cliffs_03": "Cliffs_02",
    "Deepnest_45_v02": "Deepnest_40",
    "Deepnest_East_17": "Deepnest_East_07",
    "Fungus1_35": "Fungus1_26",
    "Fungus1_36": "Fungus1_21",
    "Mines_35": "Mines_34",
    "RestingGrounds_07": "RestingGrounds_05",
    "Room_Tram": "Crossroads_46",
    "Room_Tram_RG": "Crossroads_46b",
    "Room_Mantis_Lord": "Fungus2_15",
    "Room_nailmaster": "Cliffs_02",
    "Room_nailmaster_02": "Fungus1_15",
    "Room_nailmaster_03": "Deepnest_East_06",
    "Dream_Mighty_Zote": "Town",
    "Hive_01": "Hive_01",
    "Hive_02": "Hive_02",
    "Hive_03": "Hive_03",
    "Hive_04": "Hive_04",
    "Hive_05": "Hive_05",
    "Fungus1_35": "Fungus1_35",
    "Fungus1_36": "Fungus1_36",
    "Fungus2_25": "Fungus2_25",
    "Fungus3_44": "Fungus3_44",
    "Fungus3_49": "Fungus3_49",
    "Deepnest_45_v02": "Deepnest_45_v02",
    "Deepnest_East_14b": "Deepnest_East_14b",
    "Waterways_13": "Waterways_13",
    "Ruins1_31b": "Ruins1_31b",
    "Ruins2_11_b": "Ruins2_11_b",
}

# Proportional room layout for White Palace & Path of Pain (no vanilla Game_Map exists).
# Coordinates are in HK map units (Y-up, matching Game_Map), with room proportions derived
# from the actual scene geometry and connected along their real doorway transitions.
WHITE_PALACE_ROOMS = [
    # Entry & lower wing
    {"id": "White_Palace_11", "label": "Palace Entry", "x": -1.45, "y": -3.20, "w": 1.65, "h": 0.72, "group": "wp"},
    {"id": "White_Palace_01", "label": "Entrance Hall", "x": 0.35, "y": -3.15, "w": 1.50, "h": 0.85, "group": "wp"},
    {"id": "White_Palace_02", "label": "Entrance Wing", "x": 2.05, "y": -3.20, "w": 1.45, "h": 0.62, "group": "wp"},
    # Central hub & left/right wings
    {"id": "White_Palace_03_hub", "label": "Central Hub", "x": 0.35, "y": -1.75, "w": 1.35, "h": 1.35, "group": "wp"},
    {"id": "White_Palace_04", "label": "Left Wing Lower", "x": -1.65, "y": -2.10, "w": 1.75, "h": 0.78, "group": "wp"},
    {"id": "White_Palace_14", "label": "Left Wing Upper", "x": -1.80, "y": -1.00, "w": 2.00, "h": 0.82, "group": "wp"},
    {"id": "White_Palace_15", "label": "Right Wing Entry", "x": 2.05, "y": -1.95, "w": 1.50, "h": 0.75, "group": "wp"},
    {"id": "White_Palace_05", "label": "Right Wing Sawblades", "x": 3.95, "y": -1.85, "w": 1.85, "h": 0.88, "group": "wp"},
    {"id": "White_Palace_16", "label": "Right Wing Upper", "x": 4.20, "y": -0.70, "w": 1.55, "h": 0.85, "group": "wp"},
    # Central atrium & upper spire
    {"id": "White_Palace_06", "label": "Central Atrium", "x": 0.35, "y": -0.15, "w": 1.70, "h": 0.78, "group": "wp"},
    {"id": "White_Palace_07", "label": "Upper Ascent", "x": 2.05, "y": 0.35, "w": 1.25, "h": 1.35, "group": "wp"},
    {"id": "White_Palace_12", "label": "Upper Corridor", "x": 2.05, "y": 1.85, "w": 1.55, "h": 0.85, "group": "wp"},
    {"id": "White_Palace_13", "label": "Throne Tower", "x": 3.50, "y": 2.85, "w": 1.15, "h": 1.35, "group": "wp"},
    {"id": "White_Palace_08", "label": "Palace Workshop", "x": 5.05, "y": 2.85, "w": 1.35, "h": 0.90, "group": "wp"},
    {"id": "White_Palace_09", "label": "Throne Room", "x": 1.85, "y": 3.20, "w": 1.55, "h": 0.92, "group": "wp"},
    # Path of Pain (branches left from Central Atrium)
    {"id": "White_Palace_18", "label": "PoP: Entrance", "x": -1.80, "y": 0.35, "w": 1.95, "h": 0.82, "group": "pop"},
    {"id": "White_Palace_17", "label": "PoP: Lever Room", "x": -2.40, "y": 1.65, "w": 1.80, "h": 0.85, "group": "pop"},
    {"id": "White_Palace_19", "label": "PoP: Vertical Shaft", "x": -0.75, "y": 1.85, "w": 1.05, "h": 1.45, "group": "pop"},
    {"id": "White_Palace_20", "label": "PoP: Final Gauntlet", "x": -0.55, "y": 3.25, "w": 2.05, "h": 0.88, "group": "pop"},
]

WHITE_PALACE_ROUTES = [
    ["White_Palace_11", "White_Palace_01"],
    ["White_Palace_01", "White_Palace_02"],
    ["White_Palace_01", "White_Palace_03_hub"],
    ["White_Palace_03_hub", "White_Palace_04"],
    ["White_Palace_03_hub", "White_Palace_14"],
    ["White_Palace_04", "White_Palace_14"],
    ["White_Palace_03_hub", "White_Palace_15"],
    ["White_Palace_15", "White_Palace_05"],
    ["White_Palace_05", "White_Palace_16"],
    ["White_Palace_03_hub", "White_Palace_06"],
    ["White_Palace_06", "White_Palace_07"],
    ["White_Palace_07", "White_Palace_12"],
    ["White_Palace_12", "White_Palace_13"],
    ["White_Palace_13", "White_Palace_09"],
    ["White_Palace_13", "White_Palace_08"],
    ["White_Palace_06", "White_Palace_18"],
    ["White_Palace_18", "White_Palace_17"],
    ["White_Palace_17", "White_Palace_19"],
    ["White_Palace_19", "White_Palace_20"],
]


def _should_skip_child(name: str) -> bool:
    if not name:
        return True
    return any(tok in name for tok in SKIP_CHILD_SUBSTRINGS)


def _sprite_bounds_world(sprite, pos_x: float, pos_y: float, scale_x: float, scale_y: float) -> tuple[float, float, float, float]:
    """Return (center_x, center_y, width, height) in map units."""
    rect = sprite.m_Rect
    ppu = sprite.m_PixelsToUnits or 100.0
    w = abs(scale_x) * (rect.width / ppu)
    h = abs(scale_y) * (rect.height / ppu)
    # Pivot is in normalized [0..1] sprite space (0.5, 0.5 is centered).
    pivot = getattr(sprite, "m_Pivot", None)
    if pivot is not None:
        dx = (0.5 - pivot.x) * (rect.width / ppu) * scale_x
        dy = (0.5 - pivot.y) * (rect.height / ppu) * scale_y
        pos_x += dx
        pos_y += dy
    return pos_x, pos_y, w, h


def _r(value: float) -> float:
    return round(float(value), 3)


def extract_hk(steam_common: Path, hk_rooms_path: Path) -> dict:
    hk_data = steam_common / "Hollow Knight" / "hollow_knight_Data"
    env = UnityPy.load(str(hk_data / "resources.assets"))
    go = next(
        obj.read()
        for obj in env.objects
        if obj.type.name == "GameObject" and getattr(obj.read(), "m_Name", "") == "Game_Map"
    )
    root_tr = next(
        c.component.read() for c in go.m_Component if c.component.type.name == "Transform"
    )

    hk_room_meta = {}
    if hk_rooms_path.exists():
        hk_room_meta = json.loads(hk_rooms_path.read_text(encoding="utf-8-sig"))

    rooms: dict[str, dict] = {}
    for ch in root_tr.m_Children:
        area_tr = ch.read()
        area_go = area_tr.m_GameObject.read()
        raw_area = area_go.m_Name
        if raw_area in HK_SKIP_PARENTS or raw_area not in HK_AREA_NAMES:
            continue
        default_area = HK_AREA_NAMES[raw_area]
        ax = area_tr.m_LocalPosition.x
        ay = area_tr.m_LocalPosition.y
        asx = area_tr.m_LocalScale.x
        asy = area_tr.m_LocalScale.y

        for gch in area_tr.m_Children:
            room_tr = gch.read()
            room_go = room_tr.m_GameObject.read()
            name = room_go.m_Name
            if _should_skip_child(name):
                continue
            sr = next(
                (c.component.read() for c in room_go.m_Component if c.component.type.name == "SpriteRenderer"),
                None,
            )
            if sr is None or not sr.m_Sprite.path_id:
                continue
            sprite = sr.m_Sprite.read()
            wx = ax + room_tr.m_LocalPosition.x * asx
            wy = ay + room_tr.m_LocalPosition.y * asy
            sx = asx * room_tr.m_LocalScale.x
            sy = asy * room_tr.m_LocalScale.y
            cx, cy, w, h = _sprite_bounds_world(sprite, wx, wy, sx, sy)
            if w < 0.08 or h < 0.08:
                continue
            canonical_area = hk_room_meta.get(name, {}).get("MapArea") or default_area
            # Keep the largest sprite if a room appears twice.
            prev = rooms.get(name)
            if prev is None or (w * h > prev["w"] * prev["h"]):
                rooms[name] = {
                    "id": name,
                    "area": canonical_area,
                    "x": _r(cx),
                    "y": _r(cy),
                    "w": _r(w),
                    "h": _r(h),
                }

    # Add a compact White Palace zone anchor below Ancient Basin so overview maps can show it.
    basin_rooms = [r for r in rooms.values() if r["area"] == "Ancient Basin"]
    if basin_rooms:
        bx = sum(r["x"] for r in basin_rooms) / len(basin_rooms)
        by = min(r["y"] - r["h"] / 2 for r in basin_rooms) - 2.6
    else:
        bx, by = 5.5, -14.0

    # Place White Palace rooms scaled cleanly below Ancient Basin for the overview too,
    # while keeping full-scale coordinates in the dedicated white_palace submap.
    return {
        "rooms": [rooms[k] for k in sorted(rooms)],
        "interiorToMapRoom": HK_INTERIOR_TO_MAP_ROOM,
        "whitePalaceAnchor": {"x": _r(bx), "y": _r(by)},
        "whitePalace": {
            "rooms": WHITE_PALACE_ROOMS,
            "routes": WHITE_PALACE_ROUTES,
        },
    }


def _ss_area_from_scene(scene_name: str, default_area: str) -> str:
    if scene_name.startswith("Song_Enclave"):
        return "Songclave"
    return default_area


def extract_ss(steam_common: Path) -> dict:
    UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
    aa = (
        steam_common
        / "Hollow Knight Silksong"
        / "Hollow Knight Silksong_Data"
        / "StreamingAssets"
        / "aa"
        / "StandaloneWindows64"
    )
    env = UnityPy.load(
        str(aa / "maps_assets_all.bundle"),
        str(aa / "atlases_assets_assets/sprites/_atlases/hornet_map.spriteatlas.bundle"),
        str(aa / "atlases_assets_assets/sprites/_atlases/hornet_map_patch.spriteatlas.bundle"),
    )

    sprites = {}
    for obj in env.objects:
        if obj.type.name == "Sprite":
            sp = obj.read()
            sprites[obj.path_id] = sp

    go = next(
        obj.read()
        for obj in env.objects
        if obj.type.name == "GameObject" and getattr(obj.read(), "m_Name", "") == "Game_Map_Hornet"
    )
    root_tr = next(
        c.component.read() for c in go.m_Component if c.component.type.name == "Transform"
    )

    rooms: dict[str, dict] = {}
    for ch in root_tr.m_Children:
        area_tr = ch.read()
        area_go = area_tr.m_GameObject.read()
        raw_area = area_go.m_Name
        if raw_area in SS_SKIP_PARENTS or raw_area not in SS_AREA_NAMES:
            continue
        default_area = SS_AREA_NAMES[raw_area]
        ax = area_tr.m_LocalPosition.x
        ay = area_tr.m_LocalPosition.y
        asx = area_tr.m_LocalScale.x
        asy = area_tr.m_LocalScale.y

        for gch in area_tr.m_Children:
            room_tr = gch.read()
            room_go = room_tr.m_GameObject.read()
            name = room_go.m_Name
            if _should_skip_child(name):
                continue
            sr = next(
                (c.component.read() for c in room_go.m_Component if c.component.type.name == "SpriteRenderer"),
                None,
            )
            wx = ax + room_tr.m_LocalPosition.x * asx
            wy = ay + room_tr.m_LocalPosition.y * asy
            sx = asx * room_tr.m_LocalScale.x
            sy = asy * room_tr.m_LocalScale.y

            if sr is not None and sr.m_Sprite.path_id in sprites:
                sprite = sprites[sr.m_Sprite.path_id]
                cx, cy, w, h = _sprite_bounds_world(sprite, wx, wy, sx, sy)
            else:
                # Some unmapped/dust-maze rooms have transforms without sprites.
                if not re.match(r"^[A-Z][a-zA-Z]+_[0-9A-Za-z_]+$", name):
                    continue
                cx, cy, w, h = wx, wy, 0.85, 0.55

            if w < 0.08 or h < 0.08:
                continue

            area = _ss_area_from_scene(name, default_area)
            prev = rooms.get(name)
            if prev is None or (w * h > prev["w"] * prev["h"]):
                rooms[name] = {
                    "id": name,
                    "area": area,
                    "x": _r(cx),
                    "y": _r(cy),
                    "w": _r(w),
                    "h": _r(h),
                }

    return {"rooms": [rooms[k] for k in sorted(rooms)]}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steam-common", type=Path, default=DEFAULT_STEAM_COMMON)
    parser.add_argument("--hk-rooms", type=Path, default=DEFAULT_HK_ROOMS)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    hk = extract_hk(args.steam_common, args.hk_rooms)
    ss = extract_ss(args.steam_common)

    payload = {
        "schemaVersion": 1,
        "HK": hk,
        "SS": ss,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    hk_areas = len({r["area"] for r in hk["rooms"]})
    ss_areas = len({r["area"] for r in ss["rooms"]})
    print(
        f"Extracted {len(hk['rooms'])} HK rooms ({hk_areas} areas), "
        f"{len(hk['whitePalace']['rooms'])} White Palace rooms, "
        f"{len(ss['rooms'])} SS rooms ({ss_areas} areas) -> {args.output}"
    )


if __name__ == "__main__":
    main()

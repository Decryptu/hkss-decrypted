#!/usr/bin/env python3
"""
Exports the areas that cross-game seals can close, with the game scenes in each area, to
apworld/hkss/cross_game_areas.json.

Hollow Knight scenes come from RandomizerMod's rooms.json (MapArea of each scene). Silksong
scenes come from the vendored world's map_logic.json (in_game_id of each room in the room
graph). Run with --check to verify the checked-in file.
"""
from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
APWORLD = REPO_ROOT / "apworld" / "hkss"
OUTPUT = APWORLD / "cross_game_areas.json"
DEFAULT_HK_ROOMS = REPO_ROOT / "local/reference/hk-randomizermod/RandomizerMod/Resources/Data/rooms.json"

# Areas left out on purpose: the starting areas and travel gate towns (King's Pass, Dirtmouth,
# Moss Grotto, Bone Bottom), the hubs every early route crosses (Forgotten Crossroads,
# The Marrow, Bellhart), areas entered by warps or menus rather than doorways (White Palace,
# fast travel, wish walls, Wisp Thicket), and Silksong's Act 3 goal areas.
HK_SEALABLE = (
    "Ancient Basin",
    "City of Tears",
    "Crystal Peak",
    "Deepnest",
    "Fog Canyon",
    "Fungal Wastes",
    "Greenpath",
    "Howling Cliffs",
    "Kingdom's Edge",
    "Queen's Gardens",
    "Resting Grounds",
    "Royal Waterways",
)

SS_SEALABLE = {
    "bilewater": "Bilewater",
    "blasted-steps": "Blasted Steps",
    "cogwork-core": "Cogwork Core",
    "deep-docks": "Deep Docks",
    "far-fields": "Far Fields",
    "greymoor": "Greymoor",
    "high-halls": "High Halls",
    "hunter-s-march": "Hunter's March",
    "mount-fay": "Mount Fay",
    "putrified-ducts": "Putrified Ducts",
    "sands-of-karak": "Sands of Karak",
    "shellwood": "Shellwood",
    "sinner-s-road": "Sinner's Road",
    "underworks": "Underworks",
    "whispering-vaults": "Whispering Vaults",
    "wormways": "Wormways",
}


def build(hk_rooms_path: Path) -> dict:
    hk_rooms = json.loads(hk_rooms_path.read_text(encoding="utf-8-sig"))
    hk_areas: dict[str, list[str]] = defaultdict(list)
    for scene, row in hk_rooms.items():
        if row.get("MapArea") in HK_SEALABLE:
            hk_areas[row["MapArea"]].append(scene)

    graph = json.loads((APWORLD / "silksong" / "room_graph_data.json").read_text(encoding="utf-8"))
    room_area = {room["id"]: room["area_id"] for room in graph["rooms"]}
    geometry = json.loads((APWORLD / "silksong" / "map_logic.json").read_text(encoding="utf-8"))
    ss_scenes: dict[str, set[str]] = defaultdict(set)
    for room in geometry["room_geometry"]["rooms"]:
        area_id = room_area.get(room["id"])
        if area_id in SS_SEALABLE and room.get("in_game_id"):
            ss_scenes[area_id].add(room["in_game_id"])

    missing = [a for a in HK_SEALABLE if not hk_areas.get(a)] + [a for a in SS_SEALABLE if not ss_scenes.get(a)]
    if missing:
        raise SystemExit(f"Areas without scenes: {missing}")

    return {
        "hollow_knight": {area: sorted(hk_areas[area]) for area in HK_SEALABLE},
        "silksong": {
            name: {"area_id": area_id, "scenes": sorted(ss_scenes[area_id])}
            for area_id, name in SS_SEALABLE.items()
        },
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--hk-rooms", type=Path, default=DEFAULT_HK_ROOMS)
    parser.add_argument("--check", action="store_true", help="Verify the checked-in file")
    args = parser.parse_args()
    text = json.dumps(build(args.hk_rooms), indent=1, ensure_ascii=False) + "\n"
    if args.check:
        if OUTPUT.read_text(encoding="utf-8") != text:
            raise SystemExit(f"{OUTPUT.relative_to(REPO_ROOT)} is out of date. Run the exporter.")
    else:
        OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    verb = "Checked" if args.check else "Wrote"
    print(f"{verb} {OUTPUT.relative_to(REPO_ROOT)}: {len(HK_SEALABLE)} HK areas, {len(SS_SEALABLE)} SS areas")


if __name__ == "__main__":
    main()

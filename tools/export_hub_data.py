"""Export the player hub catalog from a packaged HKSS Archipelago world."""

from __future__ import annotations

import argparse
import hashlib
import inspect
import json
import logging
import sys
import types
from collections import defaultdict
from pathlib import Path
from zipfile import ZipFile

from test_apworld import DEFAULT_AP_DIR, DEFAULT_APWORLD, REPO_ROOT


DEFAULT_ROOMS = (
    REPO_ROOT / "local/reference/hk-randomizermod/RandomizerMod/Resources/Data/rooms.json"
)
OUTPUT = REPO_ROOT / "site/world.json"

GAME_CATEGORIES = {
    "General & goals": "General/Goal Options",
    "Starting options": "Starting Options",
    "Logic & skips": "Proficiency/Skip Options",
    "Randomization": "Randomization Options",
    "Split & directional abilities": "Split/Directional Abilities",
    "Shops & costs": "Costsanity",
    "Links": "Link Options",
    "Miscellaneous": "Miscellaneous",
}

HK_CATEGORY_OPTIONS = {
    "General & goals": ("Goal", "GrubHuntGoal", "WhitePalace"),
    "Starting options": ("StartLocation", "StartingGeo"),
    "Logic & skips": (
        "PreciseMovement", "ProficientCombat", "BackgroundObjectPogos", "EnemyPogos",
        "ObscureSkips", "ShadeSkips", "InfectionSkips", "FireballSkips", "SpikeTunnels",
        "AcidSkips", "DamageBoosts", "DangerousSkips", "DarkRooms", "ComplexSkips",
        "DifficultSkips", "ExtraPlatforms",
    ),
    "Split & directional abilities": (
        "RandomizeSwim", "SplitCrystalHeart", "SplitMothwingCloak", "SplitMantisClaw",
    ),
    "Links": ("DeathLinkShade", "DeathLinkBreaksFragileCharms"),
    "Miscellaneous": ("RemoveSpellUpgrades", "AddUnshuffledLocations"),
}


def hk_category(key):
    for category, keys in HK_CATEGORY_OPTIONS.items():
        if key in keys:
            return category
    if key.startswith("CostSanity") or key.endswith(("Price", "ShopSlots", "RewardSlots", "CharmCosts")):
        return "Shops & costs"
    if key.startswith("Randomize"):
        return "Randomization"
    return "Miscellaneous"


def hub_option_groups(ordered_groups):
    """Use the same headings and order for both games in the player hub."""
    common = ordered_groups.get("Item & Location Options", {})
    shared = dict(ordered_groups.get("Game Options", {}))
    shared_keys = {"progression_balancing"}
    hk = defaultdict(dict)
    for key, option in ordered_groups["Hollow Knight"].items():
        hk[hk_category(key)][key] = option

    silksong = {}
    for name, options in ordered_groups.items():
        if name.startswith("Silksong: "):
            shared.update((key, option) for key, option in options.items() if key in shared_keys)
            silksong[name.removeprefix("Silksong: ")] = {
                key: option for key, option in options.items() if key not in common and key not in shared_keys
            }
    yield "Combo", ordered_groups["Combo"]
    yield "Shared game options", shared
    for name, options in ordered_groups.items():
        if name not in {"Combo", "Game Options", "Hollow Knight", "Item & Location Options"} and not name.startswith("Silksong: "):
            yield name, options
    for name in GAME_CATEGORIES:
        yield f"Hollow Knight: {name}", hk[name]
    for name, upstream in GAME_CATEGORIES.items():
        yield f"Silksong: {name}", silksong.pop(upstream, {})
    for name, options in silksong.items():
        yield f"Silksong: {name}", options
    yield "Item & Location Options", common


def option_schema(option, ap):
    default = option.default
    result = {
        "label": option.display_name,
        "help": inspect.cleandoc(option.__doc__ or ""),
    }
    if issubclass(option, ap.Toggle):
        result.update(kind="toggle", default=bool(default))
    elif issubclass(option, ap.Choice):
        result.update(
            kind="textChoice" if issubclass(option, ap.TextChoice) else "choice",
            default=option.name_lookup.get(default, default),
            choices=list(option.name_lookup.values()),
            aliases=sorted(set(option.options) - set(option.name_lookup.values())),
            numbers=list(option.name_lookup),
        )
    elif issubclass(option, ap.Range):
        result.update(kind="range", default=default, min=option.range_start, max=option.range_end,
                      names=getattr(option, "special_range_names", {}))
    elif issubclass(option, (ap.OptionDict, ap.OptionList, ap.OptionSet)):
        kind = "mapping" if issubclass(option, ap.OptionDict) else "list"
        result.update(kind=kind, default=sorted(default) if isinstance(default, (set, frozenset)) else default)
        if getattr(option, "valid_keys", None):
            result["keys"] = sorted(option.valid_keys)
            result["casefold"] = option.valid_keys_casefold
        if option.verify_item_name:
            result["names"] = "items"
        if option.verify_location_name:
            result["names"] = "locations"
        if issubclass(option, ap.OptionCounter):
            result["counts"] = True
    elif issubclass(option, ap.FreeText):
        result.update(kind="text", default=default)
    elif issubclass(option, ap.PlandoItems):
        result.update(kind="list", default=default)
    else:
        raise TypeError(f"Unsupported option: {option.__name__}")
    return result


def export(archive: Path, ap_dir: Path, rooms_path: Path) -> dict:
    with ZipFile(archive) as package:
        release = json.loads(package.read("hkss/archipelago.json"))

    # Use the installed generation libraries without running the installer's updater.
    module_update = types.ModuleType("ModuleUpdate")
    module_update.update = lambda *args, **kwargs: None
    sys.modules["ModuleUpdate"] = module_update
    sys.modules.setdefault("bsdiff4", types.ModuleType("bsdiff4"))
    lib = ap_dir / "lib"
    sys.path[:0] = [str(archive), str(lib / "library.zip"), str(lib)]

    # The installer includes unrelated worlds whose native extensions may be unavailable.
    previous_logging = logging.root.manager.disable
    logging.disable(logging.CRITICAL)
    try:
        import Options as ap
        from hkss import HKSSWorld
    finally:
        logging.disable(previous_logging)
    from hkss.hk.ExtractedData import connectors, location_to_region_lookup, multi_locations
    from hkss.hk.Options import hollow_knight_randomize_options
    from hkss.silksong.enemy_souls import CATALOGUE
    from hkss.silksong.locations import location_data_table
    from hkss.silksong.room_graph import load_room_graph
    from hkss.silksong.vog_hints import _room_area, get_vog_hint_areas

    rooms = json.loads(rooms_path.read_text(encoding="utf-8-sig"))

    def hk_area(room):
        return rooms.get(room, {}).get("MapArea")

    def routes(pairs):
        return sorted({tuple(sorted((source, target))) for source, target in pairs
                       if source and target and source != target})

    hk_routes = routes(
        (hk_area(source.partition("[")[0]), hk_area(target.partition("[")[0]))
        for source, target in connectors.items() if target
    )
    ss_rooms = load_room_graph().rooms
    ss_routes = routes(
        (_room_area(room.id), _room_area(transition.target.room_id))
        for room in ss_rooms for transition in room.transitions
        if transition.target.room_id and transition.is_compilable
    )
    ss_check_areas = defaultdict(set)
    for room in ss_rooms:
        area = _room_area(room.id)
        if area:
            for check in room.checks:
                if check.canonical_location:
                    ss_check_areas[check.canonical_location].add(area)
    hk_types = defaultdict(set)
    for option in hollow_knight_randomize_options.values():
        for name in option.locations:
            hk_types[name].add(option.display_name.removeprefix("Randomize "))

    first_kill_areas = {
        f"First Kill: {row['name']}": {
            area for route in row["routes"] for node in route["nodes"]
            if (area := _room_area(node))
        }
        for row in CATALOGUE
    }
    ss_areas = {
        name.removeprefix("SS "): get_vog_hint_areas(name.removeprefix("SS "))
        for name in HKSSWorld.location_name_to_id if name.startswith("SS ")
    }
    area_names = {area for areas in ss_areas.values() for area in areas}

    checks = []
    for name, code in sorted(HKSSWorld.location_name_to_id.items()):
        game, raw = name.split(" ", 1)
        if game == "HK":
            base = raw.removeprefix("Split_").removeprefix("Left_").removeprefix("Right_")
            room = location_to_region_lookup.get(raw) or location_to_region_lookup.get(base)
            areas = [hk_area(room) or "Unmapped"]
            check_types = hk_types.get(raw) or hk_types.get(base) or {"Other"}
            shop, _, number = raw.rpartition("_")
            if number.isdigit() and shop in multi_locations:
                check_types = {"Shop"}
            label = raw.replace("_", " ")
        else:
            area_prefix = raw.partition(" - ")[0]
            named_area = {area_prefix} if area_prefix in area_names else set()
            areas = ss_areas[raw] or first_kill_areas.get(raw) or ss_check_areas.get(raw) or named_area or {"Unmapped"}
            metadata = location_data_table.get(raw)
            check_types = {
                metadata.category if metadata else
                "First Kill" if raw.startswith("First Kill: ") else "Other"
            }
            room = None
            label = raw
        checks.append({
            "id": code, "key": name, "game": game, "name": label,
            "areas": sorted(areas), "types": sorted(check_types), **({"room": room} if room else {}),
        })

    groups = []
    for name, options in hub_option_groups(ap.get_option_groups(HKSSWorld)):
        if options:
            groups.append({
                "name": name,
                "options": {key: option_schema(option, ap) for key, option in options.items()},
            })
    return {
        "game": HKSSWorld.game,
        "version": release["world_version"],
        "minimumArchipelago": release["minimum_ap_version"],
        "archiveSha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
        "routes": {"HK": hk_routes, "SS": ss_routes},
        "groups": groups,
        "names": {
            "items": sorted(HKSSWorld.item_name_to_id.keys() | HKSSWorld.item_name_groups.keys()),
            "locations": sorted(HKSSWorld.location_name_to_id.keys() | HKSSWorld.location_name_groups.keys()),
        },
        "checks": checks,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apworld", type=Path, default=DEFAULT_APWORLD)
    parser.add_argument("--ap-dir", type=Path, default=DEFAULT_AP_DIR)
    parser.add_argument("--hk-rooms", type=Path, default=DEFAULT_ROOMS)
    parser.add_argument("--check", action="store_true", help="Verify the checked-in catalog")
    args = parser.parse_args()
    data = export(args.apworld.resolve(), args.ap_dir.resolve(), args.hk_rooms)
    # Keep each check on its own line so changes to the catalog are easy to review.
    checks = data.pop("checks")
    catalog = json.dumps(data, ensure_ascii=False, indent=2).removesuffix("\n}")
    rows = ",\n".join(f"    {json.dumps(row, ensure_ascii=False)}" for row in checks)
    catalog += ',\n  "checks": [\n' + rows + "\n  ]\n}\n"
    if args.check:
        if OUTPUT.read_text(encoding="utf-8") != catalog:
            raise SystemExit("site/world.json differs from the supplied world. Run the exporter.")
    else:
        OUTPUT.write_text(catalog, encoding="utf-8", newline="\n")
    print(f"World {data['version']}: {sum(len(group['options']) for group in data['groups'])} options, "
          f"{len(checks)} checks")


if __name__ == "__main__":
    main()

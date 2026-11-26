"""Logic data for the HKSS PopTracker pack, exported from the packaged Archipelago world.

Hollow Knight: the rule graph the in-game check map uses (src/HKSS.Core/Data/hk_logic.json),
plus the vanilla placements that the world turns into events for options left unrandomized.
Silksong: the bundled map logic; each slot's own overrides arrive in slot_data at connect.
"""

from __future__ import annotations

import json
import types
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
HK_LOGIC = REPO_ROOT / "src/HKSS.Core/Data/hk_logic.json"


def hk_data(world=None) -> dict:
    """Return the Hollow Knight logic tables. Call after export_hub_data.load_world()."""
    from hkss.hk import HKWorld, logicless_options
    from hkss.hk.ExtractedData import starts, vanilla_location_costs, vanilla_shop_costs
    from hkss.hk.Options import WhitePalace, hollow_knight_options, hollow_knight_randomize_options

    logic = json.loads(HK_LOGIC.read_text(encoding="utf-8"))

    vanilla = []
    for option_key, option in hollow_knight_randomize_options.items():
        for item, location in zip(option.items, option.locations):
            if location == "Start":
                continue  # Starting items are handled by the evaluator.
            vanilla.append({"o": option_key, "i": item, "l": location})

    # White Palace exclusions make randomized locations vanilla; they depend on two options.
    white_palace = {}
    for wp in (WhitePalace.option_exclude, WhitePalace.option_kingfragment,
               WhitePalace.option_nopathofpain, WhitePalace.option_include):
        for charms in (0, 1):
            fake = types.SimpleNamespace(options=types.SimpleNamespace(WhitePalace=wp, RandomizeCharms=charms))
            white_palace[f"{wp}_{charms}"] = sorted(HKWorld.white_palace_exclusions(fake))

    shop_costs = {f"{shop}|{item}": costs for (shop, item), costs in vanilla_shop_costs.items()}

    # Option defaults of the combined world, for evaluating logic before a slot is connected.
    defaults = {}
    hints = world.options_dataclass.type_hints if world is not None else {}
    for name in hollow_knight_options:
        option = hints.get(name, hollow_knight_options[name])
        try:
            defaults[name] = int(option.default)
        except (TypeError, ValueError):
            pass
    return {
        "connectors": logic["connectors"],
        "itemEffects": logic["itemEffects"],
        "locationRules": logic["locationRules"],
        "nodeRules": logic["nodeRules"],
        "nodeOrder": list(logic["nodeRules"]),
        "start": starts["king's_pass"],
        "vanilla": vanilla,
        "logicless": sorted(logicless_options),
        "whitePalace": white_palace,
        "shopCosts": shop_costs,
        "locationCosts": vanilla_location_costs,
        "defaultOptions": defaults,
    }


def ss_data(world) -> tuple[dict, dict]:
    """Return (Silksong logic tables, constants). Call after export_hub_data.load_world()."""
    from hkss.silksong.display_names import clean_item_display_name
    from hkss.silksong.map_logic_data import load_base
    from hkss.silksong.requirements import CLOAK_REQUIRED_SKILL_ITEMS, CREST_ITEMS, NON_ARCHITECT_CREST_ITEMS

    identity, data = load_base()
    logic = data["logic"]

    # Names the world cleans before counting items (numbered shards, named fleas, ...).
    names = {name[3:] for name in world.item_name_to_id if name.startswith("SS ")}

    def collect(value):
        if isinstance(value, dict):
            for key, child in value.items():
                if key in ("all_of", "any_of", "items"):
                    names.update(child)
                elif key == "item":
                    names.add(child)
                else:
                    collect(child)
        elif isinstance(value, list):
            for child in value:
                collect(child)

    collect(logic)
    collect(data["entrance_profiles"])
    clean_names = {name: clean_item_display_name(name) for name in sorted(names)
                   if clean_item_display_name(name) != name}
    constants = {
        "crests": sorted(CREST_ITEMS),
        "non_architect_crests": sorted(NON_ARCHITECT_CREST_ITEMS),
        "cloak_required": sorted(CLOAK_REQUIRED_SKILL_ITEMS),
        "clean_names": clean_names,
    }
    tables = {"identity": identity, "base": logic, "profiles": data["entrance_profiles"]}
    return tables, constants


def seal_data(world) -> dict:
    """Scenes and areas that cross-game seals use. Call after export_hub_data.load_world().

    hkLocationScenes: Hollow Knight checks in sealable scenes, by location name without "HK ".
    ssEventAreas: Silksong room events in sealable areas, by logic name ("Room Event: ...").
    """
    from hkss.cross_game import hk_scene, load_areas, ss_event_areas

    areas = load_areas()
    hk_scenes = {scene for scenes in areas["hollow_knight"].values() for scene in scenes}
    ss_areas = {data["area_id"] for data in areas["silksong"].values()}
    location_scenes = {}
    for name in world.location_name_to_id:
        if name.startswith("HK "):
            scene = hk_scene(name, hk_scenes)
            if scene in hk_scenes:
                location_scenes[name[3:]] = scene
    event_areas = {name: area for name, area in ss_event_areas().items() if area in ss_areas}
    return {
        "hkLocationScenes": dict(sorted(location_scenes.items())),
        "ssEventAreas": dict(sorted(event_areas.items())),
    }


def to_lua(value) -> str:
    """Serialize JSON-like data as a Lua table constructor."""
    if value is None:
        return "nil"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return repr(value)
    if isinstance(value, str):
        escaped = (value.replace("\\", "\\\\").replace('"', '\\"')
                   .replace("\n", "\n").replace("\r", "\r"))
        return f'"{escaped}"'
    if isinstance(value, (list, tuple)):
        return "{" + ",".join(to_lua(item) for item in value) + "}"
    if isinstance(value, dict):
        parts = []
        for key, item in value.items():
            parts.append(f"[{to_lua(key)}]={to_lua(item)}")
        return "{" + ",".join(parts) + "}"
    raise TypeError(f"Cannot write {type(value).__name__} as Lua")

"""Cross-game seals: areas in one game that stay closed until a key ability from the other game is found.

Each seal names an area, the game scenes in it, and one progression item from the other game.
Logic is strict: every route into a sealed area, and every check and event inside it, needs the
key. In game, doorways into a sealed area send the player back until the key has been received.
"""

from __future__ import annotations

import json
import pkgutil
from dataclasses import dataclass
from typing import TYPE_CHECKING, Any, Callable, Iterable

from .ids import HK_PREFIX, SS_PREFIX

if TYPE_CHECKING:
    from BaseClasses import CollectionState, Location
    from . import HKSSWorld

HOLLOW_KNIGHT = "hollow_knight"
SILKSONG = "silksong"

# Candidate key abilities. Only items in this slot's item pool are used, so split and directional
# variants are listed next to the whole ability.
HK_KEYS: tuple[str, ...] = (
    "Mothwing_Cloak", "Left_Mothwing_Cloak", "Right_Mothwing_Cloak",
    "Mantis_Claw", "Left_Mantis_Claw", "Right_Mantis_Claw",
    "Crystal_Heart", "Left_Crystal_Heart", "Right_Crystal_Heart",
    "Monarch_Wings", "Isma's_Tear", "Shade_Cloak",
    "Desolate_Dive", "Vengeful_Spirit", "Howling_Wraiths",
    "Dream_Nail", "Lumafly_Lantern",
)
SS_KEYS: tuple[str, ...] = (
    "Swift Step", "Swift Step (Left)", "Swift Step (Right)", "Progressive Swift Step",
    "Cling Grip", "Cling Grip (Left)", "Cling Grip (Right)",
    "Clawline", "Clawline (Left)", "Clawline (Right)",
    "Faydown Cloak", "Silk Soar", "Drifter's Cloak", "Needolin",
    "Needle Strike", "Silkspear", "Thread Storm", "Cross Stitch",
)

HK_WORLD_NAME = "Hallownest"
SS_WORLD_NAME = "Pharloom"


@dataclass(frozen=True)
class Seal:
    game: str
    area: str
    scenes: frozenset[str]
    key: str
    area_id: str = ""

    def slot_data(self, key_id: int) -> dict[str, Any]:
        return {
            "game": self.game,
            "area": self.area,
            "scenes": sorted(self.scenes),
            "key": self.key,
            "key_id": key_id,
            **({"area_id": self.area_id} if self.area_id else {}),
        }


def load_areas() -> dict[str, Any]:
    raw = pkgutil.get_data(__name__.rpartition(".")[0], "cross_game_areas.json")
    if raw is None:
        raise FileNotFoundError("cross_game_areas.json is missing from the world package")
    return json.loads(raw.decode("utf-8"))


def _hk_scene_of(name: str, scenes: Iterable[str]) -> str | None:
    """Scene for a Hollow Knight logic name: a transition (Scene[gate]) or a waypoint named after a scene."""
    scene = name.partition("[")[0]
    if scene in scenes:
        return scene
    best = None
    for candidate in scenes:
        if name.startswith(candidate + "_") and (best is None or len(candidate) > len(best)):
            best = candidate
    return best


def hk_scene(name: str, scenes: Iterable[str]) -> str | None:
    """Scene of a Hollow Knight location, event or transition name (with or without its prefix)."""
    from .hk.ExtractedData import location_to_region_lookup

    raw = name[len(HK_PREFIX):] if name.startswith(HK_PREFIX) else name
    scene = location_to_region_lookup.get(raw)
    if scene is None:
        base = raw.removeprefix("Split_").removeprefix("Left_").removeprefix("Right_")
        scene = location_to_region_lookup.get(base)
    return scene if scene is not None else _hk_scene_of(raw, scenes)


def ss_event_areas() -> dict[str, str]:
    """Area id of each Silksong room event region, from the room graph."""
    from .silksong.room_graph import load_room_graph

    return {
        "Room Event: " + event_id: event.node_id.split("/", 1)[0]
        for event_id, event in load_room_graph().event_by_id.items()
        if event.node_id
    }


def ss_area_of(region_name: str, event_areas: dict[str, str]) -> str | None:
    if region_name.startswith("Room Node: "):
        return region_name[len("Room Node: "):].split("/", 1)[0]
    return event_areas.get(region_name)


def _pool_item_names(world: "HKSSWorld") -> set[str]:
    return {
        item.name for item in world.multiworld.itempool
        if item.player == world.player and item.advancement
    }


def _hk_start_scene(world: "HKSSWorld") -> str | None:
    from .hk.ExtractedData import starts

    start = starts.get(world.options.StartLocation.current_key)
    return start.partition("[")[0] if isinstance(start, str) else None


def _town_reachable_without_scenes(world: "HKSSWorld", blocked_scenes: frozenset[str]) -> bool:
    """True when Hollow Knight can still reach Dirtmouth (Town) using only HK items without entering blocked_scenes."""
    from BaseClasses import CollectionState
    from .cross_game_items import is_cross_game_item
    from .hk.ExtractedData import connectors

    player = world.player
    menu = world.multiworld.get_region("Menu", player)
    state = CollectionState(world.multiworld)
    for item in world.multiworld.itempool:
        if item.player == player and item.advancement and (
            item.name.startswith(HK_PREFIX) or is_cross_game_item(item.name)
        ):
            state.collect(item, True)

    all_blocked = tuple(blocked_scenes)
    events = []
    for loc in menu.locations:
        if loc.address is not None or loc.name.startswith(SS_PREFIX):
            continue
        if all_blocked:
            src_scene = hk_scene(loc.name, all_blocked)
            if src_scene in blocked_scenes:
                continue
            if loc.name in connectors and loc.item is not None:
                tgt_scene = hk_scene(loc.item.name, all_blocked)
                if tgt_scene in blocked_scenes:
                    continue
        events.append(loc)

    state.sweep_for_advancements(locations=events)
    return state.has("Town", player)


def choose_seals(world: "HKSSWorld", count: int) -> list[Seal]:
    """Picks up to `count` sealed areas per game, each with a different key from the other game's pool."""
    if count <= 0 or not (world.has_hk and world.has_silksong):
        return []
    areas = load_areas()
    pool = _pool_item_names(world)
    rng = world.random

    hk_keys = [HK_PREFIX + name for name in HK_KEYS if HK_PREFIX + name in pool]
    ss_keys = [SS_PREFIX + name for name in SS_KEYS if SS_PREFIX + name in pool]

    hk_start = _hk_start_scene(world)
    hk_areas = [
        area for area, scenes in areas[HOLLOW_KNIGHT].items()
        if hk_start not in scenes
    ]
    ss_areas = list(areas[SILKSONG].items())

    seals: list[Seal] = []
    rng.shuffle(hk_areas)
    rng.shuffle(ss_keys)
    sealed_hk_scenes: set[str] = set()
    ss_key_iter = iter(ss_keys)
    for area in hk_areas:
        if len(seals) >= count:
            break
        area_scenes = frozenset(areas[HOLLOW_KNIGHT][area])
        candidate_blocked = frozenset(sealed_hk_scenes | area_scenes)
        if not _town_reachable_without_scenes(world, candidate_blocked):
            continue
        key = next(ss_key_iter, None)
        if key is None:
            break
        sealed_hk_scenes.update(area_scenes)
        seals.append(Seal(HOLLOW_KNIGHT, area, area_scenes, key))

    rng.shuffle(ss_areas)
    rng.shuffle(hk_keys)
    for (area, data), key in zip(ss_areas[:count], hk_keys):
        seals.append(Seal(SILKSONG, area, frozenset(data["scenes"]), key, data["area_id"]))
    return seals


def _wrap(spot: Any, key: str, player: int) -> None:
    old = spot.access_rule
    spot.access_rule = lambda state, _old=old, _key=key, _p=player: state.has(_key, _p) and _old(state)


def apply_hk_seals(world: "HKSSWorld", seals: list[Seal]) -> None:
    """Every Hollow Knight check, event and transition inside a sealed area needs that seal's key.

    Transition events also need the key when they lead into a sealed area. The target is read when
    the rule runs, so shuffled entrances are covered.
    """
    hk_seals = [seal for seal in seals if seal.game == HOLLOW_KNIGHT]
    if not hk_seals:
        return
    from .hk.ExtractedData import connectors

    scene_key = {scene: seal.key for seal in hk_seals for scene in seal.scenes}
    all_scenes = tuple(scene_key)
    player = world.player
    key_cache: dict[str, str | None] = {}

    def key_for(name: str) -> str | None:
        if name in key_cache:
            return key_cache[name]
        scene = hk_scene(name, all_scenes)
        result = scene_key.get(scene) if scene else None
        key_cache[name] = result
        return result

    for location in world.multiworld.get_locations(player):
        if location.name.startswith(SS_PREFIX):
            continue
        if location.address is None and location.name in connectors:
            _wrap_transition(location, key_for, player)
            continue
        key = key_for(location.name)
        if key:
            _wrap(location, key, player)


def _wrap_transition(location: "Location", key_for: Callable[[str], str | None], player: int) -> None:
    old = location.access_rule
    source_key = key_for(location.name)

    def rule(state: "CollectionState") -> bool:
        if source_key and not state.has(source_key, player):
            return False
        target = location.item.name if location.item else None
        target_key = key_for(target) if target else None
        if target_key and not state.has(target_key, player):
            return False
        return old(state)

    location.access_rule = rule


def apply_ss_seals(world: "HKSSWorld", seals: list[Seal]) -> None:
    """Every Silksong entrance from outside a sealed area into it needs that seal's key."""
    ss_seals = [seal for seal in seals if seal.game == SILKSONG]
    if not ss_seals:
        return
    area_key = {seal.area_id: seal.key for seal in ss_seals}
    event_areas = ss_event_areas()

    def area_of(region_name: str) -> str | None:
        return ss_area_of(region_name, event_areas)

    player = world.player
    for region in world.multiworld.get_regions(player):
        target_area = area_of(region.name)
        key = area_key.get(target_area) if target_area else None
        if not key:
            continue
        for entrance in region.entrances:
            source = entrance.parent_region
            if source is not None and area_of(source.name) == target_area:
                continue
            _wrap(entrance, key, player)


def seal_key_items(world: "HKSSWorld", seals: list[Seal]) -> list[Any]:
    return [world.create_item(seal.key) for seal in seals]


def describe(seal: Seal) -> str:
    home = HK_WORLD_NAME if seal.game == HOLLOW_KNIGHT else SS_WORLD_NAME
    key_world = SS_WORLD_NAME if seal.game == HOLLOW_KNIGHT else HK_WORLD_NAME
    key = seal.key[3:].replace("_", " ") if seal.key.startswith(HK_PREFIX) else seal.key[3:]
    return f"{seal.area} ({home}) needs {key} from {key_world}"

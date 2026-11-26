"""Coupled entrance and room randomization for Hollow Knight in HKSS combo mode."""

from __future__ import annotations

from collections import deque
from typing import TYPE_CHECKING

from .hk.ExtractedData import connectors as VANILLA_CONNECTORS

if TYPE_CHECKING:
    from random import Random

# Matches HkSlotData.EntranceRandoType in HKSS.HollowKnight.
ENTRANCE_RANDO_TYPE_NONE = 0
ENTRANCE_RANDO_TYPE_MAP_AREA = 1
ENTRANCE_RANDO_TYPE_ROOM = 3
ENTRANCE_RANDO_TYPE_DOORS = 5

SCOPE_TO_RANDO_TYPE = {
    "interiors": ENTRANCE_RANDO_TYPE_DOORS,
    "within_areas": ENTRANCE_RANDO_TYPE_MAP_AREA,
    "full": ENTRANCE_RANDO_TYPE_ROOM,
}

# Scenes and transitions kept vanilla so starting routes, conditional town doors,
# White Palace, Grimm tents, and goal/special rooms never softlock.
_EXCLUDED_SCENES = frozenset({
    "Tutorial_01",
    "Grimm_Divine",
    "Grimm_Main_Tent",
    "Room_temple",
    "Room_shop",
    "Room_Bretta",
    "Room_Ouiji",
    "Room_Town_Stag_Station",
    "Room_Mender_House",
    "Room_Colosseum_01",
    "Room_Colosseum_02",
    "Room_Colosseum_Spectate",
    "GG_Lurker",
    "Room_GG_Shortcut",
    "GG_Pipeway",
    "GG_Waterways",
    "Ruins_Elevator",
    "Ruins_Bathhouse",
    "Abyss_Lighthouse_room",
    "Room_Wyrm",
})

_EXCLUDED_TRANSITIONS = frozenset({
    "Town[left1]",
    "Town[room_divine]",
    "Town[room_grimm]",
    "Town[door_sly]",
    "Town[door_bretta]",
    "Town[door_jiji]",
    "Town[door_station]",
    "Crossroads_02[door1]",
    "Crossroads_04[door_Mender_House]",
})


def _scene_of(transition: str) -> str:
    return transition.split("[", 1)[0]


def _gate_of(transition: str) -> str:
    return transition.split("[", 1)[1].rstrip("]")


def _is_excluded(transition: str) -> bool:
    scene = _scene_of(transition)
    if scene in _EXCLUDED_SCENES or scene.startswith("White_Palace"):
        return True
    return transition in _EXCLUDED_TRANSITIONS


def _scene_area(scene: str) -> str:
    if scene == "Town" or scene.startswith(("Room_mapper",)):
        return "Town"
    if scene.startswith(("Crossroads_", "Room_Charm_Shop", "Room_ruinhouse")):
        return "Crossroads"
    if scene.startswith(("Fungus1_", "Room_Slug_Shrine", "Room_nailmaster_02")):
        return "Greenpath"
    if scene.startswith(("Fungus2_",)):
        return "FungalWastes"
    if scene.startswith(("Fungus3_", "Room_Fungus_Shaman", "Room_Queen")):
        return "FogAndGardens"
    if scene.startswith(("Deepnest_East_", "Room_nailmaster_03")):
        return "KingdomsEdge"
    if scene.startswith(("Deepnest_", "Room_spider_small", "Room_Mask_Maker")):
        return "Deepnest"
    if scene.startswith(("Hive_",)):
        return "Hive"
    if scene.startswith(("Abyss_",)):
        return "Abyss"
    if scene.startswith(("Waterways_",)):
        return "Waterways"
    if scene.startswith(("Ruins1_", "Ruins2_", "Ruins_House_", "Room_nailsmith")):
        return "CityOfTears"
    if scene.startswith(("RestingGrounds_", "Room_Mansion")):
        return "RestingGrounds"
    if scene.startswith(("Mines_",)):
        return "CrystalPeak"
    if scene.startswith(("Cliffs_", "Room_nailmaster")):
        return "HowlingCliffs"
    return scene


def _classify_pair(a: str, b: str) -> tuple[str, str, str] | None:
    """Return (bucket_kind, first_transition, second_transition) or None if ineligible."""
    if _is_excluded(a) or _is_excluded(b):
        return None
    ga, gb = _gate_of(a), _gate_of(b)
    if ga.startswith("door") and not gb.startswith("door"):
        return ("door", a, b)
    if gb.startswith("door") and not ga.startswith("door"):
        return ("door", b, a)
    if ga.startswith("left") and gb.startswith("right"):
        return ("lr", a, b)
    if ga.startswith("right") and gb.startswith("left"):
        return ("lr", b, a)
    if ga.startswith("top") and gb.startswith("bot"):
        return ("tb", a, b)
    if ga.startswith("bot") and gb.startswith("top"):
        return ("tb", b, a)
    return None


def _collect_buckets(scope: str) -> dict[tuple[str, str], list[tuple[str, str]]]:
    seen: set[tuple[str, str]] = set()
    buckets: dict[tuple[str, str], list[tuple[str, str]]] = {}
    for src, dst in sorted(VANILLA_CONNECTORS.items()):
        if not dst or VANILLA_CONNECTORS.get(dst) != src:
            continue
        key = (src, dst) if src < dst else (dst, src)
        if key in seen:
            continue
        seen.add(key)

        classified = _classify_pair(src, dst)
        if classified is None:
            continue
        kind, first, second = classified
        if scope == "interiors" and kind != "door":
            continue
        if scope == "within_areas":
            area_a, area_b = _scene_area(_scene_of(first)), _scene_area(_scene_of(second))
            if area_a != area_b:
                continue
            bucket_key = (area_a, kind)
        else:
            bucket_key = ("all", kind)
        buckets.setdefault(bucket_key, []).append((first, second))
    return {k: pairs for k, pairs in buckets.items() if len(pairs) >= 2}


def _reachable_scenes(connectors: dict[str, str], roots: set[str]) -> set[str]:
    adj: dict[str, set[str]] = {}
    for src, dst in connectors.items():
        if not dst:
            continue
        s1, s2 = _scene_of(src), _scene_of(dst)
        adj.setdefault(s1, set()).add(s2)

    visited = set(roots)
    queue = deque(roots)
    while queue:
        cur = queue.popleft()
        for nxt in adj.get(cur, ()):
            if nxt not in visited:
                visited.add(nxt)
                queue.append(nxt)
    return visited


def _merge_components(
    pairs: dict[str, str],
    buckets: dict[tuple[str, str], list[tuple[str, str]]],
    roots: set[str],
    expected_reachable: set[str],
    rng: Random,
) -> bool:
    """Repair any disconnected cycles by 2-opt swapping a reachable and unreachable edge in the same bucket."""
    for _ in range(256):
        merged = dict(VANILLA_CONNECTORS)
        merged.update(pairs)
        reached = _reachable_scenes(merged, roots)
        missing = expected_reachable - reached
        if not missing:
            return True

        candidates = []
        for _, edges in sorted(buckets.items()):
            firsts = [u for u, _ in edges]
            in_r = [u for u in firsts if _scene_of(u) in reached and _scene_of(pairs[u]) in reached]
            in_m = [u for u in firsts if _scene_of(u) in missing or _scene_of(pairs[u]) in missing]
            if in_r and in_m:
                candidates.append((in_r, in_m))
        if not candidates:
            return False
        in_r, in_m = rng.choice(candidates)
        u1, u2 = rng.choice(in_r), rng.choice(in_m)
        v1, v2 = pairs[u1], pairs[u2]
        pairs[u1], pairs[v2] = v2, u1
        pairs[u2], pairs[v1] = v1, u2
    return False


def shuffle_solvable(
    scope: str,
    rng: Random,
    is_valid,
) -> dict[str, str]:
    """Generate a reciprocal, logically solvable transition mapping for Hollow Knight.

    Calls ``is_valid(pairs)`` to verify that every vanilla-reachable check and the HK goal
    remain reachable under the real ``GeneratedRules.py`` logic.
    """
    if scope not in SCOPE_TO_RANDO_TYPE:
        raise ValueError(f"Unknown Hollow Knight entrance scope: {scope!r}")

    buckets = _collect_buckets(scope)
    if not buckets:
        return {}

    roots = {"Town"}
    expected_reachable = _reachable_scenes(VANILLA_CONNECTORS, roots)

    # Start from the vanilla identity mapping for all eligible edges.
    pairs: dict[str, str] = {}
    for edges in buckets.values():
        for u, v in edges:
            pairs[u] = v
            pairs[v] = u

    for bucket_key, edges in sorted(buckets.items()):
        firsts = [u for u, _ in edges]
        seconds = [v for _, v in edges]
        snapshot = dict(pairs)

        # Fast path: try shuffling the entire bucket at once.
        accepted = False
        for _ in range(4):
            cand = dict(snapshot)
            shuffled = list(seconds)
            rng.shuffle(shuffled)
            for u, v in zip(firsts, shuffled):
                cand[u] = v
                cand[v] = u
            if _merge_components(cand, {bucket_key: edges}, roots, expected_reachable, rng) and is_valid(cand):
                pairs = cand
                accepted = True
                break
        if accepted:
            continue

        # Fallback for buckets with one-way internal room barriers: apply random
        # coupled 2-opt swaps and keep every swap that preserves full logical reachability.
        swap_budget = min(len(firsts) * 2, 48)
        for _ in range(swap_budget):
            u1, u2 = rng.sample(firsts, 2)
            v1, v2 = pairs[u1], pairs[u2]
            if v1 == v2:
                continue
            pairs[u1], pairs[v2] = v2, u1
            pairs[u2], pairs[v1] = v1, u2
            if not is_valid(pairs):
                pairs[u1], pairs[v1] = v1, u1
                pairs[u2], pairs[v2] = v2, u2

    return pairs

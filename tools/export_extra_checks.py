"""Extract lever, breakable wall, and grass check metadata from local HK and Silksong installs.

Reads only object names, world coordinates, scene IDs, and logic-graph references
(no textures, sprites, dialogue, or other copyrighted game assets). Writes
apworld/hkss/extra_checks_data.json as the single source of truth for the APWorld,
both plugins, the player hub, and the PopTracker pack.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
import sys
import warnings
from collections import defaultdict
from pathlib import Path
from typing import Any

warnings.filterwarnings("ignore")
import UnityPy

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_STEAM_COMMON = Path(
    os.environ.get("STEAM_COMMON", r"C:\Program Files (x86)\Steam\steamapps\common")
)
DEFAULT_OUTPUT = REPO_ROOT / "apworld" / "hkss" / "extra_checks_data.json"
MAP_GEOMETRY = REPO_ROOT / "data" / "map_geometry.json"
HK_ROOMS_JSON = (
    REPO_ROOT / "local" / "reference" / "hk-randomizermod" / "RandomizerMod" / "Resources" / "Data" / "rooms.json"
)
SS_MAP_LOGIC = REPO_ROOT / "apworld" / "hkss" / "silksong" / "map_logic.json"

# Fixed ID block bases inside HK_BLOCK (84,100,000-84,199,999) and SS_BLOCK (84,200,000-84,299,999).
HK_LEVER_ID_BASE = 84_150_000
HK_WALL_ID_BASE = 84_152_000
HK_GRASS_ID_BASE = 84_154_000

SS_LEVER_ID_BASE = 84_250_000
SS_WALL_ID_BASE = 84_252_000
SS_GRASS_ID_BASE = 84_254_000

HK_SKIP_SCENE_PREFIXES = (
    "GG_",
    "Menu_",
    "Pre_Menu",
    "Quit_",
    "Beta",
    "Opening_",
    "Cinematic_",
    "End_",
    "PermaDeath",
    "Knight_Pickup",
    "Room_Colosseum_Gold",
    "Room_Colosseum_Bronze",
    "Room_Colosseum_Silver",
    "Dream_",
    "Room_Final_Boss_Core",
)

HK_POP_SCENES = frozenset({
    "White_Palace_17",
    "White_Palace_18",
    "White_Palace_19",
    "White_Palace_20",
})

# Silksong area IDs that are Act 3 only or post-Act-1 (used to match goal scoping).
SS_ACT3_AREA_PREFIXES = (
    "the-abyss/",
    "verdania/",
    "lost-verdania/",
    "memory-",
    "surface/",
)
SS_ACT3_SCENES = frozenset({
    "Cradle_03_Destroyed",
    "Abandoned_town",
    "Bellway_Centipede_Arena",
    "Bone_Steel_Servant",
})
SS_ACT1_AREAS = frozenset({
    "moss-grotto",
    "bone-bottom",
    "the-marrow",
    "deep-docks",
    "far-fields",
    "hunter-s-march",
    "greymoor",
    "bellhart",
    "shellwood",
    "wormways",
    "blasted-steps",
    "sinner-s-road",
    "weavenest-atla",
})


def _load_hk_logic_data():
    sys.path.insert(0, str(REPO_ROOT / "apworld" / "hkss" / "hk"))
    import ExtractedData as hk_ed  # type: ignore

    return hk_ed


def _hk_scene_transitions(scene: str, hk_ed) -> list[str]:
    """Return sorted HK logic transitions/nodes that enter or represent `scene`."""
    prefix = scene + "["
    transitions = sorted(
        t for t in hk_ed.connectors
        if t.startswith(prefix) and hk_ed.connectors[t]
    )
    if transitions:
        return transitions
    if scene in hk_ed.event_names:
        return [scene]
    return []


def _extract_hk(steam_common: Path, geom: dict[str, Any]) -> dict[str, list[dict[str, Any]]]:
    hk_data = steam_common / "Hollow Knight" / "hollow_knight_Data"
    hk_ed = _load_hk_logic_data()

    env0 = UnityPy.load(str(hk_data / "globalgamemanagers"))
    scene_names = [
        Path(s).stem
        for obj in env0.objects
        if obj.type.name == "BuildSettings"
        for s in obj.read().scenes
    ]
    env_ggm = UnityPy.load(str(hk_data / "globalgamemanagers.assets"))
    scripts_by_pid = {
        obj.path_id: obj.read().m_ClassName
        for obj in env_ggm.objects
        if obj.type.name == "MonoScript"
    }

    rooms_meta: dict[str, dict[str, Any]] = {}
    if HK_ROOMS_JSON.is_file():
        raw_rooms = json.loads(HK_ROOMS_JSON.read_text(encoding="utf-8"))
        if isinstance(raw_rooms, dict):
            rooms_meta = raw_rooms
        elif isinstance(raw_rooms, list):
            for entry in raw_rooms:
                if isinstance(entry, dict) and "SceneName" in entry:
                    rooms_meta[entry["SceneName"]] = entry

    hk_geom = geom["HK"]
    interior_to_map = hk_geom.get("interiorToMapRoom", {})
    geom_rooms = {r["id"]: r for r in hk_geom["rooms"]}

    hk_logic_scenes = set(hk_ed.location_to_region_lookup.values())
    for k in hk_ed.connectors:
        hk_logic_scenes.add(k.split("[")[0])

    target_scenes = [
        (i, s)
        for i, s in enumerate(scene_names)
        if s in hk_logic_scenes and not s.startswith(HK_SKIP_SCENE_PREFIXES)
    ]

    raw_levers: list[dict[str, Any]] = []
    raw_walls: list[dict[str, Any]] = []
    raw_grass: list[dict[str, Any]] = []

    def scan_scene(item: tuple[int, str]):
        idx, sc = item
        transitions = _hk_scene_transitions(sc, hk_ed)
        if not transitions:
            return [], [], None

        lv = UnityPy.load(str(hk_data / f"level{idx}"))
        gos: dict[int, str] = {}
        transforms: dict[int, tuple[float, float]] = {}
        go_scripts: dict[int, list[str]] = defaultdict(list)

        for obj in lv.objects:
            t = obj.type.name
            if t == "GameObject":
                gos[obj.path_id] = obj.read().m_Name
            elif t == "Transform":
                d = obj.read()
                pos = d.m_LocalPosition
                transforms[d.m_GameObject.path_id] = (round(float(pos.x), 2), round(float(pos.y), 2))
            elif t == "MonoBehaviour":
                d = obj.read(check_read=False)
                sname = scripts_by_pid.get(d.m_Script.path_id, "")
                if sname in ("PersistentBoolItem", "BridgeLever", "GrassCut", "Grass", "TownGrass"):
                    go_scripts[d.m_GameObject.path_id].append(sname)

        map_room = interior_to_map.get(sc, sc)
        area = (
            rooms_meta.get(sc, {}).get("MapArea")
            or geom_rooms.get(map_room, {}).get("area")
            or ("White Palace" if sc.startswith("White_Palace_") else "Hallownest")
        )
        wp = "pop" if sc in HK_POP_SCENES else ("palace" if sc.startswith("White_Palace_") else "none")

        sc_levers = []
        sc_walls = []
        has_grass = False

        for gid, slist in go_scripts.items():
            gname = gos.get(gid, "")
            gl = gname.lower()
            x, y = transforms.get(gid, (0.0, 0.0))
            if "GrassCut" in slist or "Grass" in slist or "TownGrass" in slist:
                has_grass = True
            has_persist = "PersistentBoolItem" in slist
            if "BridgeLever" in slist or (
                has_persist
                and ("lever" in gl or "switch" in gl)
                and not any(bad in gl for bad in ("toll", "bench", "stag", "gate", "door", "boss", "statue"))
            ):
                sc_levers.append({
                    "scene": sc,
                    "mapRoom": map_room,
                    "area": area,
                    "objectName": gname,
                    "x": x,
                    "y": y,
                    "whitePalace": wp,
                    "transitions": transitions,
                })
            elif has_persist and any(
                k in gl
                for k in (
                    "breakable wall",
                    "breakable_wall",
                    "one way",
                    "one_way",
                    "quake floor",
                    "quake_floor",
                    "secret wall",
                    "secret_wall",
                    "break_wall",
                    "breakable floor",
                    "breakable_floor",
                )
            ):
                sc_walls.append({
                    "scene": sc,
                    "mapRoom": map_room,
                    "area": area,
                    "objectName": gname,
                    "x": x,
                    "y": y,
                    "requiresQuake": "quake" in gl,
                    "whitePalace": wp,
                    "transitions": transitions,
                })

        sc_grass = None
        if has_grass:
            sc_grass = {
                "scene": sc,
                "mapRoom": map_room,
                "area": area,
                "whitePalace": wp,
                "transitions": transitions,
            }
        return sc_levers, sc_walls, sc_grass

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        for sc_l, sc_w, sc_g in ex.map(scan_scene, target_scenes):
            raw_levers.extend(sc_l)
            raw_walls.extend(sc_w)
            if sc_g:
                raw_grass.append(sc_g)

    def assign_hk(entries: list[dict[str, Any]], kind: str, prefix: str, id_base: int) -> list[dict[str, Any]]:
        entries.sort(key=lambda e: (e["scene"], e.get("x", 0.0), e.get("y", 0.0), e.get("objectName", "")))
        by_scene: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for e in entries:
            by_scene[e["scene"]].append(e)
        result: list[dict[str, Any]] = []
        next_id = id_base
        for sc in sorted(by_scene):
            group = by_scene[sc]
            for i, e in enumerate(group, 1):
                name = f"{prefix}-{sc}" if len(group) == 1 else f"{prefix}-{sc}_{i}"
                rec = {"id": next_id, "name": name, "kind": kind, **e}
                result.append(rec)
                next_id += 1
        return result

    return {
        "levers": assign_hk(raw_levers, "lever", "Lever", HK_LEVER_ID_BASE),
        "breakable_walls": assign_hk(raw_walls, "breakable_wall", "Breakable_Wall", HK_WALL_ID_BASE),
        "grass": assign_hk(raw_grass, "grass", "Grass", HK_GRASS_ID_BASE),
    }


def _find_ss_node_for_point(
    room: dict[str, Any],
    x: float,
    y: float,
    valid_nodes: set[str],
) -> str | None:
    """Return the Silksong 'Room Node: ...' containing (x, y), or nearest valid node in the room."""
    candidates = []
    fallback = None
    for n in room.get("nodes", []):
        rn = "Room Node: " + n["id"]
        if rn not in valid_nodes:
            continue
        if fallback is None:
            fallback = rn
        if n.get("annotation_x") is None or n.get("annotation_y") is None:
            continue
        nx = float(n.get("annotation_x") or 0.0)
        ny = float(n.get("annotation_y") or 0.0)
        nw = float(n.get("annotation_width") or 0.0)
        nh = float(n.get("annotation_height") or 0.0)
        if nx - 1.5 <= x <= nx + nw + 1.5 and ny - 1.5 <= y <= ny + nh + 1.5:
            return rn
        cx = nx + nw * 0.5
        cy = ny + nh * 0.5
        dist2 = (x - cx) ** 2 + (y - cy) ** 2
        candidates.append((dist2, rn))
    if candidates:
        candidates.sort(key=lambda item: item[0])
        return candidates[0][1]
    return fallback


def _compute_ss_reachable(
    abstract_reqs: dict[str, Any],
    forbidden: set[str],
) -> set[str]:
    known: set[str] = set()
    all_syms: set[str] = set()
    for k, v in abstract_reqs.items():
        all_syms.add(k)
        for alt in v.get("alternatives", ()):
            all_syms.update(alt.get("all_of", ()))
            for ic in alt.get("item_counts", ()):
                all_syms.update(ic.get("items", ()))
    for s in all_syms:
        if s not in abstract_reqs and s not in forbidden:
            known.add(s)
    changed = True
    while changed:
        changed = False
        for k, v in abstract_reqs.items():
            if k in known or k in forbidden:
                continue
            for alt in v.get("alternatives", ()):
                if not all(req in known for req in alt.get("all_of", ())):
                    continue
                item_counts_ok = True
                for ic in alt.get("item_counts", ()):
                    if int(ic.get("minimum", 1)) > 0 and not any(
                        item in known for item in ic.get("items", ())
                    ):
                        item_counts_ok = False
                        break
                if item_counts_ok:
                    known.add(k)
                    changed = True
                    break
    return known


def _ss_room_dimensions(room: dict[str, Any]) -> tuple[float, float]:
    max_x = 1.0
    max_y = 1.0
    for n in room.get("nodes", []):
        if n.get("annotation_x") is None or n.get("annotation_y") is None:
            continue
        max_x = max(max_x, float(n.get("annotation_x") or 0.0) + float(n.get("annotation_width") or 0.0))
        max_y = max(max_y, float(n.get("annotation_y") or 0.0) + float(n.get("annotation_height") or 0.0))
    return round(max_x, 2), round(max_y, 2)


def _extract_ss(steam_common: Path, geom: dict[str, Any]) -> dict[str, list[dict[str, Any]]]:
    UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
    ss_aa = (
        steam_common
        / "Hollow Knight Silksong"
        / "Hollow Knight Silksong_Data"
        / "StreamingAssets"
        / "aa"
        / "StandaloneWindows64"
    )
    ms_env = UnityPy.load(str(ss_aa / "94696d22b6ed0a74097d1bd58feb4dce_monoscripts.bundle"))
    target_scripts = {
        "Lever",
        "Lever_tk2d",
        "BridgeLever",
        "PersistentBoolItem",
        "Breakable",
        "GrassCut",
        "Grass",
    }
    target_pids = {
        obj.path_id: obj.read().m_ClassName
        for obj in ms_env.objects
        if obj.type.name == "MonoScript"
    }
    target_pids = {pid: name for pid, name in target_pids.items() if name in target_scripts}

    map_logic = json.loads(SS_MAP_LOGIC.read_text(encoding="utf-8"))
    abstract_reqs = map_logic["logic"]["abstract_requirements"]
    valid_nodes = {
        k
        for k in abstract_reqs
        if k.startswith("Room Node: ")
    }
    act3_forbidden = {
        "Act: 3",
        "Elegy of the Deep",
        "Beastling Call",
        "Pale Nails",
        "Everbloom",
        "Craw Summons",
        "Pollen Heart",
        "Hunter's Heart",
        "Encrusted Heart",
        "Conjoined Heart",
        "Farsight",
        "Pristine Core",
    }
    reachable_act2 = _compute_ss_reachable(abstract_reqs, act3_forbidden)
    reachable_act2_no_ss = _compute_ss_reachable(abstract_reqs, act3_forbidden | {"Silk Soar"})
    reachable_act1 = _compute_ss_reachable(
        abstract_reqs,
        act3_forbidden
        | {
            "Act: 2",
            "Silk Soar",
            "Faydown Cloak",
            "Clawline",
            "White Key",
            "Architect's Key",
            "Apostate Key",
            "Slab Key",
            "Surgeon's Key",
        },
    )
    rooms_by_scene: dict[str, dict[str, Any]] = {}
    for room in map_logic["room_geometry"]["rooms"]:
        ig = room.get("in_game_id")
        if ig and ig.lower() not in rooms_by_scene:
            rooms_by_scene[ig.lower()] = room

    ss_geom_by_id = {r["id"].lower(): r for r in geom["SS"]["rooms"]}

    sc_dir = ss_aa / "scenes_scenes_scenes"
    bundles = sorted(
        b for b in sc_dir.rglob("*.bundle")
        if b.stem.lower() in rooms_by_scene
    )

    raw_levers: list[dict[str, Any]] = []
    raw_walls: list[dict[str, Any]] = []
    raw_grass: list[dict[str, Any]] = []

    def scan_bundle(b: Path):
        room = rooms_by_scene[b.stem.lower()]
        sc = room["in_game_id"]
        room_id = room["id"]
        room_name = " ".join(room["name"].replace('"', "'").split())
        area = ss_geom_by_id.get(sc.lower(), {}).get("area") or room.get("room_group_name", "Pharloom")
        sw, sh = _ss_room_dimensions(room)
        act3_only = sc in SS_ACT3_SCENES or room_id.startswith(SS_ACT3_AREA_PREFIXES)
        act1_area = room_id.split("/", 1)[0] in SS_ACT1_AREAS and not act3_only

        lv = UnityPy.load(str(b))
        go_scripts: dict[int, list[str]] = defaultdict(list)
        go_objs: dict[int, Any] = {}
        tf_objs: dict[int, Any] = {}

        for obj in lv.objects:
            t = obj.type.name
            if t == "MonoBehaviour":
                d = obj.read(check_read=False)
                sname = target_pids.get(d.m_Script.path_id)
                if sname:
                    go_scripts[d.m_GameObject.path_id].append(sname)
            elif t == "GameObject":
                go_objs[obj.path_id] = obj
            elif t == "Transform":
                tf_objs[obj.path_id] = obj

        def get_go_info(gid: int) -> tuple[str, float, float]:
            gobj = go_objs.get(gid)
            if gobj is None:
                return "", sw * 0.5, sh * 0.5
            gd = gobj.read()
            name = gd.m_Name
            for comp in gd.m_Component:
                cid = comp.component.path_id
                if cid in tf_objs:
                    pos = tf_objs[cid].read().m_LocalPosition
                    return name, round(float(pos.x), 2), round(float(pos.y), 2)
            return name, sw * 0.5, sh * 0.5

        sc_levers = []
        sc_walls = []
        grass_nodes: set[str] = set()

        for gid, slist in go_scripts.items():
            is_grass = "GrassCut" in slist or "Grass" in slist
            has_persist = "PersistentBoolItem" in slist
            if not is_grass and not has_persist:
                continue
            gname, x, y = get_go_info(gid)
            if is_grass:
                rn = _find_ss_node_for_point(room, x, y, valid_nodes)
                if rn:
                    grass_nodes.add(rn)
            if not has_persist:
                continue
            gl = gname.lower()
            if any(s in slist for s in ("Lever", "Lever_tk2d", "BridgeLever")):
                rn = _find_ss_node_for_point(room, x, y, valid_nodes)
                if rn:
                    node_act3 = act3_only or (rn not in reachable_act2)
                    node_silk_soar = (not node_act3) and (rn not in reachable_act2_no_ss)
                    node_act1 = act1_area and (not node_act3) and (rn in reachable_act1)
                    sc_levers.append({
                        "scene": sc,
                        "roomId": room_id,
                        "roomName": room_name,
                        "area": area,
                        "objectName": gname,
                        "x": x,
                        "y": y,
                        "sceneWidth": sw,
                        "sceneHeight": sh,
                        "nodes": [rn],
                        "act3Only": node_act3,
                        "requiresSilkSoar": node_silk_soar,
                        "act1Scope": node_act1,
                    })
            elif ("Breakable" in slist or "break" in gl) and any(
                k in gl
                for k in (
                    "wall",
                    "floor",
                    "ceiling",
                    "secret",
                    "barrier",
                    "crumble",
                    "break_wall",
                    "breakable_wall",
                )
            ):
                rn = _find_ss_node_for_point(room, x, y, valid_nodes)
                if rn:
                    node_act3 = act3_only or (rn not in reachable_act2)
                    node_silk_soar = (not node_act3) and (rn not in reachable_act2_no_ss)
                    node_act1 = act1_area and (not node_act3) and (rn in reachable_act1)
                    sc_walls.append({
                        "scene": sc,
                        "roomId": room_id,
                        "roomName": room_name,
                        "area": area,
                        "objectName": gname,
                        "x": x,
                        "y": y,
                        "sceneWidth": sw,
                        "sceneHeight": sh,
                        "nodes": [rn],
                        "act3Only": node_act3,
                        "requiresSilkSoar": node_silk_soar,
                        "act1Scope": node_act1,
                    })

        sc_grass = None
        if grass_nodes:
            sorted_gnodes = sorted(grass_nodes)
            grass_act3 = act3_only or not any(rn in reachable_act2 for rn in sorted_gnodes)
            grass_silk_soar = (not grass_act3) and not any(rn in reachable_act2_no_ss for rn in sorted_gnodes)
            grass_act1 = act1_area and (not grass_act3) and any(rn in reachable_act1 for rn in sorted_gnodes)
            sc_grass = {
                "scene": sc,
                "roomId": room_id,
                "roomName": room_name,
                "area": area,
                "x": round(sw * 0.5, 2),
                "y": round(sh * 0.5, 2),
                "sceneWidth": sw,
                "sceneHeight": sh,
                "nodes": sorted_gnodes,
                "act3Only": grass_act3,
                "requiresSilkSoar": grass_silk_soar,
                "act1Scope": grass_act1,
            }
        return sc_levers, sc_walls, sc_grass

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as ex:
        for sc_l, sc_w, sc_g in ex.map(scan_bundle, bundles):
            raw_levers.extend(sc_l)
            raw_walls.extend(sc_w)
            if sc_g:
                raw_grass.append(sc_g)

    def assign_ss(entries: list[dict[str, Any]], kind: str, label: str, id_base: int) -> list[dict[str, Any]]:
        entries.sort(key=lambda e: (e["scene"], e.get("x", 0.0), e.get("y", 0.0), e.get("objectName", "")))
        by_name: dict[str, list[dict[str, Any]]] = defaultdict(list)
        for e in entries:
            by_name[e["roomName"]].append(e)
        result: list[dict[str, Any]] = []
        next_id = id_base
        for rname in sorted(by_name):
            group = by_name[rname]
            for i, e in enumerate(group, 1):
                name = f"{label}: {rname}" if len(group) == 1 else f"{label}: {rname} #{i}"
                rec = {"id": next_id, "name": name, "kind": kind, **e}
                result.append(rec)
                next_id += 1
        return result

    return {
        "levers": assign_ss(raw_levers, "lever", "Lever", SS_LEVER_ID_BASE),
        "breakable_walls": assign_ss(raw_walls, "breakable_wall", "Breakable Wall", SS_WALL_ID_BASE),
        "grass": assign_ss(raw_grass, "grass", "Grass", SS_GRASS_ID_BASE),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steam-common", type=Path, default=DEFAULT_STEAM_COMMON)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true", help="Verify output matches committed JSON.")
    args = parser.parse_args()

    geom = json.loads(MAP_GEOMETRY.read_text(encoding="utf-8"))
    payload = {
        "schemaVersion": 1,
        "hollow_knight": _extract_hk(args.steam_common, geom),
        "silksong": _extract_ss(args.steam_common, geom),
    }
    encoded = json.dumps(payload, indent=2, sort_keys=False) + "\n"

    if args.check:
        if not args.output.is_file():
            print(f"MISSING: {args.output}")
            return 1
        existing = args.output.read_text(encoding="utf-8")
        if existing != encoded:
            print(f"DRIFT: {args.output} does not match freshly extracted data.")
            return 1
        print(f"OK: {args.output} is up to date.")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(encoded, encoding="utf-8")
    hk = payload["hollow_knight"]
    ss = payload["silksong"]
    print(
        f"Wrote {args.output.relative_to(REPO_ROOT)}: "
        f"HK(levers={len(hk['levers'])}, walls={len(hk['breakable_walls'])}, grass={len(hk['grass'])}) "
        f"SS(levers={len(ss['levers'])}, walls={len(ss['breakable_walls'])}, grass={len(ss['grass'])})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

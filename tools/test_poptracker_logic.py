"""Check the PopTracker pack's logic against the Archipelago world.

Generates a seed per option case, collects random sets of the slot's progression items, and
compares which locations the world can reach (CollectionState, events swept) with what the built
pack answers: its own scripts run in a stand-in for PopTracker, fed the same slot data and items.
Requires the `lupa` package (pip install lupa) and a pack built by tools/export_poptracker.py.
"""

from __future__ import annotations

import argparse
import json
import os
import random
import shutil
import sys
import tempfile
import types
from pathlib import Path

from export_hub_data import load_world
from poptracker_logic import to_lua
from test_apworld import DEFAULT_AP_DIR, DEFAULT_APWORLD, REPO_ROOT

PACK = REPO_ROOT / "local/poptracker"

# Option cases: YAML lines under "Hollow Knight x Silksong:".
CASES = {
    "default": "",
    "split": ("SplitMothwingCloak: true", "SplitMantisClaw: true", "SplitCrystalHeart: true",
              "RandomizeSwim: true", "RandomizeFocus: true", "RandomizeNail: true",
              "directional_cling_grip: true", "directional_swift_step: true", "directional_clawline: true",
              "directional_ledge_grab: true", "split_dash_and_sprint: true"),
    "skips": ("PreciseMovement: true", "ProficientCombat: true", "EnemyPogos: true", "ShadeSkips: true",
              "DifficultSkips: true", "RandomizeCharmNotches: true", "enemy_pogo_logic: moderate",
              "proficient_combat: goated", "scuttlebrace_logic: easy",
              "platforming_capability: proficient_movement", "crest_pogo_logic: difficult"),
    "costs": ("CostSanity: on", "RandomizeGrubs: true", "RandomizeCharms: true"),
    "vanilla": ("crest_randomization: vanilla", "tool_randomization: vanilla", "eva_randomization: vanilla",
                "bell_shrine_sanity: true", "goal: flea_hunt", "starting_location: bone_bottom",
                "trails_end_requirement: owned_maps", "WhitePalace: include", "RandomizeGrubs: true",
                "RandomizeGrimmkinFlames: true", "RandomizeCharms: false", "RandomizeBossEssence: true"),
    "souls": ("enemy_souls: true", "game_mode: steel_soul", "goal: act_2", "Goal: radiance",
              "RemoveSpellUpgrades: true", "SplitMothwingCloak: true", "RandomizeSkills: false"),
    "entrances": ("entrance_randomization: coupled", "entrance_randomization_scope: full",
                  "AddUnshuffledLocations: true", "WhitePalace: nopathofpain"),
    "entrances_areas": ("entrance_randomization: coupled", "entrance_randomization_scope: within_areas",
                        "goal: cursed_ending", "combo_goal: either"),
    "vanilla_skills": ("skill_randomization: vanilla", "silk_skill_randomization: vanilla"),
    "unified": ("shuffle_entrances: within_areas", "shuffle_charms_and_crests: on",
                "shuffle_shops: randomized", "shuffle_grubs_and_fleas: on",
                "shuffle_journal_entries: on", "shuffle_lore_tablets: on"),
    "seals": ("cross_game_seals: 4",),
    "seals_entrances": ("cross_game_seals: 4", "shuffle_entrances: full", "SplitMantisClaw: true",
                        "SplitMothwingCloak: true", "directional_cling_grip: true", "directional_clawline: true"),
    "cross_game_items": ("cross_game_items: true", "cross_game_seals: 2"),
    "cross_game_items_split": ("cross_game_items: true", "SplitMothwingCloak: true", "SplitMantisClaw: true",
                               "RandomizeSwim: true", "directional_swift_step: true",
                               "directional_cling_grip: true", "swim_ability_rando: true"),
    "hk_alt_start_greenpath": ("StartLocation: greenpath", "cross_game_seals: 2"),
    "hk_alt_start_colosseum": ("StartLocation: outside_colosseum", "cross_game_seals: 4", "shuffle_entrances: full"),
    "ss_alt_start_bellhart": ("starting_location: bellhart", "cross_game_seals: 2"),
    "ss_alt_start_both_entrances": ("starting_location: wormways", "StartLocation: greenpath",
                                    "cross_game_seals: 2", "shuffle_entrances: full"),
}
FRACTIONS = (0, 0.05, 0.15, 0.3, 0.5, 0.7, 0.9, 1.0)


def generate(case: str, lines, seed: int, work: Path):
    """Generate one solo seed and return its multiworld."""
    from Generate import main as gen_main
    from Main import main as run_main

    players, out = work / "players", work / "out"
    shutil.rmtree(players, ignore_errors=True)
    shutil.rmtree(out, ignore_errors=True)
    players.mkdir(parents=True)
    out.mkdir()
    options = "".join(f"  {line}\n" for line in lines)
    (players / "P.yaml").write_text(
        f"name: P\ngame: Hollow Knight x Silksong\nHollow Knight x Silksong:\n  games: both\n{options}")
    args = types.SimpleNamespace(
        weights_file_path="", sameoptions=False, player_files_path=str(players), seed=seed, multi=1,
        spoiler=0, outputpath=str(out), allow_quantity=False, race=False, meta_file_path="",
        log_level="warning", log_time=False, csv_output=False, plando=None, skip_prog_balancing=False,
        skip_output=True, spoiler_only=False)
    erargs, rolled = gen_main(args)
    return run_main(erargs, rolled)


def ground_truth(mw, seed: int):
    from BaseClasses import CollectionState

    player = 1
    world = mw.worlds[player]
    slot_data = world.fill_slot_data()
    multidata = {"slot_data": {player: slot_data}}
    world.modify_multidata(multidata)
    slot_data = json.loads(json.dumps(multidata["slot_data"][player], default=lambda o: getattr(o, "value", str(o))))
    real = [loc for loc in mw.get_locations(player) if loc.address is not None]
    events = [loc for loc in mw.get_locations(player) if loc.address is None]
    progression = [loc.item for loc in real if loc.item and loc.item.player == player and loc.item.advancement]
    start = [item.name for item in mw.precollected_items[player]]
    rng = random.Random(seed)
    sets = []
    for fraction in FRACTIONS:
        chosen = [item for item in progression if rng.random() < fraction]
        state = CollectionState(mw)
        for item in chosen:
            state.collect(item, True)
        state.sweep_for_advancements(locations=events)
        sets.append((start + [item.name for item in chosen], {loc.name for loc in real if loc.can_reach(state)}))
    return slot_data, sorted(loc.name for loc in real), sets


class PackRunner:
    """The pack's scripts in a minimal PopTracker stand-in."""

    def __init__(self, pack: Path):
        from lupa.lua54 import LuaRuntime, lua_type

        self.lua = lua = LuaRuntime(unpack_returned_tuples=True)
        self.lua_type = lua_type
        g = self.g = lua.globals()
        g.PACK = str(pack).replace("\\", "/")
        g.json_roundtrip = self.json_roundtrip
        lua.execute("""
            OBJECTS = {}
            Tracker = {}
            function Tracker:FindObjectForCode(code)
              local o = OBJECTS[code]
              if not o then
                o = {Type = "consumable", Active = false, AcquiredCount = 0, MaxCount = 999, Increment = 1,
                     AvailableChestCount = 1, ChestCount = 1}
                OBJECTS[code] = o
              end
              return o
            end
            function Tracker:AddItems() end function Tracker:AddMaps() end
            function Tracker:AddLocations() end function Tracker:AddLayouts() end
            local function read(path)
              local f = assert(io.open(PACK .. "/" .. path, "rb")); local s = f:read("a"); f:close(); return s
            end
            ScriptHost = {}
            function ScriptHost:LoadScript(path) assert(load(read(path), "@" .. path))() end
            function ScriptHost:AddWatchForCode() end
            function ScriptHost:AddOnFrameHandler(_, f) FRAME = f end
            function ScriptHost:RunScriptAsync(path, arg, done)
              local env = setmetatable({arg = json_roundtrip(arg)}, {__index = _G})
              local loaded = {}
              env.require = function(name)
                if loaded[name] then return end
                loaded[name] = true
                assert(load(read("scripts/" .. name .. ".lua"), "@" .. name, "t", env))()
              end
              local result = assert(load(read(path), "@" .. path, "t", env))()
              PENDING = function() done(json_roundtrip(result)) end
            end
            HANDLERS = {}
            Archipelago = {}
            function Archipelago:AddClearHandler(_, f) HANDLERS.clear = f end
            function Archipelago:AddItemHandler(_, f) HANDLERS.item = f end
            function Archipelago:AddLocationHandler(_, f) HANDLERS.location = f end
            ScriptHost:LoadScript("scripts/init.lua")
        """)
        self.item_ids = {name: item_id for item_id, name in g.ITEM_NAMES.items()}
        self.location_ids = {name: location_id for location_id, name in g.LOCATION_NAMES.items()}

    def to_python(self, value):
        if self.lua_type(value) == "table":
            keys = list(value.keys())
            if keys and all(isinstance(k, int) for k in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
                return [self.to_python(value[k]) for k in sorted(keys)]
            return {str(k): self.to_python(value[k]) for k in keys}
        return value

    def json_roundtrip(self, value):
        """PopTracker passes async arguments and results as JSON."""
        return self.lua.eval(to_lua(json.loads(json.dumps(self.to_python(value)))))

    def verify_autotracking(self, pack: Path, slot_data, locations):
        """Verify pack JSON mappings and AP auto-tracking callbacks for both games."""
        items_json = json.loads((pack / "items/items.json").read_text(encoding="utf-8"))
        item_types = {entry["codes"]: entry["type"] for entry in items_json}
        for item_id, entry in self.g.ITEM_MAPPING.items():
            code, kind = entry[1], entry[2]
            assert code in item_types, f"ITEM_MAPPING[{item_id}] references unknown item code {code}"
            assert item_types[code] == kind, f"ITEM_MAPPING[{item_id}] type {kind} != {item_types[code]}"

        locs_json = json.loads((pack / "locations/locations.json").read_text(encoding="utf-8"))
        valid_paths = set()
        for game_node in locs_json:
            for group in game_node.get("children", []):
                for section in group.get("sections", []):
                    valid_paths.add(f"@{game_node['name']}/{group['name']}/{section['name']}")
        for loc_id, entry in self.g.LOCATION_MAPPING.items():
            for idx in range(1, len(entry) + 1):
                path = entry[idx]
                assert path in valid_paths, f"LOCATION_MAPPING[{loc_id}][{idx}] references unknown path {path}"

        g = self.g
        present_ids = [self.location_ids[name] for name in locations if name in self.location_ids]
        hk_loc_id = next(lid for lid in present_ids if self.g.LOCATION_NAMES[lid].startswith("HK "))
        ss_loc_id = next(lid for lid in present_ids if self.g.LOCATION_NAMES[lid].startswith("SS "))
        absent_id = next((lid for lid in self.g.LOCATION_MAPPING.keys() if lid not in set(present_ids)), None)

        g.Archipelago.MissingLocations = self.lua.eval(to_lua(present_ids))
        g.Archipelago.CheckedLocations = self.lua.eval("{}")
        g.HANDLERS.clear(self.lua.eval(to_lua(slot_data)))

        if absent_id is not None:
            absent_obj = g.Tracker.FindObjectForCode(g.Tracker, g.LOCATION_MAPPING[absent_id][1])
            assert absent_obj.AvailableChestCount == 0, "Absent location should be cleared on connect"

        for loc_id in (hk_loc_id, ss_loc_id):
            obj = g.Tracker.FindObjectForCode(g.Tracker, g.LOCATION_MAPPING[loc_id][1])
            assert obj.AvailableChestCount == 1, f"Present location {loc_id} should start unchecked"
            g.HANDLERS.location(loc_id, g.LOCATION_NAMES[loc_id])
            assert obj.AvailableChestCount == 0, f"Location {loc_id} should clear on check"
            g.HANDLERS.location(loc_id, g.LOCATION_NAMES[loc_id])
            assert obj.AvailableChestCount == 0, f"Duplicate location {loc_id} must not underflow"

        sample_items = [
            ("HK Mothwing_Cloak", "toggle"),
            ("HK Grub", "consumable"),
            ("SS Swift Step", "toggle"),
            ("SS Flea", "consumable"),
        ]
        for idx, (name, kind) in enumerate(sample_items):
            item_id = self.item_ids[name]
            code = g.ITEM_MAPPING[item_id][1]
            g.HANDLERS.item(idx, item_id, name, 1)
            obj = g.Tracker.FindObjectForCode(g.Tracker, code)
            if kind == "toggle":
                assert obj.Active is True, f"{name} toggle should be active"
            else:
                assert obj.AcquiredCount == 1, f"{name} count should be 1"
            # Replaying the same index must be ignored.
            g.HANDLERS.item(idx, item_id, name, 1)
            if kind == "consumable":
                assert obj.AcquiredCount == 1, f"Duplicate index for {name} must be ignored"

        g.Archipelago.MissingLocations = None
        g.Archipelago.CheckedLocations = None

    def reachable(self, slot_data, items, locations):
        g = self.g
        g.HANDLERS.clear(self.lua.eval(to_lua(slot_data)))
        index = 0
        for name in items:
            if name in self.item_ids:
                g.HANDLERS.item(index, self.item_ids[name], name, 1)
                index += 1
        g.FRAME()
        g.PENDING()
        return {name for name in locations
                if name in self.location_ids and g.hkss_reach(str(self.location_ids[name]))}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apworld", type=Path, default=DEFAULT_APWORLD)
    parser.add_argument("--ap-dir", type=Path, default=DEFAULT_AP_DIR)
    parser.add_argument("--pack", type=Path, default=PACK, help="Built pack folder (export_poptracker.py --out)")
    parser.add_argument("--seed", type=int, default=100)
    parser.add_argument("cases", nargs="*", help=f"Option cases to run (default: all of {', '.join(CASES)})")
    args = parser.parse_args()
    unknown = [case for case in args.cases if case not in CASES]
    if unknown:
        parser.error(f"unknown case: {', '.join(unknown)}")
    try:
        import lupa  # noqa: F401
    except ImportError:
        raise SystemExit("This check needs lupa: pip install lupa")
    if not (args.pack / "scripts/init.lua").is_file():
        raise SystemExit(f"No built pack at {args.pack}. Run tools/export_poptracker.py first.")

    apworld = args.apworld.resolve()
    load_world(apworld, args.ap_dir.resolve())
    import Utils

    work = Path(tempfile.mkdtemp(prefix="hkss-pt-"))
    user = work / "user"
    (user / "worlds").mkdir(parents=True)
    shutil.copyfile(apworld, user / "worlds/hkss.apworld")
    Utils.user_path = lambda *parts: str(Path(user, *parts))
    runner = PackRunner(args.pack.resolve())

    total = failures = 0
    cwd = os.getcwd()
    try:
        for index, case in enumerate(args.cases or CASES):
            os.chdir(user)
            try:
                mw = generate(case, CASES[case], args.seed + index, work)
            finally:
                os.chdir(cwd)
            slot_data, locations, sets = ground_truth(mw, args.seed + index)
            if index == 0:
                runner.verify_autotracking(args.pack.resolve(), slot_data, locations)
                print(f"{case}: autotracking mappings & callbacks verified (HK + SS)")
            mismatches = []
            for items, expected in sets:
                actual = runner.reachable(slot_data, items, locations)
                total += len(locations)
                missing = [name for name in locations if name not in runner.location_ids]
                mismatches += [("not in pack", name) for name in missing]
                mismatches += [("pack only", name) for name in sorted(actual - expected)]
                mismatches += [("world only", name) for name in sorted(expected - actual - set(missing))]
            failures += len(mismatches)
            print(f"{case}: {len(locations)} locations x {len(sets)} item sets, {len(mismatches)} mismatches"
                  + (f" {mismatches[:5]}" if mismatches else ""))
    finally:
        shutil.rmtree(work, ignore_errors=True)
    print(f"{total} location checks, {failures} mismatches")
    if failures:
        raise SystemExit(1)


if __name__ == "__main__":
    main()

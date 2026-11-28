"""Verify the combined Hollow Knight x Silksong APWorld against Archipelago 0.6.8."""

from __future__ import annotations

import argparse
import importlib.util
import logging
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import types
import zipfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_AP_DIR = Path(os.environ.get("ARCHIPELAGO_DIR", r"C:\ProgramData\Archipelago"))
DEFAULT_APWORLD = REPO_ROOT / "dist" / "hkss.apworld"


def check_id_space_mirror() -> None:
    spec = importlib.util.spec_from_file_location("hkss_ids", REPO_ROOT / "apworld" / "hkss" / "ids.py")
    assert spec is not None and spec.loader is not None
    ids = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ids)

    cs_text = (REPO_ROOT / "src" / "HKSS.Core" / "IdSpace.cs").read_text(encoding="utf-8")
    expected = {
        "ComboBlockStart": ids.COMBO_BLOCK_START,
        "ComboBlockEnd": ids.COMBO_BLOCK_END,
        "HollowKnightUpstreamBase": ids.HK_UPSTREAM_BASE,
        "HollowKnightBlockStart": ids.HK_BLOCK_START,
        "HollowKnightBlockEnd": ids.HK_BLOCK_END,
        "SilksongUpstreamBase": ids.SS_UPSTREAM_BASE,
        "SilksongBlockStart": ids.SS_BLOCK_START,
        "SilksongBlockEnd": ids.SS_BLOCK_END,
    }
    for name, value in expected.items():
        m = re.search(rf"public const long {name} = ([^;]+);", cs_text)
        assert m is not None, f"Missing {name} in IdSpace.cs"
        parsed = int(m.group(1).replace("_", ""), 0)
        assert parsed == value, f"IdSpace.cs {name}={parsed} != ids.py {value}"

    cgi_cs = (REPO_ROOT / "src" / "HKSS.Core" / "CrossGameItems.cs").read_text(encoding="utf-8")
    cgi_py = (REPO_ROOT / "apworld" / "hkss" / "cross_game_items.py").read_text(encoding="utf-8")
    py_entries = re.findall(
        r'CrossGameItemSpec\(COMBO_BLOCK_START \+ (\d+), "([^"]+)", "([^"]+)", "([^"]+)"\)',
        cgi_py,
    )
    cs_entries = re.findall(
        r'new CrossGameItem\(IdSpace\.ComboBlockStart \+ (\d+), "([^"]+)", "([^"]+)", "([^"]+)"\)',
        cgi_cs,
    )
    assert py_entries and py_entries == cs_entries, (py_entries, cs_entries)
    print("PASS id_space_mirror")


def check_apworld_archive(apworld_path: Path) -> None:
    assert apworld_path.is_file(), f"Missing {apworld_path}. Run tools/build_apworld.py first."
    with zipfile.ZipFile(apworld_path) as zf:
        assert zf.testzip() is None, "Corrupt ZIP entries in hkss.apworld"
        names = set(zf.namelist())
        for required in (
            "hkss/__init__.py",
            "hkss/ids.py",
            "hkss/options.py",
            "hkss/cross_game.py",
            "hkss/cross_game_items.py",
            "hkss/cross_game_areas.json",
            "hkss/extra_checks.py",
            "hkss/extra_checks_data.json",
            "hkss/archipelago.json",
            "hkss/VENDORED.json",
            "hkss/hk/__init__.py",
            "hkss/silksong/__init__.py",
        ):
            assert required in names, f"Missing {required} in {apworld_path}"
    print(f"PASS archive ({apworld_path.name}, {len(names)} files)")


def run_generation_suite(ap_dir: Path, apworld_path: Path, seed: int) -> None:
    logging.disable(logging.CRITICAL)
    sys.modules.setdefault("bsdiff4", types.ModuleType("bsdiff4"))
    mu = types.ModuleType("ModuleUpdate")
    mu.update = lambda *a, **k: None
    sys.modules.setdefault("ModuleUpdate", mu)

    lib_dir = ap_dir / "lib"
    lib_zip = lib_dir / "library.zip"
    assert lib_zip.is_file(), f"Archipelago library.zip not found at {lib_zip}"
    sys.path.insert(0, str(lib_zip))
    sys.path.insert(0, str(lib_dir))

    with tempfile.TemporaryDirectory() as settings_td:
        import Utils

        Utils.user_path = lambda *parts: str(Path(settings_td, *parts))
        world_dir = Path(settings_td, "worlds")
        world_dir.mkdir()
        shutil.copyfile(apworld_path, world_dir / "hkss.apworld")
        from Generate import main as gen_main
        from Main import main as run_main
        from worlds.hkss.ids import (
            COMBO_BLOCK_END,
            COMBO_BLOCK_START,
            HK_BLOCK_END,
            HK_BLOCK_START,
            SS_BLOCK_END,
            SS_BLOCK_START,
        )
        from worlds.hkss.cross_game_items import CROSS_GAME_ITEMS_BY_NAME

        _run_cases(
            gen_main,
            run_main,
            seed,
            COMBO_BLOCK_START,
            COMBO_BLOCK_END,
            HK_BLOCK_START,
            HK_BLOCK_END,
            SS_BLOCK_START,
            SS_BLOCK_END,
            CROSS_GAME_ITEMS_BY_NAME,
        )


def _run_cases(
    gen_main,
    run_main,
    seed: int,
    COMBO_BLOCK_START: int,
    COMBO_BLOCK_END: int,
    HK_BLOCK_START: int,
    HK_BLOCK_END: int,
    SS_BLOCK_START: int,
    SS_BLOCK_END: int,
    CROSS_GAME_ITEMS_BY_NAME: dict,
) -> None:

    only = {name for name in os.environ.get("HKSS_TEST_ONLY", "").split(",") if name}

    def run_case(label: str, yamls: dict[str, str]) -> None:
        if only and label not in only:
            return
        prev_cwd = os.getcwd()
        with tempfile.TemporaryDirectory() as td:
            os.chdir(td)
            try:
                pdir = Path(td) / "players"
                odir = Path(td) / "out"
                pdir.mkdir()
                odir.mkdir()
                for fname, content in yamls.items():
                    (pdir / fname).write_text(content, encoding="utf-8")
                args = types.SimpleNamespace(
                    weights_file_path="",
                    sameoptions=False,
                    player_files_path=str(pdir),
                    seed=seed,
                    multi=len(yamls),
                    spoiler=1,
                    outputpath=str(odir),
                    allow_quantity=False,
                    race=False,
                    meta_file_path="",
                    log_level="warning",
                    log_time=False,
                    csv_output=False,
                    plando=None,
                    skip_prog_balancing=False,
                    skip_output=False,
                    spoiler_only=False,
                )
                erargs, rolled_seed = gen_main(args)
                mw = run_main(erargs, rolled_seed)
            finally:
                os.chdir(prev_cwd)
            archives = list(odir.glob("*.zip"))
            assert len(archives) == 1, (label, archives)
            with zipfile.ZipFile(archives[0]) as seed_zip:
                assert seed_zip.testzip() is None
                assert any(name.endswith(".archipelago") for name in seed_zip.namelist()), label
            for p in mw.player_ids:
                w = mw.worlds[p]
                sd = w.fill_slot_data()
                if w.game != "Hollow Knight x Silksong":
                    continue
                if label == "solo_hollow_knight_only":
                    from BaseClasses import CollectionState

                    costs = sd["hollow_knight"]["location_costs"]
                    for shop, currency, low, high in (
                        ("Grubfather", "GRUBS", 15, 23),
                        ("Iselda", "GEO", 100, 180),
                    ):
                        prices = [
                            costs[f"{shop}_{i}"][currency]
                            for i in range(1, 9)
                        ]
                        assert low <= min(prices) < max(prices) <= high, (shop, prices)
                        assert prices == sorted(prices), (shop, prices)

                    spike_grub = mw.get_location("HK Grub-Crossroads_Spike", p)
                    events = [l for l in mw.get_locations(p) if l.address is None]
                    right_state = CollectionState(mw)
                    right_state.collect(w.create_item("HK Right_Mothwing_Cloak"), True)
                    right_state.sweep_for_advancements(locations=events)
                    assert not spike_grub.can_reach(right_state), "Crossroads spike grub must not be reachable with only right dash"

                    left_state = CollectionState(mw)
                    left_state.collect(w.create_item("HK Left_Mothwing_Cloak"), True)
                    left_state.sweep_for_advancements(locations=events)
                    assert spike_grub.can_reach(left_state), "Crossroads spike grub must be reachable with left dash"
                for loc in mw.get_locations(p):
                    if loc.address is not None:
                        if loc.name.startswith("HK "):
                            assert HK_BLOCK_START <= loc.address <= HK_BLOCK_END, loc
                        elif loc.name.startswith("SS "):
                            assert SS_BLOCK_START <= loc.address <= SS_BLOCK_END, loc
                        else:
                            raise AssertionError(f"Unprefixed real location: {loc.name}")
                    if loc.item and loc.item.player == p and loc.item.code is not None:
                        it = loc.item
                        if it.name.startswith("HK "):
                            assert HK_BLOCK_START <= it.code <= HK_BLOCK_END, it
                        elif it.name.startswith("SS "):
                            assert SS_BLOCK_START <= it.code <= SS_BLOCK_END, it
                        elif it.name in CROSS_GAME_ITEMS_BY_NAME:
                            assert COMBO_BLOCK_START <= it.code <= COMBO_BLOCK_END, it
                        else:
                            raise AssertionError(f"Unprefixed real item: {it.name}")
                own_item_names = {
                    l.item.name for l in mw.get_locations() if l.item and l.item.player == p
                }
                for early in (mw.early_items[p], mw.local_early_items[p]):
                    missing = [name for name in early if name not in own_item_names]
                    assert not missing, f"Early items not in the pool: {missing}"
                    if w.has_hk and w.has_silksong and not w._early_items_released:
                        in_pharloom = [
                            l.name for l in mw.get_locations(p)
                            if l.item and l.item.player == p and l.item.name in early and l.name.startswith("SS ")
                        ]
                        assert not in_pharloom, f"Early items placed in Pharloom: {in_pharloom}"
                if label == "solo_both_unified_shuffles":
                    hk_sd = sd["hollow_knight"]
                    ss_sd = sd["silksong"]
                    hk_pairs = hk_sd.get("entrance_pairs") or {}
                    assert len(hk_pairs) >= 10, hk_pairs
                    assert all(hk_pairs.get(dst) == src for src, dst in hk_pairs.items()), "HK entrance pairs must be reciprocal"
                    assert hk_sd["options"]["EntranceRandoType"] == 5
                    assert hk_sd["options"]["RandomizeCharms"] == 1
                    assert hk_sd["options"]["RandomizeGrubs"] == 1
                    assert hk_sd["options"]["RandomizeJournalEntries"] == 1
                    assert hk_sd["options"]["RandomizeLoreTablets"] == 1
                    assert hk_sd["options"]["CostSanity"] == 2
                    assert ss_sd["entrance_randomization"] == "coupled"
                    assert ss_sd["entrance_randomization_scope"] == "interiors"
                    assert len(ss_sd.get("entrance_pairs") or {}) > 0
                if label in ("solo_both_unified_pools_on", "solo_both_unified_pools_off"):
                    on = label.endswith("_on")
                    hk_opts = sd["hollow_knight"]["options"]
                    ss_sd = sd["silksong"]
                    # Silksong cannot generate with both skills and Bellways vanilla, so the off case
                    # leaves fast travel per game.
                    travel = ("RandomizeStags", "bellway_randomization", "ventrica_randomization")
                    for name in (
                        "RandomizeMaps", "RandomizeStags", "RandomizeMaskShards", "RandomizeVesselFragments",
                        "RandomizeRelics", "RandomizeSkills", "RandomizeFocus", "RandomizeSwim",
                    ):
                        assert hk_opts[name] == int(on or name in travel), (name, hk_opts[name])
                    for name in (
                        "map_randomization", "bellway_randomization", "ventrica_randomization",
                        "mask_shard_randomization", "spool_fragment_randomization", "relic_randomization",
                        "skill_randomization", "silk_skill_randomization",
                    ):
                        expected = "anywhere" if on or name in travel else "vanilla"
                        assert ss_sd[name] == expected, (name, ss_sd[name])
                    assert ss_sd["bind_randomization"] == ("per_crest" if on else "vanilla"), ss_sd["bind_randomization"]
                    assert ss_sd["swim_ability_rando"] is on, ss_sd["swim_ability_rando"]
                    shuffled = {
                        l.item.name for l in mw.get_locations()
                        if l.address is not None and l.item and l.item.player == p
                    }
                    for name in ("HK Focus", "HK King's_Idol", "HK Crossroads_Map", "HK Mask_Shard", "Shared Swim"):
                        assert (name in shuffled) == on, (name, on)
                    assert "HK Swim" not in shuffled and "SS Swim" not in shuffled, "Swim must only appear as Shared Swim"
                    for name in ("GeoLink", "SoulLink", "KnockbackLink"):
                        assert hk_opts[name] == int(on), (name, hk_opts[name])
                    for name in ("rosary_link", "silk_link", "knockback_link"):
                        assert ss_sd[name] is on, (name, ss_sd[name])
                    assert ss_sd["shell_shard_link"] is False, "Shell Shard Link keeps its own option"
                if label == "solo_both_unified_entrances_full":
                    hk_sd = sd["hollow_knight"]
                    ss_sd = sd["silksong"]
                    hk_pairs = hk_sd.get("entrance_pairs") or {}
                    assert len(hk_pairs) >= 40, len(hk_pairs)
                    assert all(hk_pairs.get(dst) == src for src, dst in hk_pairs.items()), "HK entrance pairs must be reciprocal"
                    assert "Town[left1]" not in hk_pairs and "Tutorial_01[right1]" not in hk_pairs
                    assert hk_sd["options"]["EntranceRandoType"] == 3
                    assert ss_sd["entrance_randomization"] == "coupled"
                    assert ss_sd["entrance_randomization_scope"] == "full"
                if label == "solo_both_seals":
                    from BaseClasses import CollectionState

                    seals = sd["cross_game_seals"]
                    assert len(seals) == 4, seals
                    assert {s["game"] for s in seals} == {"hollow_knight", "silksong"}, seals
                    for seal in seals:
                        other = "SS " if seal["game"] == "hollow_knight" else "HK "
                        assert seal["key"].startswith(other), seal
                        assert seal["key_id"] == w.item_name_to_id[seal["key"]], seal
                        assert seal["scenes"] and ("area_id" in seal) == (seal["game"] == "silksong"), seal
                    # Every progression item except one HK seal's key: that seal's checks stay out of logic.
                    seal = next(s for s in w._seals if s.game == "hollow_knight")
                    events = [l for l in mw.get_locations(p) if l.address is None]
                    state = CollectionState(mw)
                    for l in mw.get_locations(p):
                        if l.item and l.item.player == p and l.item.advancement and l.item.name != seal.key:
                            state.collect(l.item, True)
                    state.sweep_for_advancements(locations=events)
                    from worlds.hkss.cross_game import hk_scene
                    inside = [l for l in mw.get_locations(p) if l.address and l.name.startswith("HK ")
                              and hk_scene(l.name, seal.scenes) in seal.scenes]
                    assert inside and not any(l.can_reach(state) for l in inside), seal.area
                    state.collect(w.create_item(seal.key), True)
                    state.sweep_for_advancements(locations=events)
                    assert all(l.can_reach(state) for l in inside), seal.area
                if label == "solo_both":
                    if sd["cross_game_seals"] != []:
                        raise AssertionError("Seals must be off by default")
                    if sd["cross_game_items"] != []:
                        raise AssertionError("Cross-game items must be off by default")
                    if sd.get("double_steel_soul") is not False:
                        raise AssertionError("Double Steel Soul must be off by default")
                    if sd.get("start_with_full_maps") is not False:
                        raise AssertionError("Start With Full Maps must be off by default")
                    if sd["hollow_knight"]["options"].get("StartWithFullMaps") != 0:
                        raise AssertionError("HK StartWithFullMaps must be 0 by default")
                    hk_opts = sd["hollow_knight"]["options"]
                    assert hk_opts["AutomaticCompass"] == 1 and hk_opts["FasterDialogue"] == 1, hk_opts
                    assert hk_opts["EnemyGeoMultiplier"] == 1.0 and hk_opts["EarlyDash"] == 0, hk_opts
                    assert sd["silksong"]["starting_rosaries"] == 0
                    assert "SS Swift Step" in mw.local_early_items[p], dict(mw.local_early_items[p])
                if label == "solo_both_cross_game_items":
                    if sd.get("double_steel_soul") is not True:
                        raise AssertionError("Double Steel Soul must be True when enabled")
                    if sd.get("start_with_full_maps") is not True:
                        raise AssertionError("Start With Full Maps must be True when enabled")
                    if sd["hollow_knight"]["options"].get("StartWithFullMaps") != 1:
                        raise AssertionError("HK StartWithFullMaps must be 1 when enabled")
                    if sd["silksong"].get("start_with_maps") is not True:
                        raise AssertionError("Silksong start_with_maps must be True when start_with_full_maps is enabled")
                    if sd["silksong"].get("start_fully_mapped") is not True:
                        raise AssertionError("Silksong start_fully_mapped must be True when start_with_full_maps is enabled")
                    hk_opts = sd["hollow_knight"]["options"]
                    ss_sd = sd["silksong"]
                    assert hk_opts["AutomaticCompass"] == 0 and ss_sd["automatic_compass"] is False
                    assert hk_opts["FasterDialogue"] == 0 and ss_sd["faster_dialogue"] is False
                    assert hk_opts["EnemyGeoMultiplier"] == 3.0 and ss_sd["enemy_rosary_multiplier"] == "x3"
                    assert hk_opts["StartingGeo"] == 250 and ss_sd["starting_rosaries"] == 250
                    assert hk_opts["EarlyDash"] == 1 and ss_sd["early_sprint"] is True
                    assert dict(mw.local_early_items[p]).get("Shared Dash") == 1, dict(mw.local_early_items[p])
                    from BaseClasses import CollectionState

                    shared_names = {entry["name"] for entry in sd["cross_game_items"]}
                    assert shared_names == {"Shared Dash", "Shared Wall Jump", "Shared Double Jump"}, shared_names
                    placed_names = {
                        l.item.name for l in mw.get_locations(p)
                        if l.address is not None and l.item and l.item.player == p
                    }
                    for name in ("Shared Dash", "Shared Wall Jump", "Shared Double Jump"):
                        assert name in placed_names, name
                    for replaced in (
                        "HK Mothwing_Cloak", "SS Swift Step",
                        "HK Mantis_Claw", "SS Cling Grip",
                        "HK Monarch_Wings", "SS Faydown Cloak",
                    ):
                        assert replaced not in placed_names, replaced
                    state = CollectionState(mw)
                    state.collect(w.create_item("Shared Dash"), True)
                    assert state.has("HK Mothwing_Cloak", p)
                    assert state.has("Mothwing_Cloak", p)
                    assert state.has("SS Swift Step", p)
                    assert state.has("Swift Step", p)
                if label == "solo_both_cross_game_items_split":
                    shared_names = {entry["name"] for entry in sd["cross_game_items"]}
                    assert shared_names == {
                        "Shared Double Jump",
                        "Shared Swim",
                        "Shared Dash (Left)",
                        "Shared Dash (Right)",
                        "Shared Wall Jump (Left)",
                        "Shared Wall Jump (Right)",
                    }, shared_names
                if label == "solo_both_hk_alt_start":
                    from BaseClasses import CollectionState

                    hk_sd = sd["hollow_knight"]
                    assert hk_sd["options"]["StartLocationName"] == "Greenpath", hk_sd["options"]
                    assert all(s["area"] != "Greenpath" for s in sd["cross_game_seals"]), sd["cross_game_seals"]
                    events = [l for l in mw.get_locations(p) if l.address is None]
                    empty = CollectionState(mw)
                    empty.sweep_for_advancements(locations=events)
                    assert not empty.has("Town", p), "Greenpath start should not reach Dirtmouth at sphere 0"
                    ss_locs = [l for l in mw.get_locations(p) if l.address is not None and l.name.startswith("SS ")]
                    assert ss_locs and not any(l.can_reach(empty) for l in ss_locs), "Pharloom must stay locked until Dirtmouth is reached"
                if label == "solo_both_ss_alt_start":
                    assert sd["silksong"]["starting_location"] == "bellhart", sd["silksong"]["starting_location"]
                if label == "solo_both_ss_alt_start_entrances":
                    from BaseClasses import CollectionState
                    from worlds.hkss.silksong.entrance_randomization import POOL

                    ss_sd = sd["silksong"]
                    assert ss_sd["starting_location"] == "wormways", ss_sd["starting_location"]
                    assert sd["hollow_knight"]["options"]["StartLocationName"] == "Greenpath"
                    route = {s: t for s, t in (ss_sd.get("entrance_pairs") or {}).items()
                             if s.startswith(("moss-grotto/", "bone-bottom/"))}
                    assert route and any(t != POOL[s]["vanilla"] for s, t in route.items()), \
                        "The Moss Grotto to Bone Bottom route should be shuffled with full entrances"
                    events = [l for l in mw.get_locations(p) if l.address is None]
                    empty = CollectionState(mw)
                    empty.sweep_for_advancements(locations=events)
                    ss_locs = [l for l in mw.get_locations(p) if l.address is not None and l.name.startswith("SS ")]
                    assert not empty.has("Town", p) and not any(l.can_reach(empty) for l in ss_locs), \
                        "Pharloom must stay locked until Dirtmouth is reached"
                if label == "solo_both_hk_alt_start_edge":
                    hk_sd = sd["hollow_knight"]
                    assert hk_sd["options"]["StartLocationName"] == "Outside Colosseum", hk_sd["options"]
                    assert all(s["area"] != "Kingdom's Edge" for s in sd["cross_game_seals"]), sd["cross_game_seals"]
                if sd["games"] == "both" and mw.players == 1:
                    hk_in_ss = sum(
                        1 for l in mw.get_locations(p)
                        if l.name.startswith("SS ") and l.item and l.item.name.startswith("HK ")
                    )
                    ss_in_hk = sum(
                        1 for l in mw.get_locations(p)
                        if l.name.startswith("HK ") and l.item and l.item.name.startswith("SS ")
                    )
                    assert hk_in_ss > 0 and ss_in_hk > 0, (hk_in_ss, ss_in_hk)
            print(f"PASS {label} (players={mw.players}, items={len(mw.get_items())}, locs={len(mw.get_locations())})")

    for label, yamls in CASES:
        run_case(label, yamls)



# Each case is one generation: (label, {player file: yaml}).
CASES: list[tuple[str, dict[str, str]]] = [
    ("solo_hollow_knight_only", {
        "Knight.yaml": (
            "name: Knight\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: hollow_knight_only\n"
            "  SplitMothwingCloak: true\n"
            "  RandomizeGrubs: true\n"
            # Both price pairs are valid individual rolls but inverted as a pair.
            "  MinimumGrubPrice: 23\n"
            "  MaximumGrubPrice: 15\n"
            "  MinimumGeoPrice: 180\n"
            "  MaximumGeoPrice: 100\n"
            "  GrubfatherRewardSlots: 8\n"
            "  IseldaShopSlots: 8\n"
        )
    }),
    ("solo_silksong_only", {
        "Hornet.yaml": (
            "name: Hornet\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: silksong_only\n"
        )
    }),
    ("solo_both", {
        "Combo.yaml": (
            "name: Combo\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
        )
    }),
    ("solo_both_either", {
        "ComboEither.yaml": (
            "name: ComboEither\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  combo_goal: either\n"
        )
    }),
    ("solo_both_vanilla_ss_skills", {
        "ComboVanillaSSSkills.yaml": (
            "name: ComboVanillaSSSkills\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  skill_randomization: vanilla\n"
        )
    }),
    ("solo_both_unified_shuffles", {
        "ComboUnified.yaml": (
            "name: ComboUnified\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  shuffle_entrances: interiors\n"
            "  shuffle_charms_and_crests: on\n"
            "  shuffle_shops: randomized\n"
            "  shuffle_grubs_and_fleas: on\n"
            "  shuffle_journal_entries: on\n"
            "  shuffle_lore_tablets: on\n"
        )
    }),
    ("solo_both_unified_pools_on", {
        "ComboPoolsOn.yaml": (
            "name: ComboPoolsOn\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_items: true\n"
            "  shuffle_maps: on\n"
            "  shuffle_fast_travel: on\n"
            "  shuffle_masks_and_vessels: on\n"
            "  shuffle_relics: on\n"
            "  shuffle_skills: on\n"
            "  shuffle_heal: per_crest\n"
            "  shuffle_swim: on\n"
            "  currency_link: on\n"
            "  resource_link: on\n"
            "  combo_knockback_link: on\n"
        )
    }),
    ("solo_both_unified_pools_off", {
        "ComboPoolsOff.yaml": (
            "name: ComboPoolsOff\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_items: true\n"
            "  shuffle_maps: off\n"
            "  shuffle_masks_and_vessels: off\n"
            "  shuffle_relics: off\n"
            "  shuffle_skills: off\n"
            "  shuffle_heal: off\n"
            "  shuffle_swim: off\n"
            "  currency_link: off\n"
            "  resource_link: off\n"
            "  combo_knockback_link: off\n"
        )
    }),
    ("solo_both_unified_entrances_full", {
        "ComboEntrancesFull.yaml": (
            "name: ComboEntrancesFull\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  shuffle_entrances: full\n"
        )
    }),
    ("solo_both_seals", {
        "ComboSeals.yaml": (
            "name: ComboSeals\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_seals: 2\n"
            "  shuffle_entrances: within_areas\n"
        )
    }),
    ("solo_both_cross_game_items", {
        "ComboSharedItems.yaml": (
            "name: ComboSharedItems\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_items: true\n"
            "  double_steel_soul: true\n"
            "  start_with_full_maps: true\n"
            "  combo_automatic_compass: off\n"
            "  combo_faster_dialogue: off\n"
            "  enemy_currency_multiplier: x3\n"
            "  early_dash: on\n"
            "  starting_currency: 250\n"
            "  cross_game_seals: 2\n"
            "  shuffle_entrances: within_areas\n"
        )
    }),
    ("solo_both_cross_game_items_split", {
        "ComboSharedSplit.yaml": (
            "name: ComboSharedSplit\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_items: true\n"
            "  SplitMothwingCloak: true\n"
            "  SplitMantisClaw: true\n"
            "  RandomizeSwim: true\n"
            "  directional_swift_step: true\n"
            "  directional_cling_grip: true\n"
            "  swim_ability_rando: true\n"
        )
    }),
    ("solo_both_hk_alt_start", {
        "ComboAltStartGreenpath.yaml": (
            "name: ComboAltStartGreenpath\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  StartLocation: greenpath\n"
            "  cross_game_seals: 2\n"
            "  shuffle_entrances: within_areas\n"
        )
    }),
    ("solo_both_hk_alt_start_edge", {
        "ComboAltStartEdge.yaml": (
            "name: ComboAltStartEdge\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  StartLocation: outside_colosseum\n"
            "  cross_game_seals: 4\n"
            "  shuffle_entrances: full\n"
        )
    }),
    ("solo_both_ss_alt_start", {
        "ComboSsAltStart.yaml": (
            "name: ComboSsAltStart\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  starting_location: bellhart\n"
            "  cross_game_seals: 2\n"
        )
    }),
    ("solo_both_ss_alt_start_entrances", {
        "ComboSsAltStartEr.yaml": (
            "name: ComboSsAltStartEr\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  starting_location: wormways\n"
            "  StartLocation: greenpath\n"
            "  cross_game_seals: 2\n"
            "  shuffle_entrances: full\n"
        )
    }),
    ("solo_both_extra_checks", {
        "ComboExtraChecks.yaml": (
            "name: ComboExtraChecks\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  shuffle_levers: on\n"
            "  shuffle_breakable_walls: on\n"
            "  shuffle_grass: on\n"
            "  RandomizeNail: true\n"
            "  WhitePalace: include\n"
            "  cross_game_seals: 2\n"
        )
    }),
    ("solo_both_extra_checks_act2", {
        "ComboExtraChecksAct2.yaml": (
            "name: ComboExtraChecksAct2\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  goal: act_2\n"
            "  shuffle_levers: on\n"
            "  shuffle_breakable_walls: on\n"
            "  shuffle_grass: on\n"
        )
    }),
    ("multiworld_4p", {
        "P1_Both.yaml": (
            "name: P1_Both\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
        ),
        "P2_HK.yaml": (
            "name: P2_HK\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: hollow_knight_only\n"
        ),
        "P3_SS.yaml": (
            "name: P3_SS\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: silksong_only\n"
        ),
        "P4_VanillaHK.yaml": (
            "name: P4_VanillaHK\n"
            "game: Hollow Knight\n"
            "Hollow Knight: {}\n"
        ),
    }),
]


# Slowest cases start first so the parallel run ends sooner.
SLOW_CASES = (
    "solo_both_hk_alt_start",
    "solo_both_seals",
    "solo_both_cross_game_items",
    "solo_both_ss_alt_start_entrances",
    "solo_both_hk_alt_start_edge",
    "solo_both_unified_entrances_full",
)


def run_parallel(labels: list[str], args: argparse.Namespace) -> None:
    """Run each case in its own process; Archipelago's global state rules out threads."""
    labels = sorted(labels, key=lambda label: SLOW_CASES.index(label) if label in SLOW_CASES else len(SLOW_CASES))
    env = dict(os.environ, HKSS_TEST_CASE_ONLY="1")
    env.pop("HKSS_TEST_ONLY", None)

    def run(label: str) -> tuple[str, int, str, float]:
        start = time.monotonic()
        cmd = [
            sys.executable, str(Path(__file__).resolve()),
            "--ap-dir", str(args.ap_dir), "--apworld", str(args.apworld),
            "--seed", str(args.seed), "--only", label, "--jobs", "1",
        ]
        proc = subprocess.run(cmd, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
        return label, proc.returncode, proc.stdout + proc.stderr, time.monotonic() - start

    failed = []
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for future in as_completed([pool.submit(run, label) for label in labels]):
            label, code, output, seconds = future.result()
            if code == 0:
                line = next((l for l in output.splitlines() if l.startswith(f"PASS {label}")), f"PASS {label}")
                print(f"{line} [{seconds:.0f}s]", flush=True)
            else:
                failed.append(label)
                print(f"FAIL {label} (exit {code})\n{output}", flush=True)
    if failed:
        raise SystemExit(f"{len(failed)} case(s) failed: {', '.join(failed)}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ap-dir", type=Path, default=DEFAULT_AP_DIR)
    parser.add_argument("--apworld", type=Path, default=DEFAULT_APWORLD)
    parser.add_argument("--seed", type=int, default=12345)
    parser.add_argument("--only", default="", help="Comma-separated case labels to run (default: all).")
    parser.add_argument(
        "--jobs", type=int, default=min(8, max(1, (os.cpu_count() or 2) // 2)),
        help="Cases to generate at once, each in its own process (default: half the cores, at most 8).",
    )
    args = parser.parse_args()
    # Case output can hold characters the Windows console code page cannot encode.
    sys.stdout.reconfigure(errors="replace")
    labels = [label for label, _ in CASES]
    if args.only:
        wanted = [name for name in args.only.split(",") if name]
        unknown = sorted(set(wanted) - set(labels))
        assert not unknown, f"Unknown case(s): {', '.join(unknown)}"
        labels = [label for label in labels if label in wanted]
        os.environ["HKSS_TEST_ONLY"] = ",".join(labels)

    if not os.environ.get("HKSS_TEST_CASE_ONLY"):
        check_id_space_mirror()
        check_apworld_archive(args.apworld)
    if args.jobs > 1 and len(labels) > 1:
        run_parallel(labels, args)
    else:
        run_generation_suite(args.ap_dir, args.apworld, args.seed)


if __name__ == "__main__":
    main()

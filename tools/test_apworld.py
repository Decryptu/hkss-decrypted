"""Verify the combined Hollow Knight x Silksong APWorld against Archipelago 0.6.8."""

from __future__ import annotations

import argparse
import importlib.util
import logging
import os
import re
import shutil
import sys
import tempfile
import types
import zipfile
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
            "hkss/cross_game_areas.json",
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
        from worlds.hkss.ids import HK_BLOCK_END, HK_BLOCK_START, SS_BLOCK_END, SS_BLOCK_START

        _run_cases(gen_main, run_main, seed, HK_BLOCK_START, HK_BLOCK_END, SS_BLOCK_START, SS_BLOCK_END)


def _run_cases(gen_main, run_main, seed: int, HK_BLOCK_START: int, HK_BLOCK_END: int, SS_BLOCK_START: int, SS_BLOCK_END: int) -> None:

    def run_case(label: str, yamls: dict[str, str]) -> None:
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
                        else:
                            raise AssertionError(f"Unprefixed real item: {it.name}")
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
                if label == "solo_both" and sd["cross_game_seals"] != []:
                    raise AssertionError("Seals must be off by default")
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

    run_case("solo_hollow_knight_only", {
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
    })
    run_case("solo_silksong_only", {
        "Hornet.yaml": (
            "name: Hornet\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: silksong_only\n"
        )
    })
    run_case("solo_both", {
        "Combo.yaml": (
            "name: Combo\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
        )
    })
    run_case("solo_both_either", {
        "ComboEither.yaml": (
            "name: ComboEither\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  combo_goal: either\n"
        )
    })
    run_case("solo_both_vanilla_ss_skills", {
        "ComboVanillaSSSkills.yaml": (
            "name: ComboVanillaSSSkills\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  skill_randomization: vanilla\n"
        )
    })
    run_case("solo_both_unified_shuffles", {
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
    })
    run_case("solo_both_unified_entrances_full", {
        "ComboEntrancesFull.yaml": (
            "name: ComboEntrancesFull\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  shuffle_entrances: full\n"
        )
    })
    run_case("solo_both_seals", {
        "ComboSeals.yaml": (
            "name: ComboSeals\n"
            "game: Hollow Knight x Silksong\n"
            "Hollow Knight x Silksong:\n"
            "  games: both\n"
            "  cross_game_seals: 2\n"
            "  shuffle_entrances: within_areas\n"
        )
    })
    run_case("multiworld_4p", {
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
    })


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ap-dir", type=Path, default=DEFAULT_AP_DIR)
    parser.add_argument("--apworld", type=Path, default=DEFAULT_APWORLD)
    parser.add_argument("--seed", type=int, default=12345)
    args = parser.parse_args()

    check_id_space_mirror()
    check_apworld_archive(args.apworld)
    run_generation_suite(args.ap_dir, args.apworld, args.seed)


if __name__ == "__main__":
    main()

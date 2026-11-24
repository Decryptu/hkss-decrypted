"""Verify the combined Hollow Knight x Silksong APWorld against Archipelago 0.6.8."""

from __future__ import annotations

import argparse
import importlib.util
import logging
import os
import re
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
            "hkss/archipelago.json",
            "hkss/VENDORED.json",
            "hkss/hk/__init__.py",
            "hkss/silksong/__init__.py",
        ):
            assert required in names, f"Missing {required} in {apworld_path}"
    print(f"PASS archive ({apworld_path.name}, {len(names)} files)")


def run_generation_suite(ap_dir: Path, seed: int) -> None:
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
    sys.path.insert(0, str(REPO_ROOT / "apworld"))

    with tempfile.TemporaryDirectory() as settings_td:
        import Utils

        Utils.user_path = lambda *parts: str(Path(settings_td, *parts))
        import hkss  # noqa: F401
        from Generate import main as gen_main
        from Main import main as run_main
        from hkss.ids import HK_BLOCK_END, HK_BLOCK_START, SS_BLOCK_END, SS_BLOCK_START

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
                    spoiler=2,
                    outputpath=str(odir),
                    allow_quantity=False,
                    race=False,
                    meta_file_path="",
                    log_level="warning",
                    log_time=False,
                    csv_output=False,
                    plando=None,
                    skip_prog_balancing=False,
                    skip_output=True,
                    spoiler_only=False,
                )
                erargs, rolled_seed = gen_main(args)
                mw = run_main(erargs, rolled_seed)
            finally:
                os.chdir(prev_cwd)
            for p in mw.player_ids:
                w = mw.worlds[p]
                sd = w.fill_slot_data()
                if w.game != "Hollow Knight x Silksong":
                    continue
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
    run_generation_suite(args.ap_dir, args.seed)


if __name__ == "__main__":
    main()

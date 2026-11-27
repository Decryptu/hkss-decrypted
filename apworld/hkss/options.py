"""Combined option set for Hollow Knight x Silksong.

Three layers:
1. Shared combo options (games, combo_goal, death_link)
2. Hollow Knight options (from vendored worlds/hk)
3. Silksong options (from vendored silksong)
"""

from __future__ import annotations

from dataclasses import fields, make_dataclass
from typing import get_type_hints

import Options as _Options
from Options import Choice, DeathLink, OptionGroup, PerGameCommonOptions, Range, Toggle

from .hk.Options import hollow_knight_options
from .silksong import options as _ss_options_mod
from .silksong.options import SilksongOptions, silksong_option_groups


class Games(Choice):
    """Which games are included in this slot."""

    display_name = "Games"
    option_both = 0
    option_hollow_knight_only = 1
    option_silksong_only = 2
    default = 0


class ComboGoal(Choice):
    """How completion is determined when both games are enabled."""

    display_name = "Combo Goal"
    option_both = 0
    option_either = 1
    default = 0


class ShuffleEntrances(Choice):
    """Unified coupled entrance and room randomization across both games.

    per_game: use each game's own entrance setting.
    off: keep all entrances vanilla in both games.
    interiors: shuffle interior doorway pairs in both games.
    within_areas: shuffle coupled room transitions inside each area.
    full: shuffle coupled room transitions across each game's world.
    """

    display_name = "Shuffle Entrances"
    option_per_game = 0
    option_off = 1
    option_interiors = 2
    option_within_areas = 3
    option_full = 4
    default = 0


class ShuffleCharmsAndCrests(Choice):
    """Unified randomization for Hollow Knight Charms and Silksong Crests & Tools.

    per_game: use each game's individual option.
    off: keep Charms, Crests, and Tools vanilla.
    on: randomize Charms, Crests, and Tools into the shared item pool.
    """

    display_name = "Shuffle Charms & Crests"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleShops(Choice):
    """Unified shop price/cost randomization across both games.

    per_game: use each game's individual shop options.
    vanilla: keep shop costs and prices vanilla in both games.
    randomized: enable Hollow Knight shop Costsanity and Silksong shop price shuffling.
    """

    display_name = "Shuffle Shops"
    option_per_game = 0
    option_vanilla = 1
    option_randomized = 2
    default = 0


class ShuffleGrubsAndFleas(Choice):
    """Unified randomization for Hollow Knight Grubs and Silksong Fleas.

    per_game: use each game's individual option.
    off: keep Grubs and Fleas vanilla.
    on: randomize Grubs and Fleas into the shared item pool.
    """

    display_name = "Shuffle Grubs & Fleas"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleJournalEntries(Choice):
    """Unified randomization for Hunter's Journal entries in both games.

    per_game: use each game's individual option.
    off: keep journal entries vanilla.
    on: randomize journal entries into the shared item pool in both games.
    """

    display_name = "Shuffle Journal Entries"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleLoreTablets(Choice):
    """Unified randomization for Lore Tablets in both games.

    per_game: use each game's individual option.
    off: keep lore tablets vanilla.
    on: randomize lore tablets into the shared item pool in both games.
    """

    display_name = "Shuffle Lore Tablets"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleLevers(Choice):
    """Unified randomization for Levers in both games.

    per_game: use each game's individual lever option.
    off: keep levers as normal non-check switches.
    on: pulling levers in either game sends a check from the shared item pool.
    """

    display_name = "Shuffle Levers"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleBreakableWalls(Choice):
    """Unified randomization for Breakable Walls in both games.

    per_game: use each game's individual breakable wall option.
    off: keep breakable walls as normal non-check obstacles.
    on: breaking breakable walls in either game sends a check from the shared item pool.
    """

    display_name = "Shuffle Breakable Walls"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class ShuffleGrass(Choice):
    """Unified randomization for Grass in both games.

    per_game: use each game's individual grass option.
    off: keep grass as normal scenery.
    on: cutting grass in a grass-bearing room sends a check from the shared item pool.
    """

    display_name = "Shuffle Grass"
    option_per_game = 0
    option_off = 1
    option_on = 2
    default = 0


class RandomizeLevers(Toggle):
    """Add Hollow Knight levers as checks in the shared item pool.

    Levers still open their normal gates or shortcuts when hit, and also send a check.
    """

    display_name = "Randomize Levers"
    default = False


class RandomizeBreakableWalls(Toggle):
    """Add Hollow Knight breakable walls and quake floors as checks in the shared item pool.

    Walls still break normally when hit, and also send a check.
    """

    display_name = "Randomize Breakable Walls"
    default = False


class RandomizeGrass(Toggle):
    """Add one check per Hollow Knight room that contains cuttable grass.

    Cutting any grass in a room sends that room's grass check.
    """

    display_name = "Randomize Grass"
    default = False


class LeverRandomization(Toggle):
    """Add Silksong levers as checks in the shared item pool.

    Levers still activate their normal gates or mechanisms when hit, and also send a check.
    """

    display_name = "Lever Randomization"
    default = False


class BreakableWallRandomization(Toggle):
    """Add Silksong breakable walls as checks in the shared item pool.

    Walls still break normally when hit, and also send a check.
    """

    display_name = "Breakable Wall Randomization"
    default = False


class GrassRandomization(Toggle):
    """Add one check per Silksong room that contains cuttable grass.

    Cutting any grass in a room sends that room's grass check.
    """

    display_name = "Grass Randomization"
    default = False


class CrossGameSeals(Range):
    """Number of areas in each game that stay sealed until you find a key ability from the other game.

    For example, Deepnest may need Silksong's Faydown Cloak, and Shellwood may need Hollow Knight's
    Mantis Claw. Doorways into a sealed area turn you back until the key has been received. The
    randomizer's logic expects the key before anything in the area. Starting areas, travel gate
    towns, early hubs, and Silksong's Act 3 areas are never sealed.

    0 turns seals off. Only used when both games are in the slot.
    """

    display_name = "Cross-Game Seals"
    range_start = 0
    range_end = 4
    default = 0


class CrossGameItems(Toggle):
    """Merge paired movement abilities into single shared items that unlock in both games.

    For example, Shared Dash grants both Mothwing Cloak (Hallownest) and Swift Step (Pharloom),
    Shared Wall Jump grants Mantis Claw and Cling Grip, Shared Double Jump grants Monarch Wings
    and Faydown Cloak, and Shared Swim grants Swim when randomized in both games. Directional
    splits are also merged when matching split options are enabled in both games.

    Off by default. Only used when both games are in the slot.
    """

    display_name = "Cross-Game Items"
    default = False


class DoubleSteelSoul(Toggle):
    """Play both games in Steel Soul mode for one combo run.

    A Steel Soul death in either Hallownest or Pharloom marks the combo run defeated across both
    games (even while the other game is closed). Both games and the launcher surface the defeat,
    and the launcher can reset the run with fresh saves in both games.

    Off by default.
    """

    display_name = "Double Steel Soul"
    default = False


class StartWithFullMaps(Toggle):
    """Start with every area map unlocked and every room already revealed in both games.

    In Hollow Knight, unlocks the map panel, Quill, all area maps, and reveals every room on the
    map from the start without needing to buy maps from Cornifer or Iselda.
    In Silksong, enables both Start With Maps and Start Fully Mapped.

    Off by default (when off, Silksong still respects its own map start options).
    """

    display_name = "Start With Full Maps"
    default = False


_COMMON_FIELD_NAMES = {f.name for f in fields(PerGameCommonOptions)}

_shared_fields = [
    ("games", Games),
    ("combo_goal", ComboGoal),
    ("death_link", DeathLink),
    ("shuffle_entrances", ShuffleEntrances),
    ("shuffle_charms_and_crests", ShuffleCharmsAndCrests),
    ("shuffle_shops", ShuffleShops),
    ("shuffle_grubs_and_fleas", ShuffleGrubsAndFleas),
    ("shuffle_journal_entries", ShuffleJournalEntries),
    ("shuffle_lore_tablets", ShuffleLoreTablets),
    ("shuffle_levers", ShuffleLevers),
    ("shuffle_breakable_walls", ShuffleBreakableWalls),
    ("shuffle_grass", ShuffleGrass),
    ("cross_game_seals", CrossGameSeals),
    ("cross_game_items", CrossGameItems),
    ("double_steel_soul", DoubleSteelSoul),
    ("start_with_full_maps", StartWithFullMaps),
]

_hk_extra_fields = [
    ("RandomizeLevers", RandomizeLevers),
    ("RandomizeBreakableWalls", RandomizeBreakableWalls),
    ("RandomizeGrass", RandomizeGrass),
]

_hk_fields = [
    (name, opt_cls)
    for name, opt_cls in hollow_knight_options.items()
    if name != "DeathLink" and name not in _COMMON_FIELD_NAMES
] + _hk_extra_fields

_ss_type_hints = get_type_hints(SilksongOptions, globalns={**vars(_Options), **vars(_ss_options_mod)})

_ss_extra_fields = [
    ("lever_randomization", LeverRandomization),
    ("breakable_wall_randomization", BreakableWallRandomization),
    ("grass_randomization", GrassRandomization),
]

_ss_fields = [
    (f.name, _ss_type_hints[f.name])
    for f in fields(SilksongOptions)
    if f.name != "death_link" and f.name not in _COMMON_FIELD_NAMES
] + _ss_extra_fields

HKSSOptions = make_dataclass(
    "HKSSOptions",
    _shared_fields + _hk_fields + _ss_fields,
    bases=(PerGameCommonOptions,),
)


def _build_option_groups() -> list[OptionGroup]:
    groups: list[OptionGroup] = [
        OptionGroup(
            "Combo",
            [
                Games,
                ComboGoal,
                DeathLink,
                ShuffleEntrances,
                ShuffleCharmsAndCrests,
                ShuffleShops,
                ShuffleGrubsAndFleas,
                ShuffleJournalEntries,
                ShuffleLoreTablets,
                ShuffleLevers,
                ShuffleBreakableWalls,
                ShuffleGrass,
                CrossGameSeals,
                CrossGameItems,
                DoubleSteelSoul,
                StartWithFullMaps,
            ],
        ),
        OptionGroup(
            "Hollow Knight",
            [opt_cls for _, opt_cls in _hk_fields],
            start_collapsed=True,
        ),
    ]
    ss_extra_added = False
    for group in silksong_option_groups:
        filtered = [opt for opt in group.options if opt is not DeathLink]
        if group.name == "Randomization Options":
            filtered.extend(opt_cls for _, opt_cls in _ss_extra_fields)
            ss_extra_added = True
        if filtered:
            groups.append(
                OptionGroup(
                    f"Silksong: {group.name}",
                    filtered,
                    start_collapsed=True,
                )
            )
    if not ss_extra_added:
        groups.append(
            OptionGroup(
                "Silksong: Randomization Options",
                [opt_cls for _, opt_cls in _ss_extra_fields],
                start_collapsed=True,
            )
        )
    return groups


hkss_option_groups = _build_option_groups()

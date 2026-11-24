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
from Options import Choice, DeathLink, OptionGroup, PerGameCommonOptions

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


_COMMON_FIELD_NAMES = {f.name for f in fields(PerGameCommonOptions)}

_shared_fields = [
    ("games", Games),
    ("combo_goal", ComboGoal),
    ("death_link", DeathLink),
]

_hk_fields = [
    (name, opt_cls)
    for name, opt_cls in hollow_knight_options.items()
    if name != "DeathLink" and name not in _COMMON_FIELD_NAMES
]

_ss_type_hints = get_type_hints(SilksongOptions, globalns={**vars(_Options), **vars(_ss_options_mod)})

_ss_fields = [
    (f.name, _ss_type_hints[f.name])
    for f in fields(SilksongOptions)
    if f.name != "death_link" and f.name not in _COMMON_FIELD_NAMES
]

HKSSOptions = make_dataclass(
    "HKSSOptions",
    _shared_fields + _hk_fields + _ss_fields,
    bases=(PerGameCommonOptions,),
)


def _build_option_groups() -> list[OptionGroup]:
    groups: list[OptionGroup] = [
        OptionGroup("Combo", [Games, ComboGoal, DeathLink]),
        OptionGroup(
            "Hollow Knight",
            [opt_cls for _, opt_cls in _hk_fields],
            start_collapsed=True,
        ),
    ]
    for group in silksong_option_groups:
        filtered = [opt for opt in group.options if opt is not DeathLink]
        if filtered:
            groups.append(
                OptionGroup(
                    f"Silksong: {group.name}",
                    filtered,
                    start_collapsed=True,
                )
            )
    return groups


hkss_option_groups = _build_option_groups()

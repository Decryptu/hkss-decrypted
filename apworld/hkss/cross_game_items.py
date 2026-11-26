"""Cross-game items: shared abilities that work in both Hallownest and Pharloom.

Mirrored in src/HKSS.Core/CrossGameItems.cs. Each shared item lives in the combo ID block
(84,000,000-84,099,999) and pairs one Hollow Knight progression ability with its Silksong
counterpart. When the opt-in ``cross_game_items`` option is enabled in a both-games slot, any
pair whose Hollow Knight and Silksong items are both present in the item pool is replaced by
one shared item plus one filler item to preserve pool size.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Any

from BaseClasses import Item, ItemClassification

from .ids import COMBO_BLOCK_START, GAME_NAME, hk_name, ss_name

if TYPE_CHECKING:
    from . import HKSSWorld


@dataclass(frozen=True)
class CrossGameItemSpec:
    code: int
    name: str
    hk_item: str
    ss_item: str

    def slot_data(self) -> dict[str, Any]:
        return {
            "id": self.code,
            "name": self.name,
            "hk_item": self.hk_item,
            "ss_item": self.ss_item,
        }

    def describe(self) -> str:
        hk_display = self.hk_item.replace("_", " ")
        return f"{self.name}: {hk_display} (Hallownest) + {self.ss_item} (Pharloom)"


# Fixed IDs in the combo block; order and IDs are mirrored in src/HKSS.Core/CrossGameItems.cs.
CROSS_GAME_ITEMS: tuple[CrossGameItemSpec, ...] = (
    CrossGameItemSpec(COMBO_BLOCK_START + 1, "Shared Dash", "Mothwing_Cloak", "Swift Step"),
    CrossGameItemSpec(COMBO_BLOCK_START + 2, "Shared Wall Jump", "Mantis_Claw", "Cling Grip"),
    CrossGameItemSpec(COMBO_BLOCK_START + 3, "Shared Double Jump", "Monarch_Wings", "Faydown Cloak"),
    CrossGameItemSpec(COMBO_BLOCK_START + 4, "Shared Swim", "Swim", "Swim"),
    CrossGameItemSpec(COMBO_BLOCK_START + 5, "Shared Dash (Left)", "Left_Mothwing_Cloak", "Swift Step (Left)"),
    CrossGameItemSpec(COMBO_BLOCK_START + 6, "Shared Dash (Right)", "Right_Mothwing_Cloak", "Swift Step (Right)"),
    CrossGameItemSpec(COMBO_BLOCK_START + 7, "Shared Wall Jump (Left)", "Left_Mantis_Claw", "Cling Grip (Left)"),
    CrossGameItemSpec(COMBO_BLOCK_START + 8, "Shared Wall Jump (Right)", "Right_Mantis_Claw", "Cling Grip (Right)"),
)

CROSS_GAME_ITEMS_BY_NAME: dict[str, CrossGameItemSpec] = {spec.name: spec for spec in CROSS_GAME_ITEMS}
CROSS_GAME_ITEMS_BY_ID: dict[int, CrossGameItemSpec] = {spec.code: spec for spec in CROSS_GAME_ITEMS}


class ComboItem(Item):
    game: str = GAME_NAME


def is_cross_game_item(name: str) -> bool:
    return name in CROSS_GAME_ITEMS_BY_NAME


def create_cross_game_item(spec: CrossGameItemSpec, player: int) -> ComboItem:
    return ComboItem(spec.name, ItemClassification.progression, spec.code, player)


def merge_cross_game_items(world: "HKSSWorld") -> list[CrossGameItemSpec]:
    """Replaces paired HK + SS abilities in the player's item pool with shared items.

    Each merged pair removes one HK item and one SS item from ``multiworld.itempool`` and adds
    one shared progression item plus one filler item so the total item count stays balanced.
    """
    if not (world.has_hk and world.has_silksong and world.options.cross_game_items.value):
        return []

    player = world.player
    pool = world.multiworld.itempool
    merged: list[CrossGameItemSpec] = []

    for spec in CROSS_GAME_ITEMS:
        hk_full = hk_name(spec.hk_item)
        ss_full = ss_name(spec.ss_item)

        hk_idx = next(
            (i for i, item in enumerate(pool) if item.player == player and item.name == hk_full),
            None,
        )
        if hk_idx is None:
            continue
        ss_idx = next(
            (i for i, item in enumerate(pool) if item.player == player and item.name == ss_full),
            None,
        )
        if ss_idx is None:
            continue

        # Remove higher index first so the lower index remains valid.
        for idx in sorted((hk_idx, ss_idx), reverse=True):
            pool.pop(idx)

        pool.append(create_cross_game_item(spec, player))
        pool.append(world.create_filler())
        merged.append(spec)

    return merged

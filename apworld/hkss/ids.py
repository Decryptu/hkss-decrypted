"""ID blocks and name prefixes for Hollow Knight x Silksong.

Mirrored in src/HKSS.Core/IdSpace.cs. Every upstream Hollow Knight and Silksong
item/location ID is mapped by a fixed offset into its own block so upstream data
can be resynced without renumbering.
"""

from __future__ import annotations

GAME_NAME = "Hollow Knight x Silksong"

HK_PREFIX = "HK "
SS_PREFIX = "SS "

# Shared / combo block reserved for future cross-game items and locations.
COMBO_BLOCK_START = 84_000_000
COMBO_BLOCK_END = 84_099_999

# Hollow Knight block (upstream base: 0x1000000 = 16,777,216).
HK_UPSTREAM_BASE = 0x1000000
HK_BLOCK_START = 84_100_000
HK_BLOCK_END = 84_199_999

# Silksong block (upstream base: 835,000).
SS_UPSTREAM_BASE = 835_000
SS_BLOCK_START = 84_200_000
SS_BLOCK_END = 84_299_999


def hk_id(upstream_id: int) -> int:
    mapped = HK_BLOCK_START + (upstream_id - HK_UPSTREAM_BASE)
    if not (HK_BLOCK_START <= mapped <= HK_BLOCK_END):
        raise ValueError(f"Hollow Knight upstream ID {upstream_id} maps outside HK block: {mapped}")
    return mapped


def hk_upstream_id(combined_id: int) -> int:
    if not (HK_BLOCK_START <= combined_id <= HK_BLOCK_END):
        raise ValueError(f"ID {combined_id} is not in the Hollow Knight block")
    return HK_UPSTREAM_BASE + (combined_id - HK_BLOCK_START)


def ss_id(upstream_id: int) -> int:
    mapped = SS_BLOCK_START + (upstream_id - SS_UPSTREAM_BASE)
    if not (SS_BLOCK_START <= mapped <= SS_BLOCK_END):
        raise ValueError(f"Silksong upstream ID {upstream_id} maps outside SS block: {mapped}")
    return mapped


def ss_upstream_id(combined_id: int) -> int:
    if not (SS_BLOCK_START <= combined_id <= SS_BLOCK_END):
        raise ValueError(f"ID {combined_id} is not in the Silksong block")
    return SS_UPSTREAM_BASE + (combined_id - SS_BLOCK_START)


def game_for_id(combined_id: int) -> str | None:
    if HK_BLOCK_START <= combined_id <= HK_BLOCK_END:
        return "hollow_knight"
    if SS_BLOCK_START <= combined_id <= SS_BLOCK_END:
        return "silksong"
    if COMBO_BLOCK_START <= combined_id <= COMBO_BLOCK_END:
        return "combo"
    return None


def hk_name(name: str) -> str:
    return name if name.startswith(HK_PREFIX) else f"{HK_PREFIX}{name}"


def ss_name(name: str) -> str:
    return name if name.startswith(SS_PREFIX) else f"{SS_PREFIX}{name}"


def strip_hk_prefix(name: str) -> str:
    return name[len(HK_PREFIX):] if name.startswith(HK_PREFIX) else name


def strip_ss_prefix(name: str) -> str:
    return name[len(SS_PREFIX):] if name.startswith(SS_PREFIX) else name


def strip_game_prefix(name: str) -> tuple[str | None, str]:
    if name.startswith(HK_PREFIX):
        return ("hollow_knight", name[len(HK_PREFIX):])
    if name.startswith(SS_PREFIX):
        return ("silksong", name[len(SS_PREFIX):])
    return (None, name)

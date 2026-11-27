"""Hollow Knight x Silksong combined Archipelago world."""

from __future__ import annotations

import json
import pkgutil
from typing import Any, ClassVar, Union

from BaseClasses import CollectionState, Item, MultiWorld, Region, Tutorial
from rule_builder.cached_world import CachedRuleBuilderWorld
from settings import Bool, FilePath, Group
from worlds.AutoWorld import WebWorld

from .cross_game import Seal, apply_hk_seals, apply_ss_seals, choose_seals, describe, seal_key_items
from .cross_game_items import (
    CROSS_GAME_ITEMS,
    CROSS_GAME_ITEMS_BY_NAME,
    CrossGameItemSpec,
    create_cross_game_item,
    is_cross_game_item,
    merge_cross_game_items,
)
from .extra_checks import (
    build_extra_location_groups,
    build_extra_location_name_to_id,
    create_extra_filler_items,
    create_extra_regions_and_locations,
    export_ss_extra_requirements,
    set_extra_rules,
)
from .hk import HKItem, HKWorld
from .hk.ExtractedData import connectors as HK_VANILLA_CONNECTORS
from .hk.Options import CostSanity
from .hk_entrances import SCOPE_TO_RANDO_TYPE, shuffle_solvable as shuffle_hk_entrances
from .ids import (
    GAME_NAME,
    HK_PREFIX,
    SS_PREFIX,
    hk_id,
    hk_name,
    ss_id,
    ss_name,
    strip_hk_prefix,
    strip_ss_prefix,
)
from .options import (
    ComboGoal,
    Games,
    HKSSOptions,
    ShuffleBreakableWalls,
    ShuffleCharmsAndCrests,
    ShuffleEntrances,
    ShuffleGrass,
    ShuffleGrubsAndFleas,
    ShuffleJournalEntries,
    ShuffleLevers,
    ShuffleLoreTablets,
    ShuffleShops,
    hkss_option_groups,
)
from .silksong import SilksongWorld
from .silksong.items import PROGRESSIVE_SWIFT_STEP_ITEM
from .silksong.options import (
    EntranceRandomization,
    EntranceRandomizationScope,
    GlobalRandomization,
    PurchasePriceRandomization,
)


class HKSSSettings(Group):
    class DisableMapModSpoilers(Bool):
        """Disallows the Hollow Knight map mod from showing spoiler placements."""

    class UTPackPath(FilePath):
        required = False
        ut_dialog_name = "Select Data Pack"

    disable_spoilers: Union[DisableMapModSpoilers, bool] = False
    ut_pack_path: Union[str, UTPackPath] = UTPackPath()


class HKSSWebWorld(WebWorld):
    theme = "ocean"
    option_groups = hkss_option_groups
    tutorials = [
        Tutorial(
            tutorial_name="Setup Guide",
            description="How to set up and play Hollow Knight x Silksong with Archipelago.",
            language="English",
            file_name="setup_en.md",
            link="setup/en",
            authors=["Decrypt"],
        )
    ]


def _build_item_name_to_id() -> dict[str, int]:
    table: dict[str, int] = {}
    for spec in CROSS_GAME_ITEMS:
        table[spec.name] = spec.code
    for name, code in HKWorld.item_name_to_id.items():
        if code is not None:
            table[hk_name(name)] = hk_id(code)
    for name, code in SilksongWorld.item_name_to_id.items():
        if code is not None:
            table[ss_name(name)] = ss_id(code)
    return table


def _build_location_name_to_id() -> dict[str, int]:
    table: dict[str, int] = {}
    for name, code in HKWorld.location_name_to_id.items():
        if code is not None:
            table[hk_name(name)] = hk_id(code)
    for name, code in SilksongWorld.location_name_to_id.items():
        if code is not None:
            table[ss_name(name)] = ss_id(code)
    table.update(build_extra_location_name_to_id())
    return table


def _build_item_name_groups(all_items: dict[str, int]) -> dict[str, set[str]]:
    groups: dict[str, set[str]] = {
        "Shared": {spec.name for spec in CROSS_GAME_ITEMS if spec.name in all_items},
        "Hollow Knight": {name for name in all_items if name.startswith(HK_PREFIX)},
        "Silksong": {name for name in all_items if name.startswith(SS_PREFIX)},
    }
    for group_name, members in HKWorld.item_name_groups.items():
        prefixed = {hk_name(m) for m in members if hk_name(m) in all_items}
        if prefixed:
            groups[f"HK {group_name}"] = prefixed
            if group_name != "Everything":
                groups.setdefault(group_name, set()).update(prefixed)
    for group_name, members in SilksongWorld.item_name_groups.items():
        prefixed = {ss_name(m) for m in members if ss_name(m) in all_items}
        if prefixed:
            groups[f"SS {group_name}"] = prefixed
            if group_name != "Everything":
                groups.setdefault(group_name, set()).update(prefixed)
    return groups


def _build_location_name_groups(all_locations: dict[str, int]) -> dict[str, set[str]]:
    groups: dict[str, set[str]] = {
        "Hollow Knight": {name for name in all_locations if name.startswith(HK_PREFIX)},
        "Silksong": {name for name in all_locations if name.startswith(SS_PREFIX)},
    }
    for group_name, members in SilksongWorld.location_name_groups.items():
        prefixed = {ss_name(m) for m in members if ss_name(m) in all_locations}
        if prefixed:
            groups[f"SS {group_name}"] = prefixed
            if group_name != "Everywhere":
                groups.setdefault(group_name, set()).update(prefixed)
    for group_name, members in build_extra_location_groups().items():
        filtered = {m for m in members if m in all_locations}
        if filtered:
            groups.setdefault(group_name, set()).update(filtered)
    return groups


def _load_world_version() -> str:
    raw = pkgutil.get_data(__name__, "archipelago.json")
    if raw:
        data = json.loads(raw.decode("utf-8"))
        version = str(data.get("world_version", "")).strip()
        if version:
            return version
    return "0.0.0"


WORLD_VERSION: str = _load_world_version()
_ALL_ITEMS = _build_item_name_to_id()
_ALL_LOCATIONS = _build_location_name_to_id()


class HKSSWorld(SilksongWorld, CachedRuleBuilderWorld):
    """Combo randomizer for Hollow Knight and Hollow Knight: Silksong."""

    game: str = GAME_NAME
    topology_present = True
    author = "Decrypt"
    web = HKSSWebWorld()
    options_dataclass = HKSSOptions
    options: HKSSOptions
    settings: ClassVar[HKSSSettings]
    required_client_version = (0, 6, 8)

    item_name_to_id = _ALL_ITEMS
    location_name_to_id = _ALL_LOCATIONS
    item_name_groups = _build_item_name_groups(_ALL_ITEMS)
    location_name_groups = _build_location_name_groups(_ALL_LOCATIONS)

    def __init__(self, multiworld: MultiWorld, player: int):
        CachedRuleBuilderWorld.__init__(self, multiworld, player)
        self.hk = HKWorld(multiworld, player, parent_world=self)
        self._hk_completion_condition = None
        self._silksong_completion_rule = None
        self._hk_entrance_scope: str | None = None
        self._hk_entrance_pairs: dict[str, str] = {}
        self._hk_entrance_rando_type: int = 0
        self._seals: list[Seal] = []
        self._cross_game_items: list[CrossGameItemSpec] = []
        self._extra_hk_entries: list[dict[str, Any]] = []
        self._extra_ss_entries: list[tuple[dict[str, Any], list[str]]] = []

    @property
    def has_hk(self) -> bool:
        return self.options.games.value != Games.option_silksong_only

    @property
    def has_silksong(self) -> bool:
        return self.options.games.value != Games.option_hollow_knight_only

    def _apply_unified_shuffles(self) -> None:
        opts = self.options

        if opts.shuffle_charms_and_crests.value != ShuffleCharmsAndCrests.option_per_game:
            on = opts.shuffle_charms_and_crests.value == ShuffleCharmsAndCrests.option_on
            opts.RandomizeCharms.value = int(on)
            val = GlobalRandomization.option_true if on else GlobalRandomization.option_false
            opts.crest_randomization.value = val
            opts.tool_randomization.value = val

        if opts.shuffle_shops.value != ShuffleShops.option_per_game:
            randomized = opts.shuffle_shops.value == ShuffleShops.option_randomized
            opts.CostSanity.value = CostSanity.option_shopsonly if randomized else CostSanity.option_off
            price_val = PurchasePriceRandomization.option_shuffle if randomized else PurchasePriceRandomization.option_vanilla
            opts.normal_shop_prices.value = price_val
            opts.map_prices.value = price_val
            opts.pin_prices.value = price_val
            opts.upgrade_prices.value = price_val

        if opts.shuffle_grubs_and_fleas.value != ShuffleGrubsAndFleas.option_per_game:
            on = opts.shuffle_grubs_and_fleas.value == ShuffleGrubsAndFleas.option_on
            opts.RandomizeGrubs.value = int(on)
            opts.flea_randomization.value = GlobalRandomization.option_true if on else GlobalRandomization.option_false

        if opts.shuffle_journal_entries.value != ShuffleJournalEntries.option_per_game:
            on = opts.shuffle_journal_entries.value == ShuffleJournalEntries.option_on
            opts.RandomizeJournalEntries.value = int(on)
            opts.journal_randomization.value = GlobalRandomization.option_true if on else GlobalRandomization.option_false

        if opts.shuffle_lore_tablets.value != ShuffleLoreTablets.option_per_game:
            on = opts.shuffle_lore_tablets.value == ShuffleLoreTablets.option_on
            opts.RandomizeLoreTablets.value = int(on)
            opts.lore_tablet_randomization.value = GlobalRandomization.option_true if on else GlobalRandomization.option_false

        if opts.shuffle_levers.value != ShuffleLevers.option_per_game:
            on = opts.shuffle_levers.value == ShuffleLevers.option_on
            opts.RandomizeLevers.value = int(on)
            opts.lever_randomization.value = int(on)

        if opts.shuffle_breakable_walls.value != ShuffleBreakableWalls.option_per_game:
            on = opts.shuffle_breakable_walls.value == ShuffleBreakableWalls.option_on
            opts.RandomizeBreakableWalls.value = int(on)
            opts.breakable_wall_randomization.value = int(on)

        if opts.shuffle_grass.value != ShuffleGrass.option_per_game:
            on = opts.shuffle_grass.value == ShuffleGrass.option_on
            opts.RandomizeGrass.value = int(on)
            opts.grass_randomization.value = int(on)

        if opts.start_with_full_maps.value:
            opts.start_with_maps.value = 1
            opts.start_fully_mapped.value = 1

        ent = opts.shuffle_entrances.value
        if ent == ShuffleEntrances.option_off:
            opts.entrance_randomization.value = EntranceRandomization.option_off
            self._hk_entrance_scope = None
        elif ent in (
            ShuffleEntrances.option_interiors,
            ShuffleEntrances.option_within_areas,
            ShuffleEntrances.option_full,
        ):
            scope_map = {
                ShuffleEntrances.option_interiors: ("interiors", EntranceRandomizationScope.option_interiors),
                ShuffleEntrances.option_within_areas: ("within_areas", EntranceRandomizationScope.option_within_areas),
                ShuffleEntrances.option_full: ("full", EntranceRandomizationScope.option_full),
            }
            hk_scope, ss_scope = scope_map[ent]
            opts.entrance_randomization.value = EntranceRandomization.option_coupled
            opts.entrance_randomization_scope.value = ss_scope
            self._hk_entrance_scope = hk_scope

    def generate_early(self) -> None:
        self._apply_unified_shuffles()
        self.hk.options = self.options
        self.hk.random = self.random
        if self.has_hk:
            self.hk.generate_early()
        if self.has_silksong:
            SilksongWorld.generate_early(self)

    def create_regions(self) -> None:
        if self.has_hk:
            self.hk.create_regions()
        if self.has_silksong:
            SilksongWorld.create_regions(self)
        self._extra_hk_entries, self._extra_ss_entries = create_extra_regions_and_locations(self)

    def create_items(self) -> None:
        if self.has_hk:
            self.hk.create_items()
        if self.has_silksong:
            SilksongWorld.create_items(self)
        create_extra_filler_items(self, len(self._extra_hk_entries), len(self._extra_ss_entries))
        self._cross_game_items = merge_cross_game_items(self)

    def create_item(self, name: str, *args, **kwargs) -> Item:
        if name in CROSS_GAME_ITEMS_BY_NAME:
            return create_cross_game_item(CROSS_GAME_ITEMS_BY_NAME[name], self.player)
        if name.startswith(HK_PREFIX):
            return self.hk.create_item(strip_hk_prefix(name))
        if name.startswith(SS_PREFIX):
            return SilksongWorld.create_item(self, strip_ss_prefix(name), *args, **kwargs)
        if name in SilksongWorld.item_name_to_id:
            return SilksongWorld.create_item(self, name, *args, **kwargs)
        if name in HKWorld.item_name_to_id:
            return self.hk.create_item(name)
        raise KeyError(f"Unknown HKSS item: {name!r}")

    def create_filler(self) -> Item:
        if self.has_silksong:
            return SilksongWorld.create_filler(self)
        return self.hk.create_item(self.hk.get_filler_item_name())

    def get_filler_item_name(self) -> str:
        if self.has_hk and not self.has_silksong:
            return hk_name(self.hk.get_filler_item_name())
        if self.has_silksong and not self.has_hk:
            return ss_name(SilksongWorld.get_filler_item_name(self))
        if self.random.random() < 0.5:
            return hk_name(self.hk.get_filler_item_name())
        return ss_name(SilksongWorld.get_filler_item_name(self))

    def set_completion_rule(self, rule) -> None:
        self._silksong_completion_rule = rule
        self._refresh_completion_rule()

    def _refresh_completion_rule(self) -> None:
        if self.has_hk and not self.has_silksong:
            self.multiworld.completion_condition[self.player] = self._hk_completion_condition
            return
        if self.has_silksong and not self.has_hk:
            CachedRuleBuilderWorld.set_completion_rule(self, self._silksong_completion_rule)
            return
        if self._silksong_completion_rule is not None:
            CachedRuleBuilderWorld.set_completion_rule(self, self._silksong_completion_rule)
            ss_cond = self.multiworld.completion_condition[self.player]
            hk_cond = self._hk_completion_condition
            if hk_cond is None:
                return
            if self.options.combo_goal.value == ComboGoal.option_either:
                self.multiworld.completion_condition[self.player] = (
                    lambda state, h=hk_cond, s=ss_cond: h(state) or s(state)
                )
            else:
                self.multiworld.completion_condition[self.player] = (
                    lambda state, h=hk_cond, s=ss_cond: h(state) and s(state)
                )

    def set_rules(self) -> None:
        if self.has_hk:
            self.hk.set_rules()
            self._hk_completion_condition = self.hk.get_completion_condition()
        if self.has_silksong:
            SilksongWorld.set_rules(self)
        set_extra_rules(self, self._extra_hk_entries, self._extra_ss_entries)
        self._refresh_completion_rule()
        self._seals = choose_seals(self, self.options.cross_game_seals.value)
        apply_hk_seals(self, self._seals)

    def _enable_silk_supply(self, supply) -> None:
        self._silk_supply = supply

    def _invalidate_rule_cache(self, state: CollectionState, names: tuple[str, ...], *, only_false: bool) -> None:
        results = state.rule_builder_cache[self.player]
        if not results:
            return
        for name in dict.fromkeys(names):
            if not name:
                continue
            dependencies = self.rule_item_dependencies.get(name)
            if not dependencies:
                continue
            for rule_id in results.keys() & dependencies:
                if not only_false or results.get(rule_id) is False:
                    del results[rule_id]

    def _silksong_collect_name(self, state: CollectionState, raw_name: str, remove: bool = False) -> str | None:
        if raw_name == PROGRESSIVE_SWIFT_STEP_ITEM and "Dash" in self.get_directional_abilities():
            from .silksong.directional_abilities import progressive_steps

            steps = progressive_steps(self._swift_step_first_right)
            if remove:
                return next((n for n in reversed(steps) if state.has(n, self.player)), None)
            return next((n for n in steps if not state.has(n, self.player)), None)
        return self.item_mapping.get(raw_name, raw_name)

    def collect(self, state: CollectionState, item: Item) -> bool:
        spec = CROSS_GAME_ITEMS_BY_NAME.get(item.name)
        if spec is not None:
            if not item.advancement:
                return False
            state.add_item(item.name, self.player)
            hk_prefixed = hk_name(spec.hk_item)
            state.add_item(hk_prefixed, self.player)
            if spec.hk_item != "Swim":
                state.add_item(spec.hk_item, self.player)
            if self.has_hk:
                self.hk.collect_effects(state, HKItem(hk_prefixed, True, None, "Item", self.player))
            ss_prefixed = ss_name(spec.ss_item)
            collected_name = self._silksong_collect_name(state, spec.ss_item, remove=False)
            state.add_item(ss_prefixed, self.player)
            if collected_name:
                state.add_item(collected_name, self.player)
            if hasattr(self, "_silk_supply"):
                self._silk_supply.collect(state, spec.ss_item)
                if collected_name and collected_name != spec.ss_item:
                    self._silk_supply.collect(state, collected_name)
            self._invalidate_rule_cache(
                state,
                (item.name, hk_prefixed, ss_prefixed, spec.ss_item, collected_name or "", self.item_mapping.get(spec.ss_item, "")),
                only_false=True,
            )
            return True

        if item.name.startswith(HK_PREFIX):
            changed = CachedRuleBuilderWorld.collect(self, state, item)
            if changed:
                raw_name = strip_hk_prefix(item.name)
                if raw_name != "Swim":
                    state.add_item(raw_name, self.player)
                self.hk.collect_effects(state, item)
            return changed

        if item.name.startswith(SS_PREFIX):
            if not item.advancement:
                return False
            raw_name = strip_ss_prefix(item.name)
            collected_name = self._silksong_collect_name(state, raw_name, remove=False)
            if not collected_name:
                return False
            state.add_item(item.name, self.player)
            state.add_item(collected_name, self.player)
            if hasattr(self, "_silk_supply"):
                self._silk_supply.collect(state, raw_name)
                if collected_name != raw_name:
                    self._silk_supply.collect(state, collected_name)
            self._invalidate_rule_cache(
                state,
                (item.name, raw_name, collected_name, self.item_mapping.get(raw_name, "")),
                only_false=True,
            )
            return True

        # Event items (HK events/connectors/vanilla items or Silksong events)
        changed = CachedRuleBuilderWorld.collect(self, state, item)
        if changed:
            if self.has_hk:
                self.hk.collect_effects(state, item)
            if hasattr(self, "_silk_supply"):
                self._silk_supply.collect(state, item.name)
        return changed

    def remove(self, state: CollectionState, item: Item) -> bool:
        spec = CROSS_GAME_ITEMS_BY_NAME.get(item.name)
        if spec is not None:
            if not item.advancement:
                return False
            state.remove_item(item.name, self.player)
            hk_prefixed = hk_name(spec.hk_item)
            state.remove_item(hk_prefixed, self.player)
            if spec.hk_item != "Swim":
                state.remove_item(spec.hk_item, self.player)
            if self.has_hk:
                self.hk.remove_effects(state, HKItem(hk_prefixed, True, None, "Item", self.player))
            ss_prefixed = ss_name(spec.ss_item)
            collected_name = self._silksong_collect_name(state, spec.ss_item, remove=True)
            state.remove_item(ss_prefixed, self.player)
            if collected_name:
                state.remove_item(collected_name, self.player)
            if hasattr(state, "_hkss_silksong_silk_supply"):
                state._hkss_silksong_silk_supply.pop(self.player, None)
            self._invalidate_rule_cache(
                state,
                (item.name, hk_prefixed, ss_prefixed, spec.ss_item, collected_name or "", self.item_mapping.get(spec.ss_item, "")),
                only_false=False,
            )
            return True

        if item.name.startswith(HK_PREFIX):
            changed = CachedRuleBuilderWorld.remove(self, state, item)
            if changed:
                raw_name = strip_hk_prefix(item.name)
                if raw_name != "Swim":
                    state.remove_item(raw_name, self.player)
                self.hk.remove_effects(state, item)
            return changed

        if item.name.startswith(SS_PREFIX):
            if not item.advancement:
                return False
            raw_name = strip_ss_prefix(item.name)
            collected_name = self._silksong_collect_name(state, raw_name, remove=True)
            if not collected_name:
                return False
            state.remove_item(item.name, self.player)
            state.remove_item(collected_name, self.player)
            if hasattr(state, "_hkss_silksong_silk_supply"):
                state._hkss_silksong_silk_supply.pop(self.player, None)
            self._invalidate_rule_cache(
                state,
                (item.name, raw_name, collected_name, self.item_mapping.get(raw_name, "")),
                only_false=False,
            )
            return True

        changed = CachedRuleBuilderWorld.remove(self, state, item)
        if changed:
            if self.has_hk:
                self.hk.remove_effects(state, item)
            if hasattr(state, "_hkss_silksong_silk_supply"):
                state._hkss_silksong_silk_supply.pop(self.player, None)
        return changed

    def reached_region(self, state: CollectionState, region: Region) -> None:
        dependencies = self.rule_region_dependencies.get(region.name)
        if dependencies:
            results = state.rule_builder_cache[self.player]
            for rule_id in results.keys() & dependencies:
                del results[rule_id]
        if hasattr(self, "_silk_supply"):
            self._silk_supply.reached(state, region)

    def fill_hook(self, progitempool, usefulitempool, filleritempool, fill_locations):
        if self.has_silksong:
            SilksongWorld.fill_hook(self, progitempool, usefulitempool, filleritempool, fill_locations)

    def _connect_hk_entrances(self) -> None:
        if not (self.has_hk and self._hk_entrance_scope):
            return
        menu_region = self.multiworld.get_region("Menu", self.player)
        connector_locs = {
            loc.name: loc
            for loc in menu_region.locations
            if loc.address is None and loc.name in HK_VANILLA_CONNECTORS and HK_VANILLA_CONNECTORS[loc.name]
        }
        hk_real_locs = [
            loc for loc in self.multiworld.get_locations(self.player)
            if loc.address is not None and loc.name.startswith(HK_PREFIX)
        ]
        hk_events = [
            loc for loc in menu_region.locations
            if loc.address is None
        ]
        hk_pool_items = [
            item for item in self.multiworld.itempool
            if item.player == self.player and (item.name.startswith(HK_PREFIX) or is_cross_game_item(item.name))
        ]

        seal_keys = seal_key_items(self, self._seals)

        def eval_hk_state(include_seal_keys: bool = True) -> CollectionState:
            state = CollectionState(self.multiworld)
            for item in hk_pool_items:
                state.collect(item, True)
            if include_seal_keys:
                for item in seal_keys:
                    state.collect(item, True)
            state.sweep_for_advancements(locations=hk_events)
            return state

        baseline_state = eval_hk_state()
        baseline_locs = [loc for loc in hk_real_locs if loc.can_reach(baseline_state)]
        baseline_goal = self._hk_completion_condition(baseline_state) if self._hk_completion_condition else True
        need_unsealed_town = (
            self.has_silksong
            and bool(seal_keys)
            and self.options.StartLocation.current_key != "king's_pass"
            and eval_hk_state(include_seal_keys=False).has("Town", self.player)
        )

        def apply_pairs(pairs: dict[str, str]) -> None:
            for name, loc in connector_locs.items():
                target = pairs.get(name, HK_VANILLA_CONNECTORS[name])
                loc.item = None
                loc.place_locked_item(HKItem(target, True, None, "Event", self.player))

        def is_valid(pairs: dict[str, str]) -> bool:
            apply_pairs(pairs)
            state = eval_hk_state()
            if baseline_goal and self._hk_completion_condition and not self._hk_completion_condition(state):
                return False
            if not all(loc.can_reach(state) for loc in baseline_locs):
                return False
            if need_unsealed_town and not eval_hk_state(include_seal_keys=False).has("Town", self.player):
                return False
            return True

        pairs = shuffle_hk_entrances(self._hk_entrance_scope, self.random, is_valid)
        apply_pairs(pairs)
        self._hk_entrance_pairs = pairs
        self._hk_entrance_rando_type = SCOPE_TO_RANDO_TYPE[self._hk_entrance_scope]

    def connect_entrances(self) -> None:
        self._connect_hk_entrances()
        if self.has_silksong:
            SilksongWorld.connect_entrances(self)
        # Silksong's entrance randomizer reconnects exits, so its seals go on the final entrances.
        apply_ss_seals(self, self._seals)

    def extend_hint_information(self, hint_data: dict[int, dict[int, str]]) -> None:
        if self.has_silksong:
            SilksongWorld.extend_hint_information(self, hint_data)

    def write_spoiler(self, spoiler_handle) -> None:
        if self._cross_game_items:
            player_name = self.multiworld.get_player_name(self.player)
            spoiler_handle.write(f"\nCross-Game Items ({player_name}):\n")
            for spec in self._cross_game_items:
                spoiler_handle.write(f"  {spec.describe()}\n")
        if self._seals:
            player_name = self.multiworld.get_player_name(self.player)
            spoiler_handle.write(f"\nCross-Game Seals ({player_name}):\n")
            for seal in self._seals:
                spoiler_handle.write(f"  {describe(seal)}\n")
        if self.has_hk and self._hk_entrance_pairs:
            player_name = self.multiworld.get_player_name(self.player)
            spoiler_handle.write(f"\nHollow Knight Entrances ({player_name}):\n")
            for src in sorted(self._hk_entrance_pairs):
                spoiler_handle.write(f"  {src} -> {self._hk_entrance_pairs[src]}\n")
        if self.has_silksong:
            SilksongWorld.write_spoiler(self, spoiler_handle)

    def get_pre_fill_items(self) -> list[Item]:
        if self.has_silksong:
            return SilksongWorld.get_pre_fill_items(self)
        return []

    @classmethod
    def stage_generate_basic(cls, multiworld: MultiWorld) -> None:
        SilksongWorld.stage_generate_basic(multiworld)

    @classmethod
    def stage_pre_fill(cls, multiworld: MultiWorld) -> None:
        HKWorld.stage_pre_fill(multiworld)
        SilksongWorld.stage_pre_fill(multiworld)

    @classmethod
    def stage_post_fill(cls, multiworld: MultiWorld) -> None:
        SilksongWorld.stage_post_fill(multiworld)

    @classmethod
    def stage_finalize_multiworld(cls, multiworld: MultiWorld) -> None:
        SilksongWorld.stage_finalize_multiworld(multiworld)

    @classmethod
    def stage_write_spoiler(cls, multiworld: MultiWorld, spoiler_handle) -> None:
        HKWorld.stage_write_spoiler(multiworld, spoiler_handle)

    def fill_slot_data(self) -> dict[str, Any]:
        hk_data = self.hk.fill_slot_data() if self.has_hk else None
        if hk_data is not None:
            hk_data["options"]["RandomizeLevers"] = int(bool(self.options.RandomizeLevers.value))
            hk_data["options"]["RandomizeBreakableWalls"] = int(bool(self.options.RandomizeBreakableWalls.value))
            hk_data["options"]["RandomizeGrass"] = int(bool(self.options.RandomizeGrass.value))
            hk_data["options"]["StartWithFullMaps"] = int(bool(self.options.start_with_full_maps.value))
            if self._hk_entrance_pairs:
                hk_data["entrance_pairs"] = dict(self._hk_entrance_pairs)
                hk_data["options"]["EntranceRandoType"] = self._hk_entrance_rando_type
                hk_data["options"]["ShuffleEntrancesMode"] = 0
        ss_data = SilksongWorld.fill_slot_data(self) if self.has_silksong else None
        if ss_data is not None:
            ss_data["lever_randomization"] = bool(self.options.lever_randomization.value)
            ss_data["breakable_wall_randomization"] = bool(self.options.breakable_wall_randomization.value)
            ss_data["grass_randomization"] = bool(self.options.grass_randomization.value)
            if self._extra_ss_entries:
                ss_data["requirements"].update(export_ss_extra_requirements(self._extra_ss_entries))
        return {
            "world_version": WORLD_VERSION,
            "games": self.options.games.current_key,
            "combo_goal": self.options.combo_goal.current_key,
            "death_link": bool(self.options.death_link.value),
            "double_steel_soul": bool(self.options.double_steel_soul.value),
            "start_with_full_maps": bool(self.options.start_with_full_maps.value),
            "cross_game_seals": [
                seal.slot_data(self.item_name_to_id[seal.key]) for seal in self._seals
            ],
            "cross_game_items": [
                spec.slot_data() for spec in self._cross_game_items
            ],
            "hollow_knight": hk_data,
            "silksong": ss_data,
        }

    def modify_multidata(self, multidata: dict) -> None:
        if self.has_silksong:
            # The standalone world encodes its map logic at the root of its slot data.
            # Pass the nested object by reference so the combo envelope stays intact.
            silksong_data = multidata["slot_data"][self.player]["silksong"]
            SilksongWorld.modify_multidata(self, {"slot_data": {self.player: silksong_data}})

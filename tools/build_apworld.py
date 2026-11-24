"""Vendor upstream Hollow Knight and Silksong worlds into apworld/hkss and build hkss.apworld."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_HK_SRC = REPO_ROOT / "local" / "reference" / "archipelago" / "worlds" / "hk"
DEFAULT_SS_SRC = REPO_ROOT / "local" / "reference" / "silksong-decrypted" / "APWorld" / "silksong"
APWORLD_DIR = REPO_ROOT / "apworld" / "hkss"
DEFAULT_OUTPUT = REPO_ROOT / "dist" / "hkss.apworld"


def git_rev(repo_dir: Path) -> str:
    try:
        out = subprocess.check_output(
            ["git", "-C", str(repo_dir), "rev-parse", "HEAD"],
            stderr=subprocess.DEVNULL,
            text=True,
        )
        return out.strip()
    except Exception:
        return "unknown"


def must_replace(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        raise RuntimeError(f"Expected snippet not found in {label}: {old!r}")
    return text.replace(old, new)


def copy_tree_filtered(src: Path, dst: Path, skip_names: set[str]) -> None:
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True, exist_ok=True)
    for item in src.iterdir():
        if item.name in skip_names or item.name == "__pycache__":
            continue
        target = dst / item.name
        if item.is_dir():
            shutil.copytree(
                item,
                target,
                ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo"),
            )
        else:
            shutil.copy2(item, target)


def vendor_hk(hk_src: Path, hk_dst: Path) -> None:
    copy_tree_filtered(
        hk_src,
        hk_dst,
        skip_names={"test", "docs", "templates", "archipelago.json", ".gitignore"},
    )

    # 1. Rewrite LogicMixin method names in Rules.py, GeneratedRules.py, GodhomeData.py
    for rel in ("Rules.py", "GeneratedRules.py", "GodhomeData.py"):
        p = hk_dst / rel
        if not p.exists():
            continue
        text = p.read_text(encoding="utf-8")
        text = text.replace("._hk_notches(", "._hkss_hk_notches(")
        text = text.replace("._hk_option(", "._hkss_hk_option(")
        text = text.replace("._hk_start(", "._hkss_hk_start(")
        if rel == "Rules.py":
            text = must_replace(
                text,
                "from ..generic.Rules import set_rule, add_rule\nfrom ..AutoWorld import World",
                "from worlds.generic.Rules import set_rule, add_rule\n"
                "from worlds.AutoWorld import World\n"
                "from ..ids import hk_name",
                "hk/Rules.py imports",
            )
            text = must_replace(
                text,
                "        try:\n"
                "            locations = [hk_world.multiworld.get_location(location, player)]\n"
                "        except KeyError:\n"
                "            return",
                "        try:\n"
                "            locations = [hk_world.multiworld.get_location(hk_name(location), player)]\n"
                "        except KeyError:\n"
                "            try:\n"
                "                locations = [hk_world.multiworld.get_location(location, player)]\n"
                "            except KeyError:\n"
                "                return",
                "hk/Rules.py hk_set_rule lookup",
            )
            text = must_replace(
                text,
                "    for location in hk_world.multiworld.get_locations(player):\n"
                "        if location.costs:",
                "    for location in hk_world.multiworld.get_locations(player):\n"
                "        if getattr(location, 'costs', None):",
                "hk/Rules.py location.costs",
            )
        p.write_text(text, encoding="utf-8")

    # 2. Rewrite hk/__init__.py
    init_p = hk_dst / "__init__.py"
    text = init_p.read_text(encoding="utf-8")
    text = must_replace(
        text,
        "from worlds.AutoWorld import World, LogicMixin, WebWorld",
        "from worlds.AutoWorld import LogicMixin\n"
        "from ..ids import GAME_NAME as HKSS_GAME_NAME, hk_id, hk_name, strip_hk_prefix",
        "hk/__init__.py imports",
    )
    # Remove HKWeb class (not needed when vendored)
    text = re.sub(
        r"class HKWeb\(WebWorld\):.*?(?=\nclass HKWorld)",
        "",
        text,
        flags=re.S,
    )
    text = must_replace(
        text,
        "class HKWorld(World):",
        "class HKWorld:",
        "hk/__init__.py HKWorld base",
    )
    text = must_replace(
        text,
        '    game: str = "Hollow Knight"\n'
        "    options_dataclass = HKOptions\n"
        "    options: HKOptions\n"
        "    settings: typing.ClassVar[HollowKnightSettings]\n\n"
        "    web = HKWeb()",
        "    game: str = HKSS_GAME_NAME\n"
        "    options_dataclass = HKOptions\n"
        "    options: HKOptions\n"
        "    settings: typing.ClassVar[HollowKnightSettings]",
        "hk/__init__.py HKWorld header",
    )
    text = must_replace(
        text,
        "    def __init__(self, multiworld, player):\n"
        "        super(HKWorld, self).__init__(multiworld, player)",
        "    def __init__(self, multiworld, player, parent_world=None):\n"
        "        self.multiworld = multiworld\n"
        "        self.player = player\n"
        "        self.parent_world = parent_world",
        "hk/__init__.py __init__",
    )
    # Reuse existing Menu region if already created
    text = must_replace(
        text,
        "        menu_region: Region = create_region(self.multiworld, self.player, 'Menu')\n"
        "        self.multiworld.regions.append(menu_region)",
        "        try:\n"
        "            menu_region = self.multiworld.get_region('Menu', self.player)\n"
        "        except KeyError:\n"
        "            menu_region = create_region(self.multiworld, self.player, 'Menu')\n"
        "            self.multiworld.regions.append(menu_region)",
        "hk/__init__.py create_regions Menu reuse",
    )
    # Vessel_Fragment-Basin check uses basename (unprefixed)
    text = must_replace(
        text,
        '                if location.name == "Vessel_Fragment-Basin":',
        '                if location.basename == "Vessel_Fragment-Basin":',
        "hk/__init__.py Vessel_Fragment-Basin",
    )
    # Return completion predicate instead of overwriting multiworld.completion_condition directly
    text = must_replace(
        text,
        "    def set_rules(self):\n"
        "        multiworld = self.multiworld\n"
        "        player = self.player\n"
        "        goal = self.options.Goal\n"
        "        if goal == Goal.option_hollowknight:\n"
        "            multiworld.completion_condition[player] = lambda state: _hk_can_beat_thk(state, player)\n"
        "        elif goal == Goal.option_siblings:\n"
        "            multiworld.completion_condition[player] = lambda state: _hk_siblings_ending(state, player)\n"
        "        elif goal == Goal.option_radiance:\n"
        "            multiworld.completion_condition[player] = lambda state: _hk_can_beat_radiance(state, player)\n"
        "        elif goal == Goal.option_godhome:\n"
        "            multiworld.completion_condition[player] = lambda state: state.count(\"Defeated_Pantheon_5\", player)\n"
        "        elif goal == Goal.option_godhome_flower:\n"
        "            multiworld.completion_condition[player] = lambda state: state.count(\"Godhome_Flower_Quest\", player)\n"
        "        elif goal == Goal.option_grub_hunt:\n"
        "            multiworld.completion_condition[player] = lambda state: self.can_grub_goal(state)\n"
        "        else:\n"
        "            # Any goal\n"
        "            multiworld.completion_condition[player] = lambda state: _hk_siblings_ending(state, player) and \\\n"
        "                _hk_can_beat_radiance(state, player) and state.count(\"Godhome_Flower_Quest\", player) and \\\n"
        "                self.can_grub_goal(state)\n"
        "\n"
        "        set_rules(self)",
        "    def get_completion_condition(self):\n"
        "        player = self.player\n"
        "        goal = self.options.Goal\n"
        "        if goal == Goal.option_hollowknight:\n"
        "            return lambda state: _hk_can_beat_thk(state, player)\n"
        "        if goal == Goal.option_siblings:\n"
        "            return lambda state: _hk_siblings_ending(state, player)\n"
        "        if goal == Goal.option_radiance:\n"
        "            return lambda state: _hk_can_beat_radiance(state, player)\n"
        "        if goal == Goal.option_godhome:\n"
        "            return lambda state: bool(state.count(\"Defeated_Pantheon_5\", player))\n"
        "        if goal == Goal.option_godhome_flower:\n"
        "            return lambda state: bool(state.count(\"Godhome_Flower_Quest\", player))\n"
        "        if goal == Goal.option_grub_hunt:\n"
        "            return lambda state: self.can_grub_goal(state)\n"
        "        return lambda state: (_hk_siblings_ending(state, player) and\n"
        "            _hk_can_beat_radiance(state, player) and bool(state.count(\"Godhome_Flower_Quest\", player)) and\n"
        "            self.can_grub_goal(state))\n"
        "\n"
        "    def set_rules(self):\n"
        "        set_rules(self)",
        "hk/__init__.py set_rules",
    )
    # Grub goal checks "Grub" (collected by both vanilla Grub events and randomized HK Grub items)
    # stage_pre_fill iterates over HKSS players that have HK enabled
    text = must_replace(
        text,
        '        worlds = [world for world in multiworld.get_game_worlds(cls.game) if world.options.Goal in ["any", "grub_hunt"]]\n'
        '        if worlds:\n'
        '            grubs = [item for item in multiworld.get_items() if item.name == "Grub"]',
        '        worlds = [world.hk for world in multiworld.get_game_worlds(cls.game) if getattr(world, "has_hk", False) and world.options.Goal in ["any", "grub_hunt"]]\n'
        '        if worlds:\n'
        '            grubs = [item for item in multiworld.get_items() if item.name in ("Grub", hk_name("Grub"))]',
        "hk/__init__.py stage_pre_fill worlds",
    )
    text = must_replace(
        text,
        "            for player, count in grub_count_per_player.items():\n"
        "                multiworld.worlds[player].grub_count = count\n"
        "\n"
        "            for player, grub_player_count in per_player_grubs_per_player.items():\n"
        "                if player in all_grub_players:\n"
        "                    multiworld.worlds[player].grub_player_count = grub_player_count",
        "            for player, count in grub_count_per_player.items():\n"
        "                if getattr(multiworld.worlds[player], 'has_hk', False):\n"
        "                    multiworld.worlds[player].hk.grub_count = count\n"
        "\n"
        "            for player, grub_player_count in per_player_grubs_per_player.items():\n"
        "                if player in all_grub_players and getattr(multiworld.worlds[player], 'has_hk', False):\n"
        "                    multiworld.worlds[player].hk.grub_player_count = grub_player_count",
        "hk/__init__.py stage_pre_fill grub counts",
    )
    # fill_slot_data handles shared death_link and non-HK locations without .costs
    text = must_replace(
        text,
        "        for option_name in hollow_knight_options:\n"
        "            option = getattr(self.options, option_name)",
        "        for option_name in hollow_knight_options:\n"
        "            option = self.options.death_link if option_name == 'DeathLink' else getattr(self.options, option_name)",
        "hk/__init__.py fill_slot_data options",
    )
    text = must_replace(
        text,
        "                if location.costs:\n"
        "                    location_costs[location.name] = location.costs",
        "                if getattr(location, 'costs', None):\n"
        "                    location_costs[strip_hk_prefix(location.name)] = location.costs",
        "hk/__init__.py fill_slot_data costs",
    )
    text = must_replace(
        text,
        "    def create_vanilla_location(self, location: str, item: Item):\n"
        "        costs = self.vanilla_shop_costs.get((location, item.name))",
        "    def create_vanilla_location(self, location: str, item: Item):\n"
        "        costs = self.vanilla_shop_costs.get((location, strip_hk_prefix(item.name)))",
        "hk/__init__.py create_vanilla_location costs",
    )
    text = must_replace(
        text,
        '        slot_data["is_race"] = self.settings.disable_spoilers or self.multiworld.is_race',
        '        slot_data["is_race"] = bool(getattr(self.parent_world.settings, "disable_spoilers", False)) or self.multiworld.is_race',
        "hk/__init__.py fill_slot_data is_race",
    )
    # create_item / create_location / HKItem / HKLocation prefixing & ID mapping
    text = must_replace(
        text,
        "        if multi is not None:\n"
        "            i = len(multi) + 1\n"
        "            name = f\"{name}_{i}\"",
        "        if multi is not None:\n"
        "            i = len(multi) + 1\n"
        "            name = f\"{name}_{i}\"",
        "hk/__init__.py create_location multi",
    )
    text = must_replace(
        text,
        "    def collect(self, state, item: HKItem) -> bool:\n"
        "        change = super(HKWorld, self).collect(state, item)\n"
        "        if change:\n"
        "            for effect_name, effect_value in item_effects.get(item.name, {}).items():\n"
        "                state.prog_items[item.player][effect_name] += effect_value\n"
        "            if item.name in {\"Left_Mothwing_Cloak\", \"Right_Mothwing_Cloak\"}:\n"
        "                if state.prog_items[item.player].get('RIGHTDASH', 0) and \\\n"
        "                        state.prog_items[item.player].get('LEFTDASH', 0):\n"
        "                    (state.prog_items[item.player][\"RIGHTDASH\"], state.prog_items[item.player][\"LEFTDASH\"]) = \\\n"
        "                        ([max(state.prog_items[item.player][\"RIGHTDASH\"], state.prog_items[item.player][\"LEFTDASH\"])] * 2)\n"
        "        return change\n"
        "\n"
        "    def remove(self, state, item: HKItem) -> bool:\n"
        "        change = super(HKWorld, self).remove(state, item)\n"
        "\n"
        "        if change:\n"
        "            for effect_name, effect_value in item_effects.get(item.name, {}).items():\n"
        "                if state.prog_items[item.player][effect_name] == effect_value:\n"
        "                    del state.prog_items[item.player][effect_name]\n"
        "                else:\n"
        "                    state.prog_items[item.player][effect_name] -= effect_value\n"
        "\n"
        "        return change",
        "    def collect_effects(self, state, item: Item) -> None:\n"
        "        raw_name = strip_hk_prefix(item.name)\n"
        "        for effect_name, effect_value in item_effects.get(raw_name, {}).items():\n"
        "            state.prog_items[item.player][effect_name] += effect_value\n"
        "        if raw_name in {\"Left_Mothwing_Cloak\", \"Right_Mothwing_Cloak\"}:\n"
        "            if state.prog_items[item.player].get('RIGHTDASH', 0) and \\\n"
        "                    state.prog_items[item.player].get('LEFTDASH', 0):\n"
        "                (state.prog_items[item.player][\"RIGHTDASH\"], state.prog_items[item.player][\"LEFTDASH\"]) = \\\n"
        "                    ([max(state.prog_items[item.player][\"RIGHTDASH\"], state.prog_items[item.player][\"LEFTDASH\"])] * 2)\n"
        "\n"
        "    def remove_effects(self, state, item: Item) -> None:\n"
        "        raw_name = strip_hk_prefix(item.name)\n"
        "        for effect_name, effect_value in item_effects.get(raw_name, {}).items():\n"
        "            if state.prog_items[item.player][effect_name] == effect_value:\n"
        "                del state.prog_items[item.player][effect_name]\n"
        "            else:\n"
        "                state.prog_items[item.player][effect_name] -= effect_value",
        "hk/__init__.py collect/remove",
    )
    text = must_replace(
        text,
        "        hk_players = multiworld.get_game_players(cls.game)",
        "        hk_players = [p for p in multiworld.get_game_players(cls.game) if getattr(multiworld.worlds[p], 'has_hk', False)]\n"
        "        if not hk_players:\n"
        "            return",
        "hk/__init__.py stage_write_spoiler players",
    )
    text = text.replace(
        "hk_world: HKWorld = multiworld.worlds[player]",
        "hk_world: HKWorld = multiworld.worlds[player].hk",
    )
    text = must_replace(
        text,
        "                        if loc.costs",
        "                        if getattr(loc, 'costs', None)",
        "hk/__init__.py stage_write_spoiler costs",
    )
    # HKLocation prefixes real location names and maps code via hk_id
    text = must_replace(
        text,
        'class HKLocation(Location):\n'
        '    game: str = "Hollow Knight"',
        'class HKLocation(Location):\n'
        '    game: str = HKSS_GAME_NAME',
        "hk/__init__.py HKLocation game",
    )
    text = must_replace(
        text,
        "        self.basename = basename or name\n"
        "        super(HKLocation, self).__init__(player, name, code if code else None, parent)",
        "        self.basename = basename or name\n"
        "        mapped_name = hk_name(name) if code else name\n"
        "        mapped_code = hk_id(code) if code else None\n"
        "        super(HKLocation, self).__init__(player, mapped_name, mapped_code, parent)",
        "hk/__init__.py HKLocation __init__",
    )
    # HKItem prefixes real item names and maps code via hk_id
    text = must_replace(
        text,
        'class HKItem(Item):\n'
        '    game = "Hollow Knight"',
        'class HKItem(Item):\n'
        '    game = HKSS_GAME_NAME',
        "hk/__init__.py HKItem game",
    )
    text = must_replace(
        text,
        "        super(HKItem, self).__init__(name, classification, code if code else None, player)",
        "        mapped_name = hk_name(name) if code else name\n"
        "        mapped_code = hk_id(code) if code else None\n"
        "        super(HKItem, self).__init__(mapped_name, classification, mapped_code, player)",
        "hk/__init__.py HKItem __init__",
    )
    # Rename HKLogicMixin -> HkssHKLogicMixin
    text = must_replace(
        text,
        "class HKLogicMixin(LogicMixin):\n"
        "    multiworld: MultiWorld\n"
        "\n"
        "    def _hk_notches(self, player: int, *notches: int) -> int:\n"
        "        return sum(self.multiworld.worlds[player].charm_costs[notch] for notch in notches)\n"
        "\n"
        "    def _hk_option(self, player: int, option_name: str) -> int:\n"
        "        return getattr(self.multiworld.worlds[player].options, option_name).value\n"
        "\n"
        "    def _hk_start(self, player, start_location: str) -> bool:\n"
        "        return self.multiworld.worlds[player].options.StartLocation == start_location",
        "class HkssHKLogicMixin(LogicMixin):\n"
        "    multiworld: MultiWorld\n"
        "\n"
        "    def _hkss_hk_notches(self, player: int, *notches: int) -> int:\n"
        "        return sum(self.multiworld.worlds[player].hk.charm_costs[notch] for notch in notches)\n"
        "\n"
        "    def _hkss_hk_option(self, player: int, option_name: str) -> int:\n"
        "        opts = self.multiworld.worlds[player].options\n"
        "        opt = opts.death_link if option_name == 'DeathLink' else getattr(opts, option_name)\n"
        "        return opt.value\n"
        "\n"
        "    def _hkss_hk_start(self, player, start_location: str) -> bool:\n"
        "        return self.multiworld.worlds[player].options.StartLocation == start_location",
        "hk/__init__.py HKLogicMixin",
    )
    init_p.write_text(text, encoding="utf-8")


def vendor_silksong(ss_src: Path, ss_dst: Path) -> None:
    copy_tree_filtered(
        ss_src,
        ss_dst,
        skip_names={"test", "docs", "archipelago.json", ".apignore"},
    )

    # Helper module inside vendored silksong for clean name/ID translation
    (ss_dst / "_hkss_bridge.py").write_text(
        '"""Bridge helpers between vendored Silksong logic and HKSS prefixed names/IDs."""\n\n'
        "from ..ids import GAME_NAME as HKSS_GAME_NAME, ss_id, ss_name, strip_ss_prefix\n\n"
        "def ss_get_location(multiworld, name: str, player: int):\n"
        "    try:\n"
        "        return multiworld.get_location(ss_name(name), player)\n"
        "    except KeyError:\n"
        "        return multiworld.get_location(name, player)\n\n"
        "def ss_worlds(multiworld, game: str = HKSS_GAME_NAME):\n"
        "    return [w for w in multiworld.get_game_worlds(game) if getattr(w, 'has_silksong', False)]\n",
        encoding="utf-8",
    )

    # 1. Replace game name string in the 6 files that define it
    for rel in (
        "__init__.py",
        "items.py",
        "locations.py",
        "requirement_rules.py",
        "silk_supply.py",
        "tracker/__init__.py",
    ):
        p = ss_dst / rel
        if p.exists():
            t = p.read_text(encoding="utf-8")
            t = t.replace('"Hollow Knight: Silksong"', '"Hollow Knight x Silksong"')
            t = t.replace("'Hollow Knight: Silksong'", "'Hollow Knight x Silksong'")
            p.write_text(t, encoding="utf-8")

    # 2. silk_supply.py: rename LogicMixin and state attribute so it never collides with standalone silksong.apworld
    silk_p = ss_dst / "silk_supply.py"
    t = silk_p.read_text(encoding="utf-8")
    t = t.replace("class SilkSupplyState(LogicMixin):", "class HkssSilkSupplyState(LogicMixin):")
    t = t.replace("_silksong_silk_supply", "_hkss_silksong_silk_supply")
    silk_p.write_text(t, encoding="utf-8")

    # 3. items.py: SilksongItem prefixes real item names and maps code via ss_id
    items_p = ss_dst / "items.py"
    t = items_p.read_text(encoding="utf-8")
    t = must_replace(
        t,
        "class SilksongItem(Item):\n"
        '    game = "Hollow Knight x Silksong"\n'
        "\n"
        "    def __init__(\n"
        "        self,\n"
        "        name: str,\n"
        "        classification: ItemClassification,\n"
        "        code: int,\n"
        "        player: int,\n"
        "        placement_category: str | None = None,\n"
        "    ) -> None:\n"
        "        super().__init__(name, classification, code, player)\n"
        "        self.silksong_placement_category = placement_category",
        "from ._hkss_bridge import ss_id, ss_name\n\n\n"
        "class SilksongItem(Item):\n"
        '    game = "Hollow Knight x Silksong"\n'
        "\n"
        "    def __init__(\n"
        "        self,\n"
        "        name: str,\n"
        "        classification: ItemClassification,\n"
        "        code: int | None,\n"
        "        player: int,\n"
        "        placement_category: str | None = None,\n"
        "    ) -> None:\n"
        "        self.raw_name = name\n"
        "        mapped_name = ss_name(name) if code is not None else name\n"
        "        mapped_code = ss_id(code) if code is not None else None\n"
        "        super().__init__(mapped_name, classification, mapped_code, player)\n"
        "        self.silksong_placement_category = placement_category",
        "silksong/items.py SilksongItem",
    )
    items_p.write_text(t, encoding="utf-8")

    # 4. locations.py: SilksongLocation prefixes real location names and maps address via ss_id
    locs_p = ss_dst / "locations.py"
    t = locs_p.read_text(encoding="utf-8")
    t = must_replace(
        t,
        "class SilksongLocation(Location):\n"
        '    game = "Hollow Knight x Silksong"',
        "from ._hkss_bridge import ss_id, ss_name\n\n\n"
        "class SilksongLocation(Location):\n"
        '    game = "Hollow Knight x Silksong"\n'
        "\n"
        "    def __init__(self, player: int, name: str, address: int | None = None, parent=None):\n"
        "        self.raw_name = name\n"
        "        mapped_name = ss_name(name) if address is not None else name\n"
        "        mapped_address = ss_id(address) if address is not None else None\n"
        "        super().__init__(player, mapped_name, mapped_address, parent)",
        "silksong/locations.py SilksongLocation",
    )
    locs_p.write_text(t, encoding="utf-8")

    # 5. requirement_rules.py: prefix CanReachLocation targets
    rr_p = ss_dst / "requirement_rules.py"
    t = rr_p.read_text(encoding="utf-8")
    t = must_replace(
        t,
        "return CanReachLocation(location_name)",
        "return CanReachLocation(_ss_name(location_name))",
        "silksong/requirement_rules.py CanReachLocation",
    )
    imp = "from ._hkss_bridge import ss_name as _ss_name\n"
    first_nl = t.index("\n") + 1
    t = t[:first_nl] + imp + t[first_nl:]
    rr_p.write_text(t, encoding="utf-8")

    # 6. enemy_souls.py: use ss_get_location
    es_p = ss_dst / "enemy_souls.py"
    t = es_p.read_text(encoding="utf-8")
    t = must_replace(
        t,
        "        location = world.multiworld.get_location(location_name(name), world.player)",
        "        from ._hkss_bridge import ss_get_location\n"
        "        location = ss_get_location(world.multiworld, location_name(name), world.player)",
        "silksong/enemy_souls.py get_location",
    )
    es_p.write_text(t, encoding="utf-8")

    # 7. rules.py: use ss_get_location and strip_ss_prefix on location/item names
    rules_p = ss_dst / "rules.py"
    t = rules_p.read_text(encoding="utf-8")
    imp = "from ._hkss_bridge import ss_get_location, strip_ss_prefix\n"
    if t.startswith("from __future__"):
        first_nl = t.index("\n") + 1
        t = t[:first_nl] + imp + t[first_nl:]
    else:
        t = imp + t
    t = must_replace(
        t,
        '    get_location = getattr(world.multiworld, "get_location", None)\n'
        '    if not callable(get_location):\n'
        '        return ()',
        '    if not callable(getattr(world.multiworld, "get_location", None)):\n'
        '        return ()\n'
        '    get_location = lambda name, player: ss_get_location(world.multiworld, name, player)',
        "silksong/rules.py get_location alias",
    )
    t = t.replace(
        "world.multiworld.get_location(",
        "ss_get_location(world.multiworld, ",
    )
    # Strip SS prefix when inspecting location.name / item.name in rules.py
    t = re.sub(r"\b(location|loc)\.item\.name\b(?!\s*=[^=])", r"strip_ss_prefix(\1.item.name)", t)
    t = re.sub(r"(?<!\.)\b(item|location|loc)\.name\b(?!\s*=[^=])", r"strip_ss_prefix(\1.name)", t)
    rules_p.write_text(t, encoding="utf-8")

    # 8. Strip SS prefix in helper modules that compare item.name / location.name against raw sets
    for rel in (
        "category_fill.py",
        "craftmetal.py",
        "boss_souls.py",
        "entrance_randomization.py",
        "generation_diagnostics.py",
        "native_regions.py",
        "vog_hints.py",
    ):
        p = ss_dst / rel
        if not p.exists():
            continue
        t = p.read_text(encoding="utf-8-sig")
        # Insert import after __future__ import if present
        imp = "from ._hkss_bridge import ss_worlds, strip_ss_prefix\n"
        if t.startswith("from __future__"):
            first_nl = t.index("\n") + 1
            t = t[:first_nl] + imp + t[first_nl:]
        else:
            t = imp + t
        # Replace multiworld.get_game_worlds(game) with ss_worlds(multiworld, game)
        t = re.sub(r"multiworld\.get_game_worlds\(([^)]+)\)", r"ss_worlds(multiworld, \1)", t)
        # Replace item.name / location.name / loc.name reads (avoiding assignments like .name =)
        t = re.sub(r"\b(location|loc)\.item\.name\b(?!\s*=[^=])", r"strip_ss_prefix(\1.item.name)", t)
        t = re.sub(r"(?<!\.)\b(item|location|loc)\.name\b(?!\s*=[^=])", r"strip_ss_prefix(\1.name)", t)
        if rel == "category_fill.py":
            t = t.replace(
                "strip_ss_prefix(location.name) in world.location_name_to_id",
                "location.name in world.location_name_to_id",
            )
        p.write_text(t, encoding="utf-8")

    # 8b. tracker/wishes.py: look up prefixed location names in world.location_name_to_id
    tw_p = ss_dst / "tracker" / "wishes.py"
    if tw_p.exists():
        t = tw_p.read_text(encoding="utf-8")
        imp = "from .._hkss_bridge import ss_name as _ss_name\n"
        if t.startswith("from __future__"):
            first_nl = t.index("\n") + 1
            t = t[:first_nl] + imp + t[first_nl:]
        else:
            t = imp + t
        t = must_replace(
            t,
            "mapping[f'{host}/Turn-in: {name}'] = world.location_name_to_id[name]",
            "mapping[f'{host}/Turn-in: {name}'] = world.location_name_to_id[_ss_name(name)]",
            "silksong/tracker/wishes.py location_name_to_id",
        )
        tw_p.write_text(t, encoding="utf-8")

    # 9. silksong/__init__.py: remove World subclass registration, bridge prefixing, hook stage methods
    init_p = ss_dst / "__init__.py"
    t = init_p.read_text(encoding="utf-8")
    t = t.replace("_silksong_silk_supply", "_hkss_silksong_silk_supply")
    t = must_replace(
        t,
        "from rule_builder.cached_world import CachedRuleBuilderWorld",
        "from rule_builder.cached_world import CachedRuleBuilderWorld\n"
        "from ._hkss_bridge import ss_get_location, ss_id, ss_name, ss_worlds, strip_ss_prefix",
        "silksong/__init__.py imports",
    )
    # Prevent AutoWorldRegister from registering vendored SilksongWorld as a standalone World
    t = must_replace(
        t,
        "class SilksongWorld(CachedRuleBuilderWorld):",
        "class SilksongWorld:",
        "silksong/__init__.py SilksongWorld base",
    )
    # Guard get_game_worlds calls so hollow_knight_only players are skipped
    t = re.sub(r"multiworld\.get_game_worlds\(([^)]+)\)", r"ss_worlds(multiworld, \1)", t)
    t = t.replace(
        "multiworld.worlds[player].game == cls.game",
        "multiworld.worlds[player].game == cls.game and getattr(multiworld.worlds[player], 'has_silksong', False)",
    )
    # get_location calls in __init__.py
    t = t.replace(
        "self.multiworld.get_location(",
        "ss_get_location(self.multiworld, ",
    )
    t = t.replace(
        "multiworld.get_location(",
        "ss_get_location(multiworld, ",
    )
    # Reuse existing Menu region if already created (for both mode) and gate Pharloom entry in both mode
    t = must_replace(
        t,
        '        menu = Region("Menu", self.player, self.multiworld)\n'
        '        pharloom = Region("Pharloom", self.player, self.multiworld)\n'
        '        logic_unknown = Region(\n'
        '            LOGIC_UNKNOWN_REGION_NAME,\n'
        '            self.player,\n'
        '            self.multiworld,\n'
        '        )\n'
        '        self.multiworld.regions += [menu, pharloom, logic_unknown]',
        '        try:\n'
        '            menu = self.multiworld.get_region("Menu", self.player)\n'
        '        except KeyError:\n'
        '            menu = Region("Menu", self.player, self.multiworld)\n'
        '            self.multiworld.regions.append(menu)\n'
        '        pharloom = Region("Pharloom", self.player, self.multiworld)\n'
        '        logic_unknown = Region(\n'
        '            LOGIC_UNKNOWN_REGION_NAME,\n'
        '            self.player,\n'
        '            self.multiworld,\n'
        '        )\n'
        '        self.multiworld.regions += [pharloom, logic_unknown]',
        "silksong/__init__.py create_regions Menu reuse",
    )
    t = must_replace(
        t,
        '        menu.connect(pharloom)\n'
        '        self.create_entrance(\n'
        '            pharloom,\n'
        '            logic_unknown,\n'
        '            Has("Victory"),\n'
        '            "Silksong Logic: LogicUnknown",\n'
        '        )\n'
        '        connect_native_logic_regions(self, menu, native_regions)',
        '        if getattr(self, "has_hk", False):\n'
        '            self.create_entrance(\n'
        '                menu,\n'
        '                pharloom,\n'
        '                Has("Town"),\n'
        '                "Dirtmouth Gate -> Pharloom",\n'
        '            )\n'
        '            silksong_entry = pharloom\n'
        '        else:\n'
        '            menu.connect(pharloom)\n'
        '            silksong_entry = menu\n'
        '        self.create_entrance(\n'
        '            pharloom,\n'
        '            logic_unknown,\n'
        '            Has("Victory"),\n'
        '            "Silksong Logic: LogicUnknown",\n'
        '        )\n'
        '        connect_native_logic_regions(self, silksong_entry, native_regions)',
        "silksong/__init__.py create_regions Dirtmouth gate",
    )
    # Filter addressed_unfilled_locations in create_items to SilksongLocation only (so HK locations in both mode don't trip lane count)
    t = must_replace(
        t,
        "                    location.player == self.player\n"
        "                    and location.address is not None\n"
        "                    and location.item is None",
        "                    location.player == self.player\n"
        "                    and isinstance(location, SilksongLocation)\n"
        "                    and location.address is not None\n"
        "                    and location.item is None",
        "silksong/__init__.py addressed_unfilled_locations filter",
    )
    # In stage_post_fill, restore combined completion rule via _refresh_completion_rule
    t = must_replace(
        t,
        '                world.set_completion_rule(\n'
        '                    world._silksong_rule_builder_rules["Completion"]\n'
        '                )',
        '                world._refresh_completion_rule()',
        "silksong/__init__.py stage_post_fill completion rule",
    )
    # In stage_pre_fill, count Silk & Soul point items (events stay unprefixed, items stripped)
    t = must_replace(
        t,
        "        point_items = Counter((item.player, item.name) for item in multiworld.get_items())",
        "        point_items = Counter((item.player, strip_ss_prefix(item.name)) for item in multiworld.get_items())",
        "silksong/__init__.py stage_pre_fill point_items",
    )
    # Strip SS prefix in SilksongWorld's internal item.name / location.name checks
    t = must_replace(
        t,
        "        if item.name == PROGRESSIVE_SWIFT_STEP_ITEM and 'Dash' in self.get_directional_abilities():",
        "        if strip_ss_prefix(item.name) == PROGRESSIVE_SWIFT_STEP_ITEM and 'Dash' in self.get_directional_abilities():",
        "silksong/__init__.py collect_item progressive swift step",
    )
    t = must_replace(
        t,
        "            and not (location.player in silksong_players and location.name in CRAFTMETAL_PURCHASE_LOCATIONS)",
        "            and not (location.player in silksong_players and strip_ss_prefix(location.name) in CRAFTMETAL_PURCHASE_LOCATIONS)",
        "silksong/__init__.py stage_post_fill craftmetal",
    )
    # Strip SS prefix in _prepare_start_inventory_pool / precollected / exclude_locations checks
    t = re.sub(
        r"\b(location|loc)\.item\.name\b(?!\s*=[^=])",
        r"strip_ss_prefix(\1.item.name)",
        t,
    )
    t = re.sub(
        r"(?<!\.)\b(item|location|loc)\.name\b(?!\s*=[^=])",
        r"strip_ss_prefix(\1.name)",
        t,
    )
    # Restore collect() / _collect_with_silk_supply() exact item.name usage (handled by HKSSWorld)
    init_p.write_text(t, encoding="utf-8")


def package_apworld(apworld_dir: Path, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file_path in sorted(apworld_dir.rglob("*")):
            if file_path.is_dir():
                continue
            if "__pycache__" in file_path.parts or file_path.suffix in {".pyc", ".pyo"}:
                continue
            arcname = Path("hkss") / file_path.relative_to(apworld_dir)
            zf.write(file_path, arcname.as_posix())


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--hk-src", type=Path, default=DEFAULT_HK_SRC)
    parser.add_argument("--ss-src", type=Path, default=DEFAULT_SS_SRC)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--vendor-only", action="store_true", help="Vendor sources without zipping .apworld")
    args = parser.parse_args()

    if not args.hk_src.is_dir():
        raise SystemExit(f"Missing Hollow Knight reference world at {args.hk_src}. Run tools/fetch-references.ps1 first.")
    if not args.ss_src.is_dir():
        raise SystemExit(f"Missing Silksong reference world at {args.ss_src}. Run tools/fetch-references.ps1 first.")

    vendor_hk(args.hk_src, APWORLD_DIR / "hk")
    vendor_silksong(args.ss_src, APWORLD_DIR / "silksong")

    manifest = {
        "hk": {
            "upstream": "ArchipelagoMW/Archipelago (worlds/hk)",
            "commit": git_rev(REPO_ROOT / "local" / "reference" / "archipelago"),
            "license": "MIT",
        },
        "silksong": {
            "upstream": "Decryptu/silksong-decrypted (APWorld/silksong)",
            "commit": git_rev(REPO_ROOT / "local" / "reference" / "silksong-decrypted"),
            "license": "MIT",
        },
    }
    (APWORLD_DIR / "VENDORED.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    if not args.vendor_only:
        package_apworld(APWORLD_DIR, args.output)
        print(f"Built {args.output} ({args.output.stat().st_size:,} bytes)")
    else:
        print("Vendored hk and silksong into apworld/hkss/")


if __name__ == "__main__":
    main()

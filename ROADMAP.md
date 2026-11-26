# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 8. First playtest release (in progress) | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Next

Phase 5 (Silksong) is built and verified on Silksong 1.0.30000: Silksong Decrypted's item grants, manifests, 596 Harmony patch groups, map markers, links, and modes are ported into `HKSS.Silksong` (tracked in [docs/PARITY.md](docs/PARITY.md)) and wired onto `HKSS.Core` (`slot_data["silksong"]`, `SS ` prefixing, `IdSpace.IsSilksong` filtering, cross-game Hallownest display, shared goal reporting, and isolated `silksong_randomizer.xml` persistence).

Phase 7 (Player hub) is built and verified in a clean browser profile. The checklist, YAML generator and item map use the packaged world catalog. All four YAML presets passed generation in Archipelago 0.6.8.

v0.1.1 adds a launcher connection check, retry and bug report actions after launch or travel errors, and a [host smoke test](docs/PLAYTEST.md) for the first group session.

v0.1.2 fixes the diagnostic ZIP archive writer, adds pre-launch checks for legacy Unity versions, leftover Lumafly/Modding API files, incomplete Steam installs, and running games, and captures BepInEx preloader logs and install diagnostics.

v0.1.3 fixes `CustomSkillItem` assembly resolution in Hollow Knight (which prevented randomized placements from loading on seeds with Focus/Swim/split skills), adds an in-game placement load error alert, hardens the startup update check, adds `.apworld` vs. launcher/plugin version mismatch warnings, clarifies automatic mod bundling in the launcher, and stops warning on Steam's default `"public"` beta key.

v0.1.4 fixes Hollow Knight ItemChanger lifecycle, Archipelago state sync, and launcher reliability bugs, and ports Hollow Knight DeathLink, shop and preview auto-hinting, helper and start platforms, alternate start locations, repickable Delicate Flower, and the `F9` recent items feed.

v0.1.5 fixes undismissable Hollow Knight big item popups (`CanvasGroup` missing on `CanvasUtil.CreateCanvas`), fixes corner item popups, shops, hint boxes, and dialogue by migrating ItemChanger from `TMPro` to Hollow Knight 1.5.12620's `TMProOld`, fixes lost custom skills (nail slashes, Focus, split abilities) after relaunch by loading remote items before granting and registering `SplitNail`, and reconciles already-obtained custom skills on save load.

v0.1.6 hardens Hollow Knight, Silksong, Archipelago sync, the APWorld, and the launcher, and adds cross-game QoL parity: adds Hollow Knight map check pins + automatic Wayward Compass, fast non-blocking item pickups, instant Stag presence + fast Stag travel skip, and fast dialogue across both Hollow Knight and Silksong; filters Silksong save validation and official hints to Silksong IDs, fixes Silksong DeathLink YAML support, socket error handling, `.pending`/`.bak` save recovery, and automatic AP reconnect, unlocks the cursor in the F6 overlay, defers Hollow Knight remote check sync until gameplay is ready, fixes Hollow Knight head-of-line item deadlock, Unity 6 `SendHeroInPosition` respawns, late-connect `OnEnterGame`, `ScenePreloader`, and split-skill null guards, fixes APWorld `CostSanity`, `start_inventory_from_pool`, `early_sprint`, `hollow_knight_only`, and prefix/completion-rule bugs, and hardens launcher slot recovery, `cover.json` travel locking, room validation, LAN `ws://` fallback, NTFS case-only updates, and reparse-point safety.

v0.1.7 fixes `F4` town warp spawning the Knight and Dirtmouth travel portal underground when warping from a room that contains a bench, by scoping `TownGate`'s `RestBench`, gate-existence, and ground raycast lookups strictly to the target `Town`/`Bonetown` scene.

v0.1.8 upgrades the Hollow Knight in-game check map to full Silksong parity (19 category/shop pins, room clusters, hint outlines, hover tooltips for controller and mouse, visibility and check filters, and live `worlds/hk` reachability logic toggled with `F10`), and fixes returning to Silksong from Hollow Knight dropping Hornet back in `Tut_01` by accepting the runtime Bone Bottom gate respawn marker in `GameManager.GetRespawnInfo`.

v0.1.9 fixes a Hornet 1 (`Fungus1_04`) arena gate softlock caused by `MothwingCloakLocation.EditCloakCorpse` throwing `IndexOutOfRangeException` before attaching the `BG OPEN` gate-open broadcast, harmonizes Hollow Knight map pin scale and cluster spacing with Silksong, adds a unified Benchwarp panel (`F7`) and Last Bench warp (`F8`) across both games with persistent discovered-bench tracking, adds Hollow Knight `BenchSyncModule` (slot DataStorage bench sync) and `GiftingModule` (AP GiftBox v2/v3 send & receive), adds Hollow Knight `SoulLink`, `GeoLink`, and cross-game `KnockbackLink`, adds instant HK cutscene skip, optional boss roar lock skip, bulk relic/egg selling (Lemm, Jinn, Scrounge), and a configurable HK enemy Geo multiplier, and upgrades the `F6` overlay with Connection, Benchwarp, and Links & QoL tabs.

v0.1.10 adds Hollow Knight's Archipelago Inventory and Hints tabs, fixes randomized shop and Grubfather pricing when rolled price bounds cross, and keeps the mouse unlocked while the Benchwarp panel is open.

v0.1.11 adds shared settings for auto-quill, check pin filters, incoming DeathLink shade and cocoon handling, bench recovery, and rebindable shortcuts. Hollow Knight travel guards now check battle gates directly so a waiting encounter controller no longer blocks Benchwarp.

v0.1.12 fixes Forgotten Crossroads spike grub logic when Split Mothwing Cloak is enabled, requiring left dash in both the world and the in-game check map.

v0.1.13 persists both games' goal completion in `state.json` and syncs both halves on connect from either game, shows a toast when the first game's goal is completed, displays both games' goal status in Hollow Knight's `F6` Inventory tab, Silksong's Archipelago inventory, and the launcher's run slots, auto-reloads Slot 1 after ending credits so players return straight to their last bench to travel to the other game, and aligns the plugin version with the release version.

v0.1.14 adds combined end-of-run stats (play time, completion, deaths, kills, and checks across both games in the `F6` **Stats** tab and launcher run Settings), ships the PopTracker pack (`dist/hkss-poptracker.zip`) with item grids, area overview maps, Archipelago auto-tracking, and full per-slot logic for both games, fixes `CostSanity` generation in both-games slots, fixes Silksong `skill_randomization: vanilla` generation (Clawline Statue reachability cycle), adds tracker slot-data hints for vanilla Silksong pickups and split Shade Cloak, and updates the vendored Silksong world to `v0.5.5-rc.6`.

v0.1.15 adds unified combo shuffle options in the `Combo` option group (`shuffle_entrances`, `shuffle_charms_and_crests`, `shuffle_shops`, `shuffle_grubs_and_fleas`, `shuffle_journal_entries`, `shuffle_lore_tablets`), implements Hollow Knight coupled entrance/room randomization (`interiors`, `within_areas`, `full`) verified against `GeneratedRules.py`, hardens end-of-run death/kill stats against double-counting and new-seed leaks, and makes PopTracker location auto-tracking idempotent.

v0.1.16 adds the opt-in `cross_game_seals` option (0 to 4 areas per game) sealing areas until a key ability from the other game is found, with generation logic, doorway bounces, `F6` Connection tab listing, Hollow Knight check map integration, PopTracker pack support, and spoiler log output.

v0.1.17 finishes cross-game seals (Silksong in-game check map seal logic and fast-travel warp blocks across Benchwarp, Stags, trams, Dream Gate, Bellways, Ventricas, and Beastling Call), adds opt-in `cross_game_items` (`Shared Dash`, `Shared Wall Jump`, `Shared Double Jump`, `Shared Swim`, and matching directional splits across APWorld, both plugins, both in-game check maps, PopTracker, and spoiler log), adds opt-in `double_steel_soul` (Steel Soul across both games, shared defeat state in `state.json` even while the other game is closed, defeat overlays in both games and the launcher, and one-click **Reset run** with fresh saves in both), and adds in-place launcher updates from GitHub Releases (startup and manual checks, streaming download with progress bar, SHA-256/size/PE verification, passive NSIS install with automatic relaunch, and startup cleanup). Still to verify in game: seal doorway bounces and warp blocks, seals opening while the other game is running, shared items granting in both games on pickup and cross-game receipt, and Double Steel Soul defeat propagation across both games.

v0.1.18 enables all 16 Hollow Knight `StartLocation` options in combo runs (`King's Pass`, `Stag Nest`, `West Crossroads`, `East Crossroads`, `Ancestral Mound`, `West Fog Canyon`, `East Fog Canyon`, `Queen's Station`, `Fungal Wastes`, `Greenpath`, `Lower Greenpath`, `West Blue Lake`, `City Storerooms`, `King's Station`, `Outside Colosseum`, and `Crystallized Mound`), exports `StartLocationName` in Hollow Knight slot data for the plugin and in-game check map, guards cross-game seals and coupled HK entrance randomization so Dirtmouth remains reachable without Silksong seal keys, updates `HkLogicEvaluator` and the PopTracker pack for alternate HK starts and Dirtmouth-gated Pharloom reachability, and memoizes HK seal scene lookups to speed up sealed entrance-rando generation. Still to verify in game: alternate Hollow Knight start spawns, helper platforms, and scene edits in a live combo run.

Next release: Silksong's alternate starting locations (`starting_location`) are supported in combo runs, and shuffled entrances include the Moss Grotto to Bone Bottom route. When either applies, the town warp to the Bone Bottom gate works from the first arrival in Pharloom, so the way back to Hallownest never depends on items. The Silksong entrance randomizer now judges its opening from the Dirtmouth gate, which fixes generation failures with alternate starts in both games. The PopTracker pack and the Hollow Knight in-game check map no longer count a transition as reached when its partner is sealed. Still to verify in game: each alternate Silksong start on the first trip from Dirtmouth, and the town warp to Bone Bottom before visiting it.

Also next release: **Instant travel**, an opt-in launcher setting that keeps the game you leave paused in memory, so travelling back to it skips the restart. Still to verify in game: travel in both directions several times, items and seal keys found in the other game arriving after waking, Links & QoL after waking, a Steel Soul death in the other game, closing the active game while the other is paused, full screen and several monitors, and low free memory falling back to closing.

Also next release: **Live tracking** on the player hub's item map. It follows a slot in a running room as checks are found, or reads the seed's spoiler log, with progress per area and what each found check held. Still to verify: following a room on archipelago.gg over `wss://` from the published hub.

Still to verify during playtesting: gate placement in altered town states (Grimm Troupe / Nymm in Dirtmouth; Skull Tyrant / Act 3 in Bone Bottom), exclusive full screen, several monitors, controllers, a slower PC, and a save that fails.

## Feature Parity & Missing Ports

Both games should offer a seamless, matching experience: any randomizer, UI, or QoL feature available in one game should exist in the other with consistent controls and visuals.

Cross-game QoL settings are ready for playtesting in the **Links & QoL** tab. Verify that auto-quill, check pin filtering, DeathLink shade/cocoon handling, bench recovery and shortcut changes behave consistently after travel in both directions.

Combined end-of-run stats are wired into `state.json`, the `F6` **Stats** tab, and the launcher's run Settings panel; confirm the in-game display updates cleanly after travelling in both directions.

Recheck Hollow Knight Benchwarp before triggering an encounter, while its battle gates are closed, and after they reopen.

## Decisions

- Target the current Steam builds (Hollow Knight 1.5.12620, Silksong 1.0.30000), not the legacy 1.5.78 modding branch.
- BepInEx 5 x64 for both games; HK Modding API features are rebuilt on MonoMod/HarmonyX.
- One Archipelago game (`Hollow Knight x Silksong`) with game-prefixed names and a fixed ID block per game.
- One unified codebase: existing mods are never dependencies. Port upstream code file by file with provenance headers; never merge upstream history. LGPL-2.1 code stays in separate assemblies.
- Modded games run from mirror folders; nothing is written to the Steam installs.
- Each combo run keeps both saves in its own folder; vanilla saves and Silksong Decrypted saves are never written.
- Runs start in Hallownest. Travel is gate to gate between Dirtmouth and Bone Bottom, one game open at a time.
- Target Archipelago 0.6.8 or newer.
- Player-facing tools are self-hosted on GitHub Pages and deployed by GitHub Actions.

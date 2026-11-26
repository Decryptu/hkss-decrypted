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

Still to verify during playtesting: gate placement in altered town states (Grimm Troupe / Nymm in Dirtmouth; Skull Tyrant / Act 3 in Bone Bottom), exclusive full screen, several monitors, controllers, a slower PC, a save that fails, a game that crashes during travel, and the Silksong plugin's refusal to run without the launcher.

## Feature Parity & Missing Ports

Both games should offer a seamless, matching experience: any randomizer, UI, or QoL feature available in one game should exist in the other with consistent controls and visuals.

Cross-game QoL settings are ready for playtesting in the **Links & QoL** tab. Verify that auto-quill, check pin filtering, DeathLink shade/cocoon handling, bench recovery and shortcut changes behave consistently after travel in both directions.

The combined end-of-run stats are ready for playtesting: each game records play time, completion, deaths, kills and Archipelago check counts in `state.json`; the `F6` **Stats** tab shows both games side by side (and opens on its own when the combo goal is completed), and the launcher shows the same table in a run's Settings panel. Verify that deaths and kills are counted once each, that totals survive travel in both directions, and that a new seed in the same run slot clears them.

Recheck Hollow Knight Benchwarp before triggering an encounter, while its battle gates are closed, and after they reopen.

Verify the Forgotten Crossroads spike grub in a newly generated split-cloak seed. The world and check map require left dash for that check when the cloak is split.

## Later

- More unified shuffles: entrances and rooms in combo mode, charms/crests, shops, grubs/fleas, journal entries, lore tablets.
- Cross-game rules and items (abilities that unlock routes in the other game) as opt-in options.
- Suspending the other game for instant travel as an option, PopTracker pack, live tracking on the player hub's map page.

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

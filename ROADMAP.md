# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 6. Launcher release | Installer with bundled mod files, updates, room settings per run, diagnostics ZIP, uninstall. | A fresh Windows profile goes from download to a running combo seed without manual file copying. |
| 8. First playtest release | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Next

Phase 5 (Silksong) is built and verified on Silksong 1.0.30000: Silksong Decrypted's item grants, manifests, 596 Harmony patch groups, map markers, links, and modes are ported into `HKSS.Silksong` (tracked in [docs/PARITY.md](docs/PARITY.md)) and wired onto `HKSS.Core` (`slot_data["silksong"]`, `SS ` prefixing, `IdSpace.IsSilksong` filtering, cross-game Hallownest display, shared goal reporting, and isolated `silksong_randomizer.xml` persistence).

Phase 6 (Launcher release) is built: `npm run release` makes a per-user NSIS installer with BepInEx and both plugins bundled (`HKSS_PAYLOAD` is now only a development override). Each run has Archipelago room settings (`ap.json`) and can be deleted from the launcher; a bug report ZIP collects the run's logs, BepInEx logs, and settings with the password hidden; the launcher checks GitHub Releases for a newer version; and the modded game folders can be removed without following their junctions into the Steam installs. v0.1.0 is the first playtest release.

Phase 7 (Player hub) is built and verified in a clean browser profile. The checklist, YAML generator and item map use the packaged world catalog. All four YAML presets passed generation in Archipelago 0.6.8.

v0.1.1 adds a launcher connection check, retry and bug report actions after launch or travel errors, and a [host smoke test](docs/PLAYTEST.md) for the first group session.

v0.1.2 fixes the diagnostic ZIP archive writer, adds pre-launch checks for legacy Unity versions, leftover Lumafly/Modding API files, incomplete Steam installs, and running games, and captures BepInEx preloader logs and install diagnostics.

v0.1.3 fixes `CustomSkillItem` assembly resolution in Hollow Knight (which prevented randomized placements from loading on seeds with Focus/Swim/split skills), adds an in-game placement load error alert, hardens the startup update check, adds `.apworld` vs. launcher/plugin version mismatch warnings, clarifies automatic mod bundling in the launcher, and stops warning on Steam's default `"public"` beta key.

v0.1.4 fixes Hollow Knight ItemChanger lifecycle, Archipelago state sync, and launcher reliability bugs, and ports Hollow Knight DeathLink, shop and preview auto-hinting, helper and start platforms, alternate start locations, repickable Delicate Flower, and the `F9` recent items feed.

v0.1.5 fixes undismissable Hollow Knight big item popups (`CanvasGroup` missing on `CanvasUtil.CreateCanvas`), fixes corner item popups, shops, hint boxes, and dialogue by migrating ItemChanger from `TMPro` to Hollow Knight 1.5.12620's `TMProOld`, fixes lost custom skills (nail slashes, Focus, split abilities) after relaunch by loading remote items before granting and registering `SplitNail`, and reconciles already-obtained custom skills on save load.

v0.1.6 hardens Hollow Knight, Silksong, Archipelago sync, the APWorld, and the launcher, and adds cross-game QoL parity: adds Hollow Knight map check pins + automatic Wayward Compass, fast non-blocking item pickups, instant Stag presence + fast Stag travel skip, and fast dialogue across both Hollow Knight and Silksong; filters Silksong save validation and official hints to Silksong IDs, fixes Silksong DeathLink YAML support, socket error handling, `.pending`/`.bak` save recovery, and automatic AP reconnect, unlocks the cursor in the F6 overlay, defers Hollow Knight remote check sync until gameplay is ready, fixes Hollow Knight head-of-line item deadlock, Unity 6 `SendHeroInPosition` respawns, late-connect `OnEnterGame`, `ScenePreloader`, and split-skill null guards, fixes APWorld `CostSanity`, `start_inventory_from_pool`, `early_sprint`, `hollow_knight_only`, and prefix/completion-rule bugs, and hardens launcher slot recovery, `cover.json` travel locking, room validation, LAN `ws://` fallback, NTFS case-only updates, and reparse-point safety.

Still to verify during playtesting: clean-PC installer test, gate placement in altered town states (Grimm Troupe / Nymm in Dirtmouth; Skull Tyrant / Act 3 in Bone Bottom), exclusive full screen, several monitors, controllers, a slower PC, a save that fails, a game that crashes during travel, and the Silksong plugin's refusal to run without the launcher.

## Feature Parity & Missing Ports

Both games should offer a seamless, matching experience: any randomizer, UI, or QoL feature available in one game should exist in the other with consistent controls and visuals.

### Missing from `Archipelago.HollowKnight` (in `HKSS.HollowKnight`)
- **Bench sync (`BenchSyncModule`) and Gifting (`GiftingModule`).**

### Cross-game parity (Silksong features missing in Hollow Knight)
- **In-game check map & logic markers (Hollow Knight):** Silksong already shows unchecked items, shops, category pins, clusters, and logic state (`F10`) on the in-game map. Build a clean-room MIT/LGPL in-game check map for Hollow Knight (using ItemChanger location coordinates and `worlds/hk` logic, without GPL-3.0 code) with matching pins, filters, and hotkeys.
- **In-game Archipelago progress & hints view:** bring an AP inventory/progress and hints view to Hollow Knight matching Silksong's.
- **Additional links:** optional Soul/Geo/Knockback links in Hollow Knight to match Silksong's SilkLink, CurrencyLink, and KnockbackLink.
- **Unified in-game settings panel:** one consistent, player-configurable options menu (in the pause menu or `F6` overlay) controlling client-side toggles and hotkeys across both games.

## Optional QoL Features (Player-Toggleable, Both Games)

All QoL features below are opt-in per player so casual and competitive runs can choose their preferred experience:

- **Pause-menu bench warping:**
  - **Warp to Last Bench** button in the pause menu (and optional hotkey) in both games.
  - Optional **Benchwarp** panel to warp to any discovered bench (port LGPL-2.1 `Benchwarp` for Hollow Knight; match it in Silksong alongside `FastTravelUtil`).
- **Pacing & dialogue speedups:**
  - Fast text / instant dialogue reveal (already in Silksong; add to Hollow Knight).
  - Skippable cutscenes and boss intros (already in Silksong; add to Hollow Knight).
  - Faster menus, faster Stag / Bellway arrivals, and bulk selling for relics/eggs (Lemm, Jinn, Scrounge).
- **Map & navigation helpers:**
  - Always-on compass without using a charm notch or tool slot (already in Silksong; add to Hollow Knight).
  - Optional auto-quill / full area map reveal and customizable map pin filters.
- **Economy & death options:**
  - Optional enemy currency drop multiplier (already in Silksong; add to Hollow Knight).
  - Configurable DeathLink shade/cocoon behavior and optional softlock-recovery safeguards.
- **Customizable hotkeys:** rebindable shortcuts for Town Warp (`F4`), AP Panel (`F6`), Recent Items (`F9`), Map Logic (`F10`), and Last-Bench Warp.

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

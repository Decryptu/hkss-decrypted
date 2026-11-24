# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 6. Launcher release | Installer with bundled mod files, updates, room settings per run, diagnostics ZIP, uninstall. | A fresh Windows profile goes from download to a running combo seed without manual file copying. |
| 7. Player hub | Setup checklist, YAML generator from the exported option schema, item map of both worlds. | Every page is generated from the shipped world and passes a clean-profile browser check. |
| 8. First playtest release | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Next

Phase 5 (Silksong) is built and verified on Silksong 1.0.30000: Silksong Decrypted's item grants, manifests, 596 Harmony patch groups, map markers, links, and modes are ported into `HKSS.Silksong` (tracked in [docs/PARITY.md](docs/PARITY.md)) and wired onto `HKSS.Core` (`slot_data["silksong"]`, `SS ` prefixing, `IdSpace.IsSilksong` filtering, cross-game Hallownest display, shared goal reporting, and isolated `silksong_randomizer.xml` persistence).

Phase 6 (Launcher release) is built: `npm run release` makes a per-user NSIS installer with BepInEx and both plugins bundled (`HKSS_PAYLOAD` is now only a development override). Each run has Archipelago room settings (`ap.json`) and can be deleted from the launcher; a bug report ZIP collects the run's logs, BepInEx logs, and settings with the password hidden; the launcher checks GitHub Releases for a newer version; and the modded game folders can be removed without following their junctions into the Steam installs. Next up: publish a first release and test the installer on a clean PC.

Still to verify later: gate placement in altered town states (Grimm Troupe / Nymm in Dirtmouth; Skull Tyrant / Act 3 in Bone Bottom), exclusive full screen, several monitors, controllers, a slower PC, a save that fails, and a game that crashes during travel. The Silksong plugin's refusal to run without the launcher is only tested on Hollow Knight.

## Later

- More shuffles: entrances and rooms in both games, charms/crests, shops, grubs/fleas, journal entries, lore tablets.
- Cross-game rules and items (abilities that unlock routes in the other game) as opt-in options.
- Suspending the other game for instant travel as an option, PopTracker pack, live tracking on the map page.

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

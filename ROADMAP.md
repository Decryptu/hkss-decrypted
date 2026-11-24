# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 5. Silksong | Silksong Decrypted's features ported onto the shared code, tracked in a parity table. | A full Silksong seed plays from this repository. |
| 6. Launcher release | Installer with bundled mod files, updates, room settings per run, diagnostics ZIP, uninstall. | A fresh Windows profile goes from download to a running combo seed without manual file copying. |
| 7. Player hub | Setup checklist, YAML generator from the exported option schema, item map of both worlds. | Every page is generated from the shipped world and passes a clean-profile browser check. |
| 8. First playtest release | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Next

Phase 4 (Hollow Knight) is built and verified on Hollow Knight 1.5.12620: the BepInEx/Harmony/MonoMod `HKSS.HollowKnight.ModApi` hook layer (including additive scene preloading and `TeamCherry.Localization` compatibility), the separate LGPL-2.1 `HKSS.HollowKnight.ItemChanger` assembly, and the Hollow Knight Archipelago client wired onto `HKSS.Core` (`slot_data["hollow_knight"]`, prefix-stripped location scouting, cross-game Pharloom placeholders, shop/cost/notch/module configuration, goal reporting, and isolated `itemchanger.json` persistence). Next up is **Phase 5 (Silksong)**:

1. Audit Silksong Decrypted's features and track them in a parity table.
2. Port Silksong Decrypted's item grants, collectible source patches, shop/quest/ability hooks, and goal detection onto `HKSS.Core` and `HKSS.Silksong`.
3. Verify a full generated Silksong seed from this repository on Silksong 1.0.30000.

Still to verify later: gate placement in altered town states (Grimm Troupe / Nymm in Dirtmouth; Skull Tyrant / Act 3 in Bone Bottom), exclusive full screen, several monitors, controllers, a slower PC, a save that fails, and a game that crashes during travel. The Silksong plugin's refusal to run without the launcher is only tested on Hollow Knight. A release also needs the launcher to bundle the mod files (it reads `HKSS_PAYLOAD` today).

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

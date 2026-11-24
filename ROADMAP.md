# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 1. Gates | Town gates in Dirtmouth and Bone Bottom with the game's own prompt, arrival at the gate, first-trip rules, warp to town. | Walking through a gate travels both ways, and each gate is checked in every state of its town. |
| 3. Thin slice | Archipelago session, item routing and shared connection and message interface in `HKSS.Core` / `HKSS.Unity`; one real pickup per game. | A pickup in Hallownest unlocks something in Pharloom and the reverse, in one slot. |
| 4. Hollow Knight | Hook layer replacing the Modding API, then the ItemChanger port and the rest of the client. | A full HK logic seed plays on the current build. |
| 5. Silksong | Silksong Decrypted's features ported onto the shared code, tracked in a parity table. | A full Silksong seed plays from this repository. |
| 6. Launcher release | Installer with bundled mod files, updates, room settings per run, diagnostics ZIP, uninstall. | A fresh Windows profile goes from download to a running combo seed without manual file copying. |
| 7. Player hub | Setup checklist, YAML generator from the exported option schema, item map of both worlds. | Every page is generated from the shipped world and passes a clean-profile browser check. |
| 8. First playtest release | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Next

Travel works between both games, triggered by F8 or a timer in debug plugins. In order:

1. Confirm in game that the planned gate visuals can be copied at runtime (Dream Gate style portal, Weaver door), or choose others.
2. Build the shared gate in `HKSS.Unity`: its own object, the game's "Enter" prompt, calling `Travel`. Pick ground in Dirtmouth (`Town`) and Bone Bottom (`Bonetown`) that is clear in every state of the town.
3. Arrive at the gate: a respawn marker at each gate, set as the respawn point before saving.
4. First-trip rule: Bone Bottom's gate opens when the player first reaches it, stored per run.
5. Refuse travel in memories, boss fights and cutscenes, not only without control.
6. Warp back to the current game's town.
7. Decide whether combo runs skip Silksong's opening, which waits for the player's first Bind.

Not tested yet: exclusive full screen, several monitors, controllers, a slower PC, a save that fails, and a game that crashes during travel. The Silksong plugin's refusal to run without the launcher is only tested on Hollow Knight. A release also needs the launcher to bundle the mod files (it reads `HKSS_PAYLOAD` today).

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

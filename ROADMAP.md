# Roadmap

What is in progress and planned. Finished work is not listed: git history has it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

## Phases

| Phase | Work | Done when |
|---|---|---|
| 0. Ground truth | Run BepInEx 5 on Hollow Knight 1.5.12620 from a launcher-style profile (Doorstop disabled in the game folder, enabled per launch). Generate `MMHOOK_Assembly-CSharp` with HookGenPatcher. Decompile both games and list what the Modding API hooks map to on the current build. | A test plugin logs scene changes and save/load in both games, and a Steam launch without arguments still starts vanilla. |
| 1. Shared core | `HKSS.Core`: Archipelago session, item routing by ID block, run registry, `hkss_` save redirection contract, switch protocol. Unit tests without a game. | Two simulated clients on one slot route items and goals correctly. |
| 2. Hollow Knight plugin | Hook layer replacing the Modding API, then the ItemChanger port, then the Archipelago client from Archipelago.HollowKnight. | A vanilla-logic HK seed from Archipelago's `Hollow Knight` world plays through its first checks on the current build. |
| 3. Combined world | `apworld/hkss` composing `worlds/hk` and silksong-decrypted's world: prefixed names, ID blocks, shared options, game access tickets, goals. Universal Tracker. | Solo and multiworld seeds generate on Archipelago 0.6.8 for `both`, `hollow_knight_only` and `silksong_only`. |
| 4. Silksong plugin | `HKSS.Silksong` from a silksong-decrypted snapshot, moved onto `HKSS.Core` and the combined ID space. | Items cross between the two games in one slot. |
| 5. Launcher | Tauri 2 launcher: Steam discovery, profiles, install/repair/uninstall, run management, game switching, diagnostics ZIP. | A fresh Windows profile goes from download to a running combo seed without manual file copying. |
| 6. Player hub | Setup checklist, YAML generator from the exported option schema, item map of both worlds. | Every page is generated from the shipped world and passes a clean-profile browser check. |
| 7. First playtest release | Matched launcher, plugins and `.apworld`. | A full combo run completes in a multiworld. |

## Later

- More shuffles: entrances and rooms in both games, charms/crests, shops, grubs/fleas, journal entries, lore tablets.
- Cross-game rules and items (abilities that unlock routes in the other game) as opt-in options.
- Both games open at once as an option, PopTracker pack, live tracking on the map page.

## Decisions

- Target the current Steam builds (Hollow Knight 1.5.12620, Silksong 1.0.30000), not the legacy 1.5.78 modding branch.
- BepInEx 5 x64 for both games; HK Modding API features are rebuilt on MonoMod/HarmonyX.
- One Archipelago game (`Hollow Knight x Silksong`) with game-prefixed names and a fixed ID block per game.
- Port upstream code file by file with provenance headers; never merge upstream history. LGPL-2.1 code stays in separate assemblies.
- HKSS saves use their own `hkss_` file names; vanilla saves and Silksong Decrypted saves are never written.
- Target Archipelago 0.6.8 or newer.
- Player-facing tools are self-hosted on GitHub Pages and deployed by GitHub Actions.

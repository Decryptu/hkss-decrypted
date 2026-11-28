# Roadmap

Planned features. Finished work is not listed: git history and the release notes have it. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the design.

## Goal

A combo randomizer and Archipelago world for Hollow Knight and Hollow Knight: Silksong that a group can install in one click, play across both games in one slot, and configure and track from the player hub.

Every feature should work the same way in both games, with matching controls and visuals.

## Planned features

In priority order. Check the licence of any mod before porting its code.

1. **More check types.** New world options for ropes, benches as items, extra doors and keys, and all-boss / Godhome boss checks (`boss_sanity` parity), with logic in the `.apworld`, both in-game check maps and the PopTracker pack.
2. **Chaos and challenge traps.** A world option that applies timed challenge modifiers and Silksong-style traps (darkness, stagger, currency spill, double enemies, a broken nail, attacking in one direction only), either on a timer or as received trap items, with parity across both games.
3. **Mode and split parity.** Port Silksong's Souls modes (`boss_souls`, `enemy_souls`, `npc_souls`) and Alphabet / Spelling Bee mode to Hollow Knight, and add directional attack splitting (matching Hollow Knight's `RandomizeNail`) to Silksong.
4. **Practice panel.** Save states, teleport, no-clip and invincibility, based on what DebugMod offers. It is off by default in a multiworld, and the run stats record that it was used.

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

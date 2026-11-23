# Architecture

HKSS Decrypted is a combo randomizer for *Hollow Knight* and *Hollow Knight: Silksong*: one Archipelago slot covers both games, items from either game can be found in the other, and the player switches between Hallownest and Pharloom during a run. It works the way OoTMM works for Ocarina of Time and Majora's Mask, except that the two games stay separate executables.

This document records the design decisions. It describes the target, not what is built today. [ROADMAP.md](../ROADMAP.md) tracks progress.

## Ground facts

| | Hollow Knight | Hollow Knight: Silksong |
|---|---|---|
| Steam app ID | `367520` | `1030300` |
| Version targeted | 1.5.12620 (current Steam build) | 1.0.30000 (current Steam build) |
| Engine | Unity `6000.0.61f1`, Mono, x64 | Unity `6000.0.50f1`, Mono, x64 |
| Executable | `hollow_knight.exe` | `Hollow Knight Silksong.exe` |
| Saves | `%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\userN.dat` | `...\Team Cherry\Hollow Knight Silksong\<steamid>\userN.dat` |
| Existing randomizer | Archipelago.HollowKnight (MIT) on ItemChanger, RandomizerMod, MenuChanger, Benchwarp (LGPL-2.1) and the HK Modding API | [Silksong Decrypted](https://github.com/Decryptu/silksong-decrypted) (MIT) on BepInEx 5 |
| Existing Archipelago world | `worlds/hk` in Archipelago (game `Hollow Knight`) | `silksong.apworld` (game `Hollow Knight: Silksong`) |

The HK Modding API and every HK mod built on it only support the legacy 1.5.78.11833 build. Since 1.5.12459 the game runs on Unity 6 with the new Input System, like Silksong. We do not keep a frozen legacy copy: we port the HK randomizer code we need onto the current build.

## Decisions

### 1. One mod loader for both games: BepInEx 5 (x64)

Both games are now Unity 6 Mono x64 builds, and Silksong Decrypted already runs on BepInEx 5.4.23.x. Using the same loader for Hollow Knight means:

- one install and update path, one profile layout, one launcher code path;
- one shared core library loaded the same way in both games;
- MonoMod and HarmonyX are available in both. `BepInEx.MonoMod.HookGenPatcher` generates `MMHOOK_Assembly-CSharp.dll`, so ported HK code can keep its `On.*` hook style.

The HK Modding API's `ModHooks` events (save/load, `GetPlayerDataBool/Int`, `LanguageGet`, scene change, `AfterSavegameLoad`, and others) are rebuilt as a small hook layer in `HKSS.HollowKnight` on top of MMHOOK and Harmony. Phase 0 verifies that BepInEx 5 starts on HK 1.5.12620 before anything else is built on it.

### 2. Monorepo, shared core, one plugin per game

```
src/
  HKSS.Core/                 netstandard2.1, no Unity game references
                             Archipelago session, item routing, run/save registry,
                             switch protocol, shared settings and logging
  HKSS.HollowKnight/         BepInEx plugin for Hollow Knight (MIT)
                             hook layer replacing the Modding API, menus, HUD, AP glue
  HKSS.HollowKnight.ItemChanger/
                             LGPL-2.1: ItemChanger port (and the parts of RandomizerMod,
                             MenuChanger and Benchwarp we need), kept as its own assembly
  HKSS.Silksong/             BepInEx plugin for Silksong, starting from Silksong Decrypted
launcher/                    HKSS Launcher (desktop app, see 6)
apworld/hkss/                the combined Archipelago world (see 3)
site/                        GitHub Pages player hub
tools/                       build, sync, export, decompile and save tools
```

`HKSS.Core` is plain .NET so it can be unit tested without a game and shared with tooling. Both plugins target `netstandard2.1` (what Silksong Decrypted uses; Unity 6 Mono supports it). ItemChanger is `net472` today; it moves to `netstandard2.1` during the port.

Upstream code is never merged with its history. Files are ported one at a time with a header naming the upstream repository, path and commit. `tools/fetch-references.ps1` clones every upstream into `local/reference/` for reading.

Silksong Decrypted keeps evolving in its own repository. `HKSS.Silksong` takes snapshots of it with `tools/sync` scripts (planned) rather than diverging: keep Silksong-specific gameplay changes upstream in silksong-decrypted when they are not combo-specific, then pull them here.

### 3. One Archipelago game: `Hollow Knight x Silksong`

The world is a single game in Archipelago (package `hkss`, game name `Hollow Knight x Silksong`). One YAML, one slot, one item pool.

- **Composition, not rewriting.** The world composes the two existing worlds' data: Hollow Knight regions, locations, items and rules from Archipelago's `worlds/hk` (itself generated from RandomizerMod), and Silksong's from silksong-decrypted's APWorld. A build step (`tools/build_apworld.py`, planned) vendors both into `apworld/hkss/hk/` and `apworld/hkss/silksong/` and rewrites their imports, so the shipped `.apworld` does not depend on whatever HK or Silksong world the player has installed.
- **Names.** Both games have items and locations with the same names (Mask Shard, for example). Every name gets a game prefix: `HK Mask Shard`, `SS Mask Shard`, `HK Crossroads_04 Mask Shard`. Display names in game drop the prefix.
- **IDs.** The game has its own ID space. Hollow Knight occupies one block and Silksong another, with a fixed offset from each upstream ID, so data can be resynced without renumbering. The block boundaries live in one table in `apworld/hkss/ids.py` and are mirrored in `HKSS.Core`.
- **Cross-game logic.** Each game's graph is kept as is. A `Menu` region connects to each game's start, and an option decides when the other game opens: from the start, or after finding a `Ticket` item for that game. Optional cross-game rules (for example a Silksong area requiring an HK item) come later, as opt-in options.
- **Options.** One option set with three layers: shared (goal, game access, DeathLink, hints, item links), Hollow Knight options (ported from `worlds/hk`), Silksong options (from silksong-decrypted). A `games` option allows `both`, `hollow_knight_only` or `silksong_only`, so the same world, client and hub also cover single-game play.
- **Goal.** Configurable: complete both games' goals (default), either, or a count of bosses/endings across both.
- **Tracker.** Universal Tracker support from day one (both upstreams already support it), then a PopTracker pack.

### 4. Two clients, one slot

Each game's plugin connects to the same Archipelago slot with its own connection. Archipelago allows several clients on one slot.

- **Items.** Each client receives the whole item stream. It applies only items in its own ID block and shows the others as messages ("sent to Pharloom"). Each game save stores how many items it has processed, so items found while the other game was running are applied on the next load.
- **Locations.** Each client reports only its own game's checks.
- **Goal.** Each client writes its game's completion to Archipelago data storage (`hkss_<team>_<slot>_goal_hk` / `_goal_ss`). The client that sees the configured goal satisfied sends the goal status.
- **Shared state.** DeathLink, hints and gifting are per slot and work from whichever game is running. Data storage holds anything both games must agree on.

### 5. Saves

The owner's existing saves and other projects' saves must never be touched.

- An **HKSS run** pairs one Hollow Knight save and one Silksong save with an Archipelago room, slot and seed. The run registry is `%LOCALAPPDATA%\HKSS\runs.json` (owned by `HKSS.Core`).
- Each plugin redirects its game's save file names for HKSS runs to an `hkss_` prefix (`hkss_user1.dat` and a sidecar `hkss_user1.json` for run state, item index and settings). Vanilla `userN.dat` files and Silksong Decrypted's `userN.randomizersave`/`.randomizerdata` and `randomizer_userN` files are left alone.
- The in-game slot menu shows HKSS runs, so starting a combo run never offers to overwrite a vanilla slot.
- Before writing a run for the first time, the plugin backs up the target file names if anything already exists there.

Developers back up both save folders with `tools/backup-saves.ps1` before any test that can write saves (see [DEVELOPMENT.md](../DEVELOPMENT.md#save-safety)).

### 6. HKSS Launcher

A combo run needs two modded games that start in the right save and hand over to each other. A launcher makes that one click and keeps the games' Steam installs vanilla when it is not in use.

**Stack.** Tauri 2 (Rust backend, TypeScript + Svelte 5 + Vite front end). The Rust side handles Steam discovery, files, processes and downloads; the web UI reuses the hub's game-menu styling so the launcher and site look like the same thing. Small single installer and auto-update through Tauri's updater with GitHub Releases.

**What it does.**

1. **Find the games.** Reads Steam's `libraryfolders.vdf` and `appmanifest_367520.acf` / `appmanifest_1030300.acf`, reports installed build IDs, and warns when a game updated past the tested build.
2. **Install into profiles, not the game folder.** BepInEx, the HKSS plugins and dependencies live in `%LOCALAPPDATA%\HKSS\profiles\<game>\BepInEx\`. Only the Doorstop proxy (`winhttp.dll` + `doorstop_config.ini` with `enabled = false`) goes into each game folder, so launching from Steam directly still starts the vanilla game.
3. **Launch.** Starts the game through Steam (`steam.exe -applaunch <appid>`) with Doorstop arguments (`--doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Preloader.dll`) and `--hkss-run <runId>`, the same way r2modman launches Steam games. Falls back to starting the executable directly when Steam is already running.
4. **Runs.** Create or join a run (room address, slot, password), pick which game to start in, resume an existing run, and open the run's save folder or logs.
5. **Switch games** (see 7).
6. **Help.** Room status, last items, links to the hub's YAML generator, map and setup guide, one-click diagnostics ZIP (logs from both games and the launcher, plugin versions, game build IDs).
7. **Uninstall/repair.** Removes the proxy files from the game folders and restores anything it replaced.

**Without the launcher.** Manual install remains supported for advanced players: install BepInEx 5 in each game, drop in the plugin ZIPs, connect from the in-game menu, and start the other game yourself when the plugin asks to switch.

### 7. Switching games

The player switches at a fixed kind of place in each game: benches in Hallownest and Bellway stations in Pharloom (to be playtested; the trigger is a single setting).

1. The player picks "Travel to Pharloom" (or "to Hallownest") at the switch point.
2. The plugin saves, writes `%LOCALAPPDATA%\HKSS\runtime\switch.json` (`runId`, `from`, `to`, time) and quits to desktop.
3. The launcher sees the game exit with a pending switch and launches the other game with `--hkss-run <runId>`.
4. The other plugin reads the run, skips the title screen, loads its paired save and applies any items received meanwhile.

Without the launcher, step 3 is a prompt telling the player to start the other game; step 4 still happens from the title screen. Keeping both games open at once is possible later as an option, but the first version runs one game at a time.

### 8. Player hub (GitHub Pages)

`site/` is a static site deployed from `main` by GitHub Actions. It is styled as a game menu: Silksong Decrypted's title-screen layout recoloured to mix Hollow Knight's blue soul light with Silksong's red embers.

Planned pages, all generated from the shipped world so they cannot drift:

- **Setup**: branching checklist (launcher or manual, join or host).
- **YAML generator**: schema exported from the world's options (`tools/export_yaml_schema.py`, as in Silksong Decrypted), presets, validation, import and export.
- **Item map**: both worlds' maps with every location, filterable by game, area and check type; later, live tracking from a spoiler log or the room.
- **Downloads and status**: latest launcher, plugins and `.apworld`, tested game builds.

Map images are generated locally from the game files; whether they are published is decided before the map page ships.

### Licensing

- Project code: MIT.
- Ported from Archipelago.HollowKnight, Archipelago (`worlds/hk`) and Silksong Decrypted: MIT, credited in [NOTICE.md](../NOTICE.md) and in file headers.
- Ported from ItemChanger, RandomizerMod, RandomizerCore, MenuChanger and Benchwarp: LGPL-2.1. That code stays in its own assembly (`HKSS.HollowKnight.ItemChanger` and siblings) with its license file and full source in this repository, so the MIT plugins only link to it.
- Game files, decompiled code and extracted assets are never committed.

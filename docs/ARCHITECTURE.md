# Architecture

HKSS Decrypted is a combo randomizer for *Hollow Knight* and *Hollow Knight: Silksong*: one Archipelago slot covers both games, items from either game can be found in the other, and the player switches between Hallownest and Pharloom during a run. It works the way OoTMM works for Ocarina of Time and Majora's Mask, except that the two games stay separate executables.

This document records the design decisions. It describes the target; sections marked *built* are implemented. [ROADMAP.md](../ROADMAP.md) tracks progress.

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

The HK Modding API's `ModHooks` events (save/load, `GetPlayerDataBool/Int`, `LanguageGet`, scene change, `AfterSavegameLoad`, and others) are rebuilt as a small hook layer in `HKSS.HollowKnight` on top of MMHOOK and Harmony. BepInEx 5.4.23.6 is verified on both current builds.

### 2. One codebase, shared as far as possible

The combo is one unified mod. It does not depend on, load or install any existing randomizer: the code we need from each is ported here, so both games get the same features and the same interface.

```
src/
  HKSS.Core/                 netstandard2.1, no Unity: run context, signals, the Archipelago session,
                             item routing, settings and seals. Unit tested without a game.
  HKSS.Unity/                shared Unity source, compiled into both plugins against each game's own
                             assemblies, so a difference between the games is a build error
  HKSS.HollowKnight/         BepInEx plugin for Hollow Knight (MIT)
  HKSS.Silksong/             BepInEx plugin for Silksong (MIT)
  HKSS.HollowKnight.ItemChanger/   LGPL-2.1: ItemChanger port and the parts of RandomizerMod,
                             MenuChanger and Benchwarp we need, kept as its own assembly
tests/HKSS.Core.Tests/
launcher/                    HKSS Launcher (Tauri 2, see 6)
apworld/hkss/                the combined Archipelago world (see 3)
site/                        GitHub Pages player hub
tools/                       build, export, decompile and save tools
```

The plugins target `netstandard2.1`. Code that only one game needs goes in that game's project; `#if HOLLOW_KNIGHT` / `#if SILKSONG` is for small differences in shared files.

Upstream code is never merged with its history. Files are ported one at a time with a header naming the upstream repository, path and commit, so later upstream fixes can be found and ported by hand. `tools/fetch-references.ps1` clones every upstream into `local/reference/` for reading.

Feature parity is tracked as a table once porting starts. The first release needs the core set on both sides; Silksong Decrypted's larger feature set follows.

### 3. One Archipelago game: `Hollow Knight x Silksong`

The world is a single game in Archipelago (package `hkss`, game name `Hollow Knight x Silksong`). One YAML, one slot, one item pool.

- **Composition, not rewriting.** The world composes the two existing worlds' data: Hollow Knight regions, locations, items and rules from Archipelago's `worlds/hk` (itself generated from RandomizerMod), and Silksong's from silksong-decrypted's APWorld. A build step (`tools/build_apworld.py`) vendors both into `apworld/hkss/hk/` and `apworld/hkss/silksong/` and rewrites their imports, so the shipped `.apworld` does not depend on whatever HK or Silksong world the player has installed.
- **Names.** Both games have items and locations with the same names (Mask Shard, for example). Every name gets a game prefix: `HK Mask Shard`, `SS Mask Shard`, `HK Crossroads_04 Mask Shard`. Display names in game drop the prefix.
- **IDs.** The game has its own ID space. Hollow Knight occupies one block and Silksong another, with a fixed offset from each upstream ID, so data can be resynced without renumbering. The block boundaries live in one table in `apworld/hkss/ids.py` and are mirrored in `HKSS.Core`.
- **Cross-game logic.** Each game's graph is kept as is. Play starts in Hallownest: `Menu` connects to the Hollow Knight start, the Dirtmouth gate connects to the Silksong start, and the Bone Bottom gate connects back (see 7). Single-game modes start in their own game. The opt-in `cross_game_seals` option (see below) adds rules that cross between the games.
- **Cross-game seals.** `cross_game_seals` (0 to 4, off by default, both games only) seals that many areas in each game. Each sealed area needs a key ability from the other game's item pool, for example Deepnest needs Faydown Cloak. `apworld/hkss/cross_game_areas.json` lists the sealable areas and their scenes. `tools/export_cross_game_areas.py` builds it from RandomizerMod's `rooms.json` and the Silksong room graph. Starting areas, gate towns, early hubs, areas entered by warps, and Silksong's Act 3 areas are never sealed. Logic is strict. In Hollow Knight, every check, event and transition in a sealed scene needs the key, and so does every transition into one. In Silksong, every entrance into a sealed area's room nodes and room events from outside the area needs it. Seals reach the clients as `slot_data["cross_game_seals"]`. In game, `SealGuard` sends the player back through the doorway they used and blocks fast-travel warps (Benchwarp, Stags, trams, Dream Gate, Bellways, Ventricas, and Beastling Call) until the key has been received. The doorway check is made after entrance randomization, through ItemChanger's `TransitionBlocker` in Hollow Knight and a `BeginSceneTransition` prefix in Silksong. Received keys are saved in `state.json`, so seals stay open offline. The `F6` Connection tab lists the seals, and both games' in-game check maps and the PopTracker pack treat sealed areas as out of logic.
- **Cross-game items.** `cross_game_items` (toggle, off by default, both games only) merges paired movement abilities in the item pool into single shared items in the combo ID block (`84,000,000`–`84,099,999`): `Shared Dash` (`Mothwing_Cloak` + `Swift Step`), `Shared Wall Jump` (`Mantis_Claw` + `Cling Grip`), `Shared Double Jump` (`Monarch_Wings` + `Faydown Cloak`), `Shared Swim` (`Swim` + `Swim`), and matching directional left/right pairs when split in both games. Each merged pair replaces both game-specific items with one shared item plus one filler item to keep the pool balanced, and is exported in `slot_data["cross_game_items"]` and the spoiler log. The registry lives in `apworld/hkss/cross_game_items.py` and is mirrored in `src/HKSS.Core/CrossGameItems.cs`. Both plugins apply shared items locally, both in-game check maps and the PopTracker pack expand them into both games' inventories, and `ItemRouter` routes them to both clients.
- **Double Steel Soul.** `double_steel_soul` (toggle, off by default; also toggleable per run in the launcher for offline or pre-connect runs) starts both games' saves in Steel Soul (`permadeathMode = 1` / `PermadeathModes.On`) and persists the mode and defeat state (`double_steel_soul`, `steel_soul_defeated`, `steel_soul_defeated_game`, `steel_soul_defeated_scene`) in `state.json`. A Steel Soul death in either game (`GameManager.PlayerDead` / `PermaDeath` scene) marks the shared run defeated even while the other game is closed, blocks travel, surfaces a full-screen defeat overlay in both games (including on launch), and highlights the defeat in the launcher with a one-click **Reset run** action that wipes both saves and defeat state while keeping room settings and the Double Steel Soul toggle.
- **Start With Full Maps.** `start_with_full_maps` (toggle, off by default) starts both games with every area map unlocked and every room already revealed on the map, without needing to buy map fragments or use the Quill. In Silksong it enables `start_with_maps` and `start_fully_mapped`; in Hollow Knight it exports `StartWithFullMaps` in `slot_data["hollow_knight"]["options"]` and adds `HkFullMapsModule`, which sets `hasMap`, `hasQuill`, `mapAllRooms`, `openedMapperShop`, and all area map flags on `PlayerData`.
- **Unified pool toggles.** Seven unified combo options set both games' own pool options. Each is `per_game` (default), `off` or `on`. `shuffle_maps` sets `RandomizeMaps` and `map_randomization`. `shuffle_fast_travel` sets `RandomizeStags`, `bellway_randomization` and `ventrica_randomization`. `shuffle_masks_and_vessels` sets `RandomizeMaskShards`, `RandomizeVesselFragments`, `mask_shard_randomization` and `spool_fragment_randomization`. `shuffle_relics` sets `RandomizeRelics` and `relic_randomization`. `shuffle_skills` sets `RandomizeSkills`, `skill_randomization` and `silk_skill_randomization`. `shuffle_heal` sets `RandomizeFocus` and `bind_randomization` (`on` gives one shared Bind, and the extra `per_crest` choice gives one Bind per crest). `shuffle_swim` sets `RandomizeSwim` and `swim_ability_rando`, which Cross-Game Items merges into `Shared Swim`. They are applied in `_apply_unified_pools` before either game's `generate_early`, so both plugins read the result from their usual slot data. The Silksong world refuses to generate when skills and Bellways are both vanilla, so `shuffle_skills: off` needs fast travel randomized, and the reverse.
- **Multiworld links.** Three unified combo options turn on matching links in both games. Each is `per_game` (default), `off` or `on`. `currency_link` sets `GeoLink` and `rosary_link`. `resource_link` sets `SoulLink` (the base 99 Soul vessel) and `silk_link` (the base Spool). `combo_knockback_link` sets `KnockbackLink` and `knockback_link`; both games use the same `KnockbackLink` bounce tag, so knockback crosses games. Shell Shard Link keeps its own Silksong option. Geo and Soul use their own team data-storage keys (`HKSS:GeoLink:v1:Geo`, `HKSS:SoulLink:v1:BaseSoul`), separate from the Silksong pools. Geo carried by a shade and Soul lost on death leave the shared pool, as Rosaries and Silk do in Silksong. A cracked Soul vessel limits what that player holds without draining the pool. The Hollow Knight plugin (`HkLinksManager`) reads the links from `slot_data["hollow_knight"]["options"]`; the `F6` overlay shows them read-only, and seeds from before these options use the local overlay toggles.
- **QoL and starting options.** Five unified combo options set both games' own options. Each defaults to `per_game`. `combo_automatic_compass` sets `AutomaticCompass` and `automatic_compass`. `enemy_currency_multiplier` (x1 to x10) sets `EnemyGeoMultiplier` and `enemy_rosary_multiplier`. `combo_faster_dialogue` sets `FasterDialogue` and `faster_dialogue`. `early_dash` sets `EarlyDash` and `early_sprint`. `starting_currency` (0 to 1000) sets `StartingGeo` and `starting_rosaries`. The Hollow Knight options reach the plugin in `slot_data["hollow_knight"]["options"]`. Seeds from before these options keep the compass and fast dialogue always on and use the local Geo multiplier setting. When the seed sets the Geo multiplier, the `F6` overlay shows it read-only. Starting Rosaries are granted once per save, and `SaveState.startingRosariesGranted` records the grant. Early items are placed locally. Before fill, their names are remapped to the prefixed names in the pool, or to the shared item when Cross-Game Items merges them. With both games in the slot, early items go to Hallownest checks, because runs start there. If the locked early items leave the progression fill stuck, they are released and placed normally, so early placement is best effort.
- **Extra check types.** Unified combo toggles (`shuffle_levers`, `shuffle_breakable_walls`, `shuffle_grass`) and per-game toggles (`RandomizeLevers`, `RandomizeBreakableWalls`, `RandomizeGrass`, `lever_randomization`, `breakable_wall_randomization`, `grass_randomization`) turn levers, breakable walls (including dive floors in Hollow Knight), and grass-bearing rooms into Archipelago locations balanced 1:1 with matching filler items. Objects keep their normal vanilla behavior when hit or cut and also send their check. Metadata and coordinates live in `apworld/hkss/extra_checks_data.json` (generated by `tools/export_extra_checks.py`) in dedicated sub-ranges of each game's ID block (`84,150,000+` for Hollow Knight, `84,250,000+` for Silksong), embedded into `HKSS.Core`, evaluated by both in-game check maps, and exported to the player hub and PopTracker pack.
- **Options.** One option set with three layers: shared (goal, DeathLink, hints, item links), Hollow Knight options (ported from `worlds/hk`), Silksong options (from silksong-decrypted). A `games` option allows `both`, `hollow_knight_only` or `silksong_only`, so the same world, client and hub also cover single-game play.
- **Goal.** Configurable: complete both games' goals (default), either, or a count of bosses/endings across both.
- **Tracker.** Universal Tracker support from day one (both upstreams already support it), then a PopTracker pack.

### 4. Two clients, one slot

Each game's plugin connects to the same Archipelago slot with its own connection. Archipelago allows several clients on one slot.

- **Items.** Each client receives the whole item stream. It applies only items in its own ID block and shows the others as messages ("sent to Pharloom"). Placing items in the world uses the same ID check: upstream clients treat any item for their own slot as theirs, which is wrong when one slot spans two games. Each game save stores how many items it has processed, so items found while the other game was running are applied on the next load.
- **Locations.** Each client reports only its own game's checks.
- **Goal.** Each client writes its game's completion to Archipelago data storage (`hkss_<team>_<slot>_goal_hk` / `_goal_ss`). The client that sees the configured goal satisfied sends the goal status.
- **Shared state.** DeathLink, hints and gifting are per slot and work from whichever game is running. Data storage holds anything both games must agree on.

### 5. Saves

*Built.* The player's own saves and other projects' saves are never touched.

- A **combo run** is one folder, `%LOCALAPPDATA%\HKSS\runs\slot-<n>\`, holding `run.json` (which game the player is in), one save per game under `saves/<game>/`, logs and signals. The launcher shows four run slots; the player never sees an in-game save menu.
- Each game sets its save folder once, in `DesktopPlatform.Awake`. The plugin replaces that path with the run's folder, and save files are named `hkss_user1.dat`. Hollow Knight's shared data no longer mirrors into the registry, and `AppConfig.ini` is not rewritten.
- Steam achievements and stats are not sent during a run.
- **Fail closed.** The plugin ends the process at once if the launcher did not start it, if any save patch cannot be applied, or if a save path is outside the run. The launcher ends a game whose plugin does not report in within 20 seconds.
- Steam must be running: Silksong adds the Steam user ID to its save path, so a run played without Steam would look empty. The launcher checks this.
- Settings (controls, language, video) are shared with normal play through the games' own preferences.

Developers back up both save folders with `tools/backup-saves.ps1` before any test that can write saves (see [DEVELOPMENT.md](../DEVELOPMENT.md#save-safety)).

### 6. HKSS Launcher

A combo run needs two modded games that start in the right save and hand over to each other. The launcher makes that one click and leaves the Steam installs alone.

**Stack.** Tauri 2 (Rust back end, TypeScript + Svelte 5 + Vite front end), reusing the hub's stylesheet, backdrop and logo.

**Built.**

1. **Find the games.** Reads Steam's registry entry, `libraryfolders.vdf` and app manifests, and warns about untested builds.
2. **Mirror, do not install.** Each game runs from `%LOCALAPPDATA%\HKSS\games\<game>\`: copies of the few top-level files, directory junctions to the Steam data folders, BepInEx, the plugin and `steam_appid.txt`. Nothing is written to the Steam folder, so a Steam launch is always vanilla. Both games quit when they are not started through Steam unless `steam_appid.txt` is present; the player must still own the game and have Steam running.
3. **Play.** Starts the run's current game with `HKSS_RUN_DIR` and `HKSS_SESSION` in its environment.
4. **Switch games** behind the travel screen (see 7).

5. **Manage runs and updates.** Launcher-wide settings in `launcher.json` (instant travel, see 7), per-run Archipelago room settings with a connection test, deleting runs, a diagnostics ZIP, removing the mirrored game folders, and in-place updates from GitHub Releases (checks `releases/latest` on startup or on demand in Launcher settings, downloads the Windows NSIS setup executable into `%LOCALAPPDATA%\HKSS\updates\`, verifies its size, PE header, and SHA-256 digest from GitHub's asset metadata, and launches it in passive update mode with automatic relaunch; stale installers are removed on startup). The NSIS installer bundles the mod files.

A mirror contains junctions into the Steam library. Remove one link by link, never with a recursive delete that follows junctions.

### 7. Travelling between games

**For the player.** A run starts in Hallownest. Dirtmouth has a gate to Pharloom. The first trip starts Silksong from its beginning, or from the chosen starting location; on reaching Bone Bottom its gate opens, and from then on travel is town to town. A warp back to the current game's town covers getting stuck; changing games always uses the gate.

**Gates** (built). A standing door with the game's normal "Enter" prompt, built at runtime from the player's own game assets (a Dream Gate style portal in Dirtmouth, a Weaver door in Bone Bottom). The plugin creates it as its own object on ground that is clear in every state of the town, because both towns change: Bone Bottom in Act 3 (`Black Thread States`, `seenBonetownDestroyed`) and after the Skull Tyrant (`skullKingInvaded`), Dirtmouth with the Grimm Troupe, Divine and Nymm. Travel is refused without control, in memories and in boss fights.

**Reaching Bone Bottom.** Silksong's vanilla start leads to Bone Bottom without any item. Randomized Ledge Grab moves the start to Bone Bottom itself, as in Silksong Decrypted. Combo runs also accept Silksong's other starting locations (`starting_location`), and shuffled entrances include the Moss Grotto to Bone Bottom route. Those openings may not reach Bone Bottom without items, so the town warp to the Bone Bottom gate works from the first arrival in Pharloom, and the way back to Hallownest never depends on items. The world judges Silksong's opening from the Dirtmouth gate, which Hollow Knight progression opens.

**One game at a time** (built, the default). Keeping both open costs about 3.5 GB of private memory, so the default closes one and starts the other:

1. The game fades out picture and sound and saves. If saving fails, travel is cancelled and the player keeps playing.
2. The plugin writes a `travel` signal. The launcher raises its full-screen travel screen and confirms it; the game then closes (or after 3 seconds without an answer).
3. The launcher records the new game in `run.json` and starts it, muted, behind the travel screen. The plugin shortens the logo and menu waits, loads the save (or starts a new game and skips its opening films) and writes `ready` once the player is in the level.
4. The launcher puts a windowed game where the other one was, gives it focus and fades the travel screen out.

If a game closes or crashes without asking to travel, the run stays in that game and the launcher returns to its menu. If it crashes after `travel`, the save is already on disk.

**Instant travel** (built, opt-in in Launcher settings; 16 GB of memory recommended). The game the player leaves stays in memory, paused, so going back to it skips the restart:

1. Steps 1 and 2 as above, but the cover carries `"park": true`. Instead of closing, the game answers `parked` and stays still: faded out, muted, without control, at the gate.
2. The launcher hides its window, suspends the process (`NtSuspendProcess`) and starts or wakes the other game.
3. To wake it, the launcher writes a `resume` cover for its session, resumes the process and shows its window. The plugin logs in to the room again (a paused socket may have timed out; the server replays every item, so ones found meanwhile are applied), shows the Double Steel Soul defeat if the other game fell, fades in, hands back control and writes `ready`. The player is at the gate, where a cold start would have put them.

Every fallback is the normal path, because the paused game saved before it parked: with less than 3 GB of free memory the game closes as usual (unless the other game is the one paused); a game that cannot be suspended, or does not answer within 20 seconds of waking, is closed and started from its save. When the run ends, however it ends, the paused game is closed. Its process ID is kept in `parked-<game>.json` under the HKSS folder, so a launcher that crashed leaves nothing behind: the next start ends that process if it still runs from the mirror folder.

`state.json` is always read, changed and written back in one step, never cached, so a woken game does not undo what the other game recorded.

**Signals** are one JSON file per game in the run's `signals` folder, replaced atomically and tagged with the launch session so old files are ignored.

### 8. Player hub (GitHub Pages)

`site/` is a static site deployed from `main` by GitHub Actions. It is styled as a game menu: Silksong Decrypted's title-screen layout recoloured to mix Hollow Knight's blue soul light with Silksong's red embers.

The pages share a catalog exported from the shipped `.apworld`. The setup and download links use the catalog’s release version. See [Development](../DEVELOPMENT.md#player-hub) for the refresh and verification commands.

- **Setup** offers separate steps for players, hosts and hosts who also play. The host checklist links directly to the matching `.apworld` and explains how to open it. Manual installation is available as a fallback.
- **YAML generator** builds its form from the world’s Archipelago template options and supports presets, validation, single-slot import and download.
- **Item map** renders illustrated ink-and-parchment cartography for Hallownest, Pharloom, and White Palace & Path of Pain from room-accurate coordinates (`data/map_geometry.json`, rendered by `tools/render_maps.py`), with pan/zoom, room-level pins, area filter chips, and all registered check IDs. Players can filter by game, area, room or check type. The atlas uses project styling and does not require game artwork.
- **Live tracking** on the item map (`site/tracker.js`) follows one slot. It can follow a running room, open the seed's spoiler log, or both.
  - **Room.** The page joins with the `Tracker` tag and `items_handling` 0, so it never sends checks or receives items. `Connected` gives the seed's checks and the ones already found. `RoomUpdate` adds new finds, which light up their area tiles. `LocationScouts` with `create_as_hint` 0 names what each found check held, using item names from the room's data package. On an https page, addresses without a scheme use `wss://` only, except for this computer. A dropped connection retries with backoff and keeps the progress on screen.
  - **Spoiler log.** The map shows the slot's checks and what each one holds. Contents of checks not found yet stay hidden unless the player shows them. Without a room, players mark checks found by hand.
  - **Saved in the browser.** The room address, slot name, parsed spoiler and marks are kept in `localStorage`, with marks per seed and player. The password is never stored.

### Licensing

- Project code: MIT.
- Ported from Archipelago.HollowKnight, Archipelago (`worlds/hk`), Silksong Decrypted, Custom Knight and Patchwork: MIT, credited in [NOTICE.md](../NOTICE.md) and in file headers.
- Ported from ItemChanger, RandomizerMod, RandomizerCore, MenuChanger and Benchwarp: LGPL-2.1. That code stays in its own assembly (`HKSS.HollowKnight.ItemChanger` and siblings) with its license file and full source in this repository, so the MIT plugins only link to it.
- Game files, decompiled code and extracted assets are never committed.

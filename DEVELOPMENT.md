# Development

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first. This file covers the local setup, tools, and conventions.

## Layout

| Path | Contents |
|---|---|
| `docs/` | Architecture and design notes |
| `site/` | GitHub Pages player hub (static, no build step) |
| `tools/` | Publication checker, reference fetcher, decompiler wrapper, save backup |
| `local/` | Ignored: reference checkouts, decompiled code, extracted assets, save backups |
| `src/`, `tests/` | Plugins, shared code and tests, see the architecture's [layout](docs/ARCHITECTURE.md#2-one-codebase-shared-as-far-as-possible) |
| `launcher/` | HKSS Launcher (Tauri 2) |
| `apworld/hkss/` | Combined Archipelago world (`hk/` and `silksong/` are vendored at build time) |

## Requirements

- Windows 10/11 with both games installed through Steam.
- .NET 10 SDK (plugins and tests), Python 3.12+ (world and tools), Node 22+, Rust stable and the Visual Studio C++ build tools (launcher).
- Archipelago 0.6.8 or newer.
- `ilspycmd` for decompiling. Set `ILSPYCMD_PATH` if it is not at the default path in `tools/decompile.ps1`.
- Python packages for asset work: `UnityPy`, `Pillow`.

## First-time setup

```powershell
git config core.hooksPath .githooks
pwsh tools/fetch-references.ps1
pwsh tools/backup-saves.ps1
```

`fetch-references.ps1` clones the upstream projects we port from into `local/reference/`: the Hollow Knight Archipelago client, ItemChanger, RandomizerMod, RandomizerCore, MenuChanger, Benchwarp, the HK Modding API (to see which hooks we must rebuild), Silksong Decrypted, and Archipelago 0.6.8's `worlds/hk`.

## Building and running

```powershell
dotnet test tests/HKSS.Core.Tests
python tools/build_apworld.py                # vendors worlds/hk and silksong into dist/hkss.apworld
python tools/test_apworld.py                 # verifies IDs, archive, solo and multiworld generation on AP 0.6.8
pwsh tools/build-payload.ps1                 # BepInEx and both plugins into local/payload
cd launcher
npm install
npm run tauri build -- --debug --no-bundle   # src-tauri/target/debug/hkss-launcher.exe
cargo test --manifest-path src-tauri/Cargo.toml
npm run release   # after tools/build-payload.ps1 -Configuration Release: NSIS installer with the payload bundled
```

`tools/fetch-deps.ps1` (called by `build-payload.ps1`) downloads the pinned BepInEx and checks its hash. Set `STEAM_COMMON` if the games are not in the default Steam library.

| Variable | Effect |
|---|---|
| `HKSS_PAYLOAD` | Mod files to use, normally `local/payload`. Overrides the payload bundled with the installed launcher. |
| `HKSS_HOME` | Replaces `%LOCALAPPDATA%\HKSS`. Use a short path for tests. |
| `HKSS_DEV_PLAY` | Starts that run slot without a click; the launcher exits when it ends. |
| `HKSS_DEV_AUTOTRAVEL` | Debug plugins only: travels this many seconds after the game is ready, once the player has control. F8 travels at once. |
| `HKSS_DEV_START_IN_TOWN` | Debug plugins only: `hk`, `ss`, or `both` warps a fresh run from the tutorial scene straight to that game's town gate behind the travel screen. |
| `HKSS_DEV_SCREENSHOT_DIR` | Debug plugins only: saves `<game>.png` into this folder shortly after the game becomes ready. |
| `HKSS_AP_HOST` | Overrides the Archipelago server host (accepts `host` or `host:port`) from `ap.json`. Press `F6` in game to open the connection panel. |
| `HKSS_AP_PORT` | Overrides the Archipelago server port from `ap.json`. |
| `HKSS_AP_SLOT` | Overrides the Archipelago slot name from `ap.json`. |
| `HKSS_AP_PASSWORD` | Overrides the Archipelago room password from `ap.json`. |

Run logs are in `runs/slot-<n>/logs/`: `launcher.log` has timings and memory, `<game>-player.log` is Unity's log. BepInEx logs to `games/<game>/BepInEx/LogOutput.log`.

The Archipelago panel opens with F6 by default. Under **Links & QoL**, both games share the same run-local settings in `player-options.cfg`. Auto-quill fills visited rooms where you own the area map. The check pin filter can use the game's setting, show unchecked or checked pins, or filter unchecked pins by reachability. The DeathLink shade/cocoon setting applies to incoming deaths and defaults to the room setting. Bench recovery is opt-in and can return you to a discovered bench when input is stuck during otherwise safe gameplay. To rebind a shortcut, select it and press the new key. Escape cancels.

To delete a test home, first remove each junction in `games/<game>/` with `cmd /c rmdir <link>`. A recursive delete that follows junctions would delete the Steam install.

### Travel measurements

Hollow Knight 1.5.12620 and Silksong 1.0.30000, debug build, laptop with an RTX 3060 and 16 GB of memory, windowed:

| | Start to playable | Close | Working set | Private memory |
|---|---|---|---|---|
| Hollow Knight | 7.6 to 8.7 s | 0.4 to 0.5 s | 0.7 GB | 1.3 GB |
| Silksong | 10.4 to 12.2 s | 0.6 to 0.7 s | 1.0 GB | 2.1 GB |

A new Silksong game then waits for the player's first Bind, as in normal play. The travel screen owned every sampled point of the display from the first game closing to the second being ready. Save files, Steam folders and game settings were unchanged by the tests, apart from Unity's session counters and `Player.log` rotation.

## Decompiling

```powershell
pwsh tools/decompile.ps1 -Game hk                          # whole Assembly-CSharp project
pwsh tools/decompile.ps1 -Game silksong -Type HeroController
```

Output goes to `local/decompiled/<game>/`. Set `STEAM_COMMON` if the games are not in the default Steam library.

## Reading game assets

Silksong stores most assets in Addressables bundles under `Hollow Knight Silksong_Data\StreamingAssets\aa\StandaloneWindows64`. Their headers omit the Unity version, so set it before loading them with UnityPy:

```python
import UnityPy
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.0.50f1"
```

Hollow Knight's assets are in `hollow_knight_Data\resources.assets` and `sharedassets*.assets` and load without that setting. Extracted files stay in `local/`.

## Save safety

Both games keep saves under `%USERPROFILE%\AppData\LocalLow\Team Cherry\`. Other projects (Silksong Decrypted) test against the same folders.

- Run `tools/backup-saves.ps1` before any test that can write saves. It copies both folders into `local/saves/<timestamp>/` and never writes to the live folders.
- Never write, delete or load-and-save another player's or project's files: vanilla `userN.dat`, Silksong Decrypted's `userN.randomizersave`, `userN.randomizerdata` and `randomizer_userN_*.dat`.
- HKSS code writes only inside its own run folders ([saves](docs/ARCHITECTURE.md#5-saves)).
- Restore by hand with both games closed, copying back only the files you changed.

## Game folders

Never put a loader or any other file in the Steam installs. Test through the launcher, which runs the games from mirror folders ([launcher](docs/ARCHITECTURE.md#6-hkss-launcher)).

## Player hub

```powershell
python -m http.server 8766 --directory site
```

Open <http://localhost:8766/>. Pushing to `main` deploys `site/` through `.github/workflows/pages.yml`. The site is a game UI: reuse the existing components (title menu with fleur cursors, ornamental rules, panels, diamond checkboxes) and colour tokens in `site/style.css` rather than adding new visual styles.

Refresh the hub catalog when shipping a world update. Use the exact `.apworld` for that release. The exporter reads the packaged world with the installed Archipelago libraries and uses the Hollow Knight reference checkout for map area labels.

```powershell
python tools/export_hub_data.py --apworld dist/hkss.apworld
python tools/export_hub_data.py --apworld dist/hkss.apworld --check
```

Build the PopTracker pack from the same `.apworld` and attach `dist/hkss-poptracker.zip` to the release. The pack has item grids, an area overview map per game and Archipelago auto-tracking, but no logic yet.

```powershell
python tools/export_poptracker.py --apworld dist/hkss.apworld
```

Review and include `site/world.json` with the update. It records the world version and archive digest. In a fresh browser profile, check the setup paths, import and download a YAML, and filter the map for each game. The hub works under the project path on GitHub Pages.

The logo is `site/assets/logo.png` (1024×1024, transparent). `logo.webp` (512, used by the page), `logo-256.png` (README) and `favicon.png` (64×64) are resized from it.

## Conventions

- Branches: `feature/<task>`. Commit messages are plain, imperative and human-written; the hooks reject tool or vendor names and co-author trailers. Check PR and release text with `python tools/check_public_metadata.py --text "<text>"`.
- Release titles use `HKSS Decrypted <version>`, with the version taken from the tag without its leading `v`. Use the same title when creating or editing a release. Before publishing, run `python tools/check_public_metadata.py --release v0.1.1 "HKSS Decrypted 0.1.1"` with the actual tag and title. The Public metadata workflow checks releases on publication and edits.
- Ported files start with a comment naming the upstream repository, path, commit and license.
- Start with the smallest test that exercises a change and say what was verified.
- Update docs when commands, interfaces or behavior change; update `ROADMAP.md` when a roadmap item changes.
- Keep game files, decompiled code, extracted assets, saves and build output out of Git.

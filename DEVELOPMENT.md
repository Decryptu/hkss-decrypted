# Development

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first. This file covers the local setup, tools, and conventions.

## Layout

| Path | Contents |
|---|---|
| `docs/` | Architecture and design notes |
| `site/` | GitHub Pages player hub (static, no build step) |
| `tools/` | Publication checker, reference fetcher, decompiler wrapper, save backup |
| `local/` | Ignored: reference checkouts, decompiled code, extracted assets, save backups |
| `src/`, `apworld/`, `launcher/` | Planned, see the architecture's [layout](docs/ARCHITECTURE.md#2-monorepo-shared-core-one-plugin-per-game) |

## Requirements

- Windows 10/11 with both games installed through Steam.
- .NET 9 SDK (plugins and `HKSS.Core`), Python 3.12+ (world and tools), Node 22+ and Rust stable (launcher, when it starts).
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
- HKSS code writes only `hkss_*` files ([saves](docs/ARCHITECTURE.md#5-saves)).
- Restore by hand with both games closed, copying back only the files you changed.

## Game folders

Leave both Steam installs launchable as vanilla. When testing a plugin, use a profile directory with Doorstop disabled in the game folder and enabled per launch (as described in the [launcher](docs/ARCHITECTURE.md#6-hkss-launcher) design), or remove the loader files when you are done.

## Player hub

```powershell
python -m http.server 8766 --directory site
```

Open <http://localhost:8766/>. Pushing to `main` deploys `site/` through `.github/workflows/pages.yml`. The site is a game UI: reuse the existing components (title menu with fleur cursors, ornamental rules, panels, diamond checkboxes) and colour tokens in `site/style.css` rather than adding new visual styles.

The logo is `site/assets/logo.png` (1024×1024, transparent). `logo.webp` (512, used by the page), `logo-256.png` (README) and `favicon.png` (64×64) are resized from it.

## Conventions

- Branches: `feature/<task>`. Commit messages are plain, imperative and human-written; the hooks reject tool or vendor names and co-author trailers. Check PR and release text with `python tools/check_public_metadata.py --text "<text>"`.
- Ported files start with a comment naming the upstream repository, path, commit and license.
- Start with the smallest test that exercises a change and say what was verified.
- Update docs when commands, interfaces or behavior change; update `ROADMAP.md` when a roadmap item changes.
- Keep game files, decompiled code, extracted assets, saves and build output out of Git.

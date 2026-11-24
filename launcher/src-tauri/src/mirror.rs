//! A modded copy of each game that leaves the Steam install alone.
//!
//! The mirror holds copies of the few top-level files and directory junctions to the game's data,
//! plus BepInEx and the plugin. Nothing is ever written through the junctions or to the Steam folder.

use crate::game::Game;
use std::{
    fs, io,
    path::{Path, PathBuf},
};

pub fn prepare(game: Game, steam_dir: &Path, mirror: &Path, payload: &Path) -> io::Result<PathBuf> {
    fs::create_dir_all(mirror)?;
    for entry in fs::read_dir(steam_dir)? {
        let entry = entry?;
        let target = mirror.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            link(&entry.path(), &target)?;
        } else if is_game_file(&entry.file_name().to_string_lossy()) {
            copy_if_changed(&entry.path(), &target)?;
        }
    }

    // Lets the game start directly for a Steam user who owns it, without asking Steam to relaunch it.
    fs::write(mirror.join("steam_appid.txt"), game.app_id().to_string())?;

    let loader = payload.join("bepinex");
    for file in ["winhttp.dll", "doorstop_config.ini"] {
        copy_if_changed(&loader.join(file), &mirror.join(file))?;
    }
    copy_dir(&loader.join("BepInEx").join("core"), &mirror.join("BepInEx").join("core"))?;
    copy_dir(&payload.join(game.key()), &mirror.join("BepInEx").join("plugins").join("HKSS"))?;
    Ok(mirror.join(game.exe()))
}

/// Skips anything another tool may have left in the Steam folder, such as a mod loader.
fn is_game_file(name: &str) -> bool {
    let name = name.to_ascii_lowercase();
    name.ends_with(".exe") || name == "unityplayer.dll"
}

fn link(source: &Path, target: &Path) -> io::Result<()> {
    // A loader folder in the Steam install belongs to another tool.
    if source.file_name().is_some_and(|name| name.eq_ignore_ascii_case("BepInEx")) {
        return Ok(());
    }
    if target.symlink_metadata().is_ok() {
        if !junction::exists(target)? {
            return Err(io::Error::other(format!("{} is not a link", target.display())));
        }
        if junction::get_target(target)? == source {
            return Ok(());
        }
        // Removes the junction atomically. `remove_dir` refuses a real folder with contents.
        fs::remove_dir(target)?;
    }
    junction::create(source, target)
}

fn copy_if_changed(source: &Path, target: &Path) -> io::Result<()> {
    let from = source.metadata()?;
    if let Ok(to) = target.metadata() {
        if to.len() == from.len() && to.modified()? == from.modified()? {
            return Ok(());
        }
    }
    fs::copy(source, target).map(drop)
}

fn copy_dir(source: &Path, target: &Path) -> io::Result<()> {
    fs::create_dir_all(target)?;
    let mut keep = std::collections::HashSet::new();
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let name = entry.file_name();
        let file_type = entry.file_type()?;
        if file_type.is_file() {
            keep.insert(name.clone());
            copy_if_changed(&entry.path(), &target.join(&name))?;
        } else if file_type.is_dir() {
            keep.insert(name.clone());
            copy_dir(&entry.path(), &target.join(&name))?;
        }
    }
    // Prune stale files/folders left behind by older launcher versions (e.g. renamed/removed DLLs).
    for entry in fs::read_dir(target)? {
        let entry = entry?;
        if !keep.contains(&entry.file_name()) {
            let path = entry.path();
            if entry.file_type()?.is_dir() {
                fs::remove_dir_all(&path)?;
            } else {
                fs::remove_file(&path)?;
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mirrors_without_touching_the_install() {
        let temp = tempfile::tempdir().unwrap();
        let steam = temp.path().join("steam");
        let mirror = temp.path().join("mirror");
        let payload = temp.path().join("payload");
        let write = |path: PathBuf, text: &str| {
            fs::create_dir_all(path.parent().unwrap()).unwrap();
            fs::write(path, text).unwrap();
        };
        write(steam.join("hollow_knight.exe"), "exe");
        write(steam.join("winhttp.dll"), "someone else's loader");
        write(steam.join("BepInEx/core/other.dll"), "other");
        write(steam.join("hollow_knight_Data/level0"), "level");
        write(payload.join("bepinex/winhttp.dll"), "ours");
        write(payload.join("bepinex/doorstop_config.ini"), "");
        write(payload.join("bepinex/BepInEx/core/BepInEx.dll"), "");
        write(payload.join("hollow_knight/HKSS.HollowKnight.dll"), "v1");

        for _ in 0..2 {
            prepare(Game::HollowKnight, &steam, &mirror, &payload).unwrap();
        }
        let read = |path: &str| fs::read_to_string(mirror.join(path)).unwrap();
        assert_eq!(read("winhttp.dll"), "ours");
        assert_eq!(read("steam_appid.txt"), "367520");
        assert_eq!(read("hollow_knight_Data/level0"), "level");
        assert_eq!(read("BepInEx/plugins/HKSS/HKSS.HollowKnight.dll"), "v1");
        assert!(!mirror.join("BepInEx/core/other.dll").exists());

        // A moved Steam library replaces the link and keeps the old data.
        let moved = temp.path().join("moved");
        write(moved.join("hollow_knight.exe"), "exe");
        write(moved.join("hollow_knight_Data/level0"), "moved level");
        prepare(Game::HollowKnight, &moved, &mirror, &payload).unwrap();
        assert_eq!(read("hollow_knight_Data/level0"), "moved level");
        assert!(steam.join("hollow_knight_Data/level0").exists());

        // Upgrading the payload prunes stale/renamed DLLs left in the mirror.
        write(mirror.join("BepInEx/plugins/HKSS/OldMod.dll"), "stale");
        prepare(Game::HollowKnight, &moved, &mirror, &payload).unwrap();
        assert!(!mirror.join("BepInEx/plugins/HKSS/OldMod.dll").exists());
        assert_eq!(read("BepInEx/plugins/HKSS/HKSS.HollowKnight.dll"), "v1");
    }
}

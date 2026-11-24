//! Finds the installed games. Everything here only reads.

use crate::game::Game;
use std::{fs, path::PathBuf};
use winreg::{enums::HKEY_CURRENT_USER, RegKey};

pub struct Install {
    pub dir: PathBuf,
    pub build: String,
}

pub fn is_running() -> bool {
    RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey(r"Software\Valve\Steam\ActiveProcess")
        .and_then(|key| key.get_value::<u32, _>("pid"))
        .is_ok_and(|pid| pid != 0)
}

pub fn find(game: Game) -> Option<Install> {
    let steam: String = RegKey::predef(HKEY_CURRENT_USER)
        .open_subkey(r"Software\Valve\Steam")
        .and_then(|key| key.get_value("SteamPath"))
        .ok()?;
    let steamapps = PathBuf::from(steam).join("steamapps");
    let libraries = fs::read_to_string(steamapps.join("libraryfolders.vdf")).ok()?;

    values(&libraries, "path").into_iter().find_map(|library| {
        let apps = PathBuf::from(library).join("steamapps");
        let manifest = fs::read_to_string(apps.join(format!("appmanifest_{}.acf", game.app_id()))).ok()?;
        let dir = apps.join("common").join(values(&manifest, "installdir").into_iter().next()?);
        let build = values(&manifest, "buildid").into_iter().next()?;
        dir.join(game.exe()).is_file().then_some(Install { dir, build })
    })
}

/// Values of every `"key"  "value"` line in a Valve text file.
fn values(text: &str, key: &str) -> Vec<String> {
    text.lines()
        .filter_map(|line| {
            let mut parts = line.trim().splitn(5, '"');
            (parts.next()? == "" && parts.next()?.eq_ignore_ascii_case(key)).then_some(())?;
            parts.next()?;
            Some(parts.next()?.replace(r"\\", r"\"))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::values;

    #[test]
    fn reads_valve_text() {
        let text = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Games\"\n\t}\n}";
        assert_eq!(values(text, "path"), [r"C:\Program Files (x86)\Steam", r"D:\Games"]);
        assert_eq!(values(text, "label"), [""]);
        assert!(values(text, "missing").is_empty());
    }
}

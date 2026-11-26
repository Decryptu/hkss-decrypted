//! Launcher-wide settings, kept in `launcher.json` next to the runs.

use serde::{Deserialize, Serialize};
use std::{fs, io, path::Path};

const FILE: &str = "launcher.json";

#[derive(Clone, Default, Deserialize, Serialize)]
pub struct Settings {
    /// Keep the game the player leaves paused in memory, so travelling back to it is instant.
    #[serde(default)]
    pub instant_travel: bool,
}

/// Missing or unreadable files give the defaults: settings never block a run.
pub fn load(home: &Path) -> Settings {
    fs::read(home.join(FILE))
        .ok()
        .and_then(|bytes| serde_json::from_slice(&bytes).ok())
        .unwrap_or_default()
}

pub fn save(home: &Path, settings: &Settings) -> io::Result<()> {
    fs::create_dir_all(home)?;
    let temp = home.join("launcher.json.new");
    fs::write(&temp, serde_json::to_vec_pretty(settings)?)?;
    fs::rename(temp, home.join(FILE))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trips_and_defaults_to_off() {
        let temp = tempfile::tempdir().unwrap();
        assert!(!load(temp.path()).instant_travel);
        save(temp.path(), &Settings { instant_travel: true }).unwrap();
        assert!(load(temp.path()).instant_travel);
        fs::write(temp.path().join(FILE), "{").unwrap();
        assert!(!load(temp.path()).instant_travel);
    }
}

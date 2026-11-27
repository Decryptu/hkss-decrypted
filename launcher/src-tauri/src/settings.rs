//! Launcher-wide settings, kept in `launcher.json` next to the runs.

use serde::{Deserialize, Serialize};
use std::{fs, io, path::Path};

const FILE: &str = "launcher.json";

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Deserialize, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum FastEndingsMode {
    /// Auto-skip credits and completion screens; ending cinematics stay watchable with 1-press skip.
    #[default]
    Credits,
    /// Auto-skip ending cinematics, credits, and completion screens back to gameplay.
    All,
    /// Vanilla ending and credits playback.
    Off,
}

impl FastEndingsMode {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Credits => "credits",
            Self::All => "all",
            Self::Off => "off",
        }
    }
}

#[derive(Clone, Default, Deserialize, Serialize)]
pub struct Settings {
    /// Keep the game the player leaves paused in memory, so travelling back to it is instant.
    #[serde(default)]
    pub instant_travel: bool,
    /// How both games handle post-boss ending cinematics, credits, and completion screens.
    #[serde(default)]
    pub fast_endings: FastEndingsMode,
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
        let initial = load(temp.path());
        assert!(!initial.instant_travel);
        assert_eq!(initial.fast_endings, FastEndingsMode::Credits);
        save(
            temp.path(),
            &Settings { instant_travel: true, fast_endings: FastEndingsMode::All },
        )
        .unwrap();
        let loaded = load(temp.path());
        assert!(loaded.instant_travel);
        assert_eq!(loaded.fast_endings, FastEndingsMode::All);
        fs::write(temp.path().join(FILE), "{").unwrap();
        let fallback = load(temp.path());
        assert!(!fallback.instant_travel);
        assert_eq!(fallback.fast_endings, FastEndingsMode::Credits);
    }
}

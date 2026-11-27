//! Launcher-wide settings, kept in `launcher.json` next to the runs and synced two-way with both games.

use serde::{Deserialize, Serialize};
use std::{
    fs, io,
    path::{Path, PathBuf},
};

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

fn default_true() -> bool {
    true
}

#[derive(Clone, Debug, PartialEq, Eq, Deserialize, Serialize)]
pub struct Settings {
    /// Keep the game the player leaves paused in memory, so travelling back to it is instant.
    #[serde(default)]
    pub instant_travel: bool,
    /// How both games handle post-boss ending cinematics, credits, and completion screens.
    #[serde(default)]
    pub fast_endings: FastEndingsMode,
    /// Sell all copies of a relic or Rancid Egg at once (Lemm, Jinn, Scrounge & Cardinius).
    #[serde(default = "default_true")]
    pub bulk_relic_turn_in: bool,
    /// Automatically draw visited rooms onto owned maps.
    #[serde(default)]
    pub auto_quill: bool,
    /// Color check map pins by logic reachability.
    #[serde(default = "default_true")]
    pub show_map_logic: bool,
    /// Show the corner recent-items feed.
    #[serde(default)]
    pub show_recent_items: bool,
    /// Show damage numbers on enemy hits.
    #[serde(default)]
    pub damage_numbers: bool,
    /// Show health bars above damaged enemies.
    #[serde(default)]
    pub enemy_health_bars: bool,
    /// Show a boss health bar.
    #[serde(default)]
    pub boss_health_bar: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            instant_travel: false,
            fast_endings: FastEndingsMode::Credits,
            bulk_relic_turn_in: true,
            auto_quill: false,
            show_map_logic: true,
            show_recent_items: false,
            damage_numbers: false,
            enemy_health_bars: false,
            boss_health_bar: false,
        }
    }
}

pub fn file_path(home: &Path) -> PathBuf {
    home.join(FILE)
}

/// Missing or unreadable files give the defaults: settings never block a run.
pub fn load(home: &Path) -> Settings {
    fs::read(file_path(home))
        .ok()
        .and_then(|bytes| serde_json::from_slice(&bytes).ok())
        .unwrap_or_default()
}

pub fn save(home: &Path, settings: &Settings) -> io::Result<()> {
    fs::create_dir_all(home)?;
    let temp = home.join("launcher.json.new");
    fs::write(&temp, serde_json::to_vec_pretty(settings)?)?;
    fs::rename(temp, file_path(home))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trips_and_defaults_sensibly() {
        let temp = tempfile::tempdir().unwrap();
        let initial = load(temp.path());
        assert_eq!(initial, Settings::default());
        assert!(!initial.instant_travel);
        assert_eq!(initial.fast_endings, FastEndingsMode::Credits);
        assert!(initial.bulk_relic_turn_in);
        assert!(initial.show_map_logic);
        assert!(!initial.auto_quill);

        let updated = Settings {
            instant_travel: true,
            fast_endings: FastEndingsMode::All,
            bulk_relic_turn_in: false,
            auto_quill: true,
            show_map_logic: false,
            show_recent_items: true,
            damage_numbers: true,
            enemy_health_bars: true,
            boss_health_bar: true,
        };
        save(temp.path(), &updated).unwrap();
        assert_eq!(load(temp.path()), updated);

        // Partial legacy file keeps defaults for new fields.
        fs::write(file_path(temp.path()), r#"{"instant_travel":true}"#).unwrap();
        let partial = load(temp.path());
        assert!(partial.instant_travel);
        assert_eq!(partial.fast_endings, FastEndingsMode::Credits);
        assert!(partial.bulk_relic_turn_in);
        assert!(partial.show_map_logic);

        fs::write(file_path(temp.path()), "{").unwrap();
        assert_eq!(load(temp.path()), Settings::default());
    }
}

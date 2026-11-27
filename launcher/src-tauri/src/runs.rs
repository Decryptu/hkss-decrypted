//! Combo runs: one folder each, holding one save per game.

use crate::game::Game;
use serde::{Deserialize, Serialize};
use std::{
    env, fs, io,
    path::{Path, PathBuf},
};

pub const SLOTS: [u8; 4] = [1, 2, 3, 4];

#[derive(Serialize, Deserialize)]
pub struct Run {
    /// The game the player is in, and where the run resumes.
    pub current: Game,
}

#[derive(Default, Deserialize)]
pub struct RunGoals {
    #[serde(default)]
    pub hk_goal_completed: bool,
    #[serde(default)]
    pub ss_goal_completed: bool,
    #[serde(default)]
    pub double_steel_soul: bool,
    #[serde(default)]
    pub steel_soul_defeated: bool,
    #[serde(default)]
    pub steel_soul_defeated_game: String,
    #[serde(default)]
    pub steel_soul_defeated_scene: String,
    #[serde(default, deserialize_with = "null_default")]
    pub hk_stats: GameStats,
    #[serde(default, deserialize_with = "null_default")]
    pub ss_stats: GameStats,
    #[serde(default)]
    pub travels: u32,
    #[serde(default, deserialize_with = "null_default")]
    pub epithet: String,
    #[serde(default, deserialize_with = "null_default")]
    pub feats: Vec<Feat>,
    #[serde(default, deserialize_with = "null_default")]
    pub deadliest_area: String,
    #[serde(default, deserialize_with = "null_default")]
    pub favourite_area: String,
}

/// A light-hearted award computed by the game plugins.
#[derive(Clone, Default, Deserialize, Serialize)]
pub struct Feat {
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub description: String,
}

/// One game's end-of-run stats, written to `state.json` by the game plugins.
#[derive(Clone, Default, Deserialize, Serialize)]
pub struct GameStats {
    #[serde(default)]
    pub play_seconds: f64,
    #[serde(default)]
    pub deaths: u32,
    #[serde(default)]
    pub kills: u32,
    #[serde(default)]
    pub completion_percent: f64,
    #[serde(default)]
    pub checks_found: u32,
    #[serde(default)]
    pub checks_total: u32,
    #[serde(default)]
    pub hits_taken: u32,
    #[serde(default)]
    pub masks_lost: u32,
    #[serde(default)]
    pub hits_landed: u32,
    #[serde(default)]
    pub currency_earned: u64,
    #[serde(default)]
    pub jumps: u32,
    #[serde(default)]
    pub dashes: u32,
    #[serde(default)]
    pub distance: f64,
    #[serde(default)]
    pub rooms_entered: u32,
    /// Distinct rooms entered; `state.json` lists their names, only the count is kept.
    #[serde(default, deserialize_with = "count_list")]
    pub rooms_visited: u32,
    #[serde(default)]
    pub longest_life_seconds: f64,
    #[serde(default)]
    pub last_death_seconds: f64,
    #[serde(default)]
    pub goal_at_seconds: f64,
}

fn null_default<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Default + Deserialize<'de>,
{
    Ok(Option::<T>::deserialize(deserializer)?.unwrap_or_default())
}

fn count_list<'de, D>(deserializer: D) -> Result<u32, D::Error>
where
    D: serde::Deserializer<'de>,
{
    let list = Option::<Vec<serde::de::IgnoredAny>>::deserialize(deserializer)?;
    Ok(list.map_or(0, |l| l.len() as u32))
}

/// Best-effort read of per-game goal flags and stats from `state.json`. Missing or unreadable files return defaults.
pub fn load_goals(dir: &Path) -> RunGoals {
    fs::read(dir.join("state.json"))
        .ok()
        .and_then(|bytes| serde_json::from_slice::<RunGoals>(&bytes).ok())
        .unwrap_or_default()
}

/// Persists the `double_steel_soul` flag in `state.json`, preserving all other fields.
/// Refuses to disable the flag once the run has already been defeated in Steel Soul.
pub fn set_double_steel_soul(dir: &Path, enabled: bool) -> io::Result<()> {
    fs::create_dir_all(dir)?;
    let path = dir.join("state.json");
    let mut map = fs::read(&path)
        .ok()
        .and_then(|bytes| serde_json::from_slice::<serde_json::Value>(&bytes).ok())
        .and_then(|val| val.as_object().cloned())
        .unwrap_or_default();
    if !enabled
        && map
            .get("steel_soul_defeated")
            .and_then(|v| v.as_bool())
            .unwrap_or(false)
    {
        return Err(io::Error::other("Cannot disable Double Steel Soul on a defeated run; reset the run instead."));
    }
    map.insert("double_steel_soul".into(), serde_json::Value::Bool(enabled));
    let temp = dir.join("state.json.new");
    fs::write(&temp, serde_json::to_vec_pretty(&serde_json::Value::Object(map))?)?;
    for attempt in 0..5 {
        match fs::rename(&temp, &path) {
            Ok(()) => return Ok(()),
            Err(e)
                if (e.kind() == io::ErrorKind::PermissionDenied || e.raw_os_error() == Some(32))
                    && attempt < 4 =>
            {
                std::thread::sleep(std::time::Duration::from_millis(20));
            }
            Err(e) => {
                let _ = fs::remove_file(&temp);
                return Err(e);
            }
        }
    }
    Ok(())
}

pub fn home() -> PathBuf {
    env::var_os("HKSS_HOME").map(PathBuf::from).unwrap_or_else(|| {
        PathBuf::from(env::var_os("LOCALAPPDATA").expect("LOCALAPPDATA is set on Windows")).join("HKSS")
    })
}

pub fn dir(slot: u8) -> io::Result<PathBuf> {
    if !SLOTS.contains(&slot) {
        return Err(io::Error::other(format!("There is no run {slot}")));
    }
    Ok(home().join("runs").join(format!("slot-{slot}")))
}

/// `None` means an empty slot. A run that exists but cannot be read is an error, never a new run.
pub fn load(dir: &Path) -> io::Result<Option<Run>> {
    match fs::read(dir.join("run.json")) {
        Ok(bytes) => Ok(Some(serde_json::from_slice(&bytes)?)),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e),
    }
}

pub fn save(dir: &Path, run: &Run) -> io::Result<()> {
    fs::create_dir_all(dir.join("logs"))?;
    let temp = dir.join("run.json.new");
    fs::write(&temp, serde_json::to_vec_pretty(run)?)?;
    fs::rename(temp, dir.join("run.json"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keeps_where_the_player_is() {
        let temp = tempfile::tempdir().unwrap();
        assert!(load(temp.path()).unwrap().is_none());
        for game in Game::ALL {
            save(temp.path(), &Run { current: game }).unwrap();
            assert_eq!(load(temp.path()).unwrap().unwrap().current, game);
        }
    }

    #[test]
    fn a_damaged_run_is_not_replaced() {
        let temp = tempfile::tempdir().unwrap();
        fs::write(temp.path().join("run.json"), "{").unwrap();
        assert!(load(temp.path()).is_err());
    }

    #[test]
    fn only_known_slots_have_folders() {
        assert!(dir(0).is_err());
        assert!(dir(5).is_err());
        assert!(dir(1).unwrap().ends_with("slot-1"));
    }

    #[test]
    fn loads_goal_flags_from_state_json() {
        let temp = tempfile::tempdir().unwrap();
        let empty = load_goals(temp.path());
        assert!(!empty.hk_goal_completed && !empty.ss_goal_completed);

        fs::write(
            temp.path().join("state.json"),
            r#"{"hk_goal_completed": true, "ss_goal_completed": false}"#,
        )
        .unwrap();
        let partial = load_goals(temp.path());
        assert!(partial.hk_goal_completed);
        assert!(!partial.ss_goal_completed);

        fs::write(temp.path().join("state.json"), "{").unwrap();
        fs::write(
            temp.path().join("state.json"),
            r#"{"hk_stats": {"play_seconds": 61.5, "deaths": 3, "checks_found": 2, "checks_total": 9, "rooms_visited": ["Town", "Tutorial_01"], "deaths_by_area": {"TOWN": 3}}, "ss_stats": null, "travels": 4, "epithet": "Knight and Hornet", "feats": [{"title": "Twin Crowns", "description": "x"}], "deadliest_area": null}"#,
        )
        .unwrap();
        let stats = load_goals(temp.path());
        assert_eq!(stats.hk_stats.deaths, 3);
        assert_eq!(stats.hk_stats.checks_total, 9);
        assert_eq!(stats.ss_stats.play_seconds, 0.0);
        assert_eq!(stats.hk_stats.rooms_visited, 2);
        assert_eq!(stats.travels, 4);
        assert_eq!(stats.epithet, "Knight and Hornet");
        assert_eq!(stats.feats[0].title, "Twin Crowns");
        assert_eq!(stats.deadliest_area, "");

        fs::write(temp.path().join("state.json"), "{").unwrap();
        let corrupt = load_goals(temp.path());
        assert!(!corrupt.hk_goal_completed && !corrupt.ss_goal_completed);
    }

    #[test]
    fn double_steel_soul_flag_and_defeat_round_trip() {
        let temp = tempfile::tempdir().unwrap();
        set_double_steel_soul(temp.path(), true).unwrap();
        let enabled = load_goals(temp.path());
        assert!(enabled.double_steel_soul);
        assert!(!enabled.steel_soul_defeated);

        fs::write(
            temp.path().join("state.json"),
            r#"{"double_steel_soul": true, "steel_soul_defeated": true, "steel_soul_defeated_game": "silksong", "steel_soul_defeated_scene": "Bone_01"}"#,
        )
        .unwrap();
        let defeated = load_goals(temp.path());
        assert!(defeated.double_steel_soul);
        assert!(defeated.steel_soul_defeated);
        assert_eq!(defeated.steel_soul_defeated_game, "silksong");
        assert_eq!(defeated.steel_soul_defeated_scene, "Bone_01");
        assert!(set_double_steel_soul(temp.path(), false).is_err());
        assert!(load_goals(temp.path()).double_steel_soul);
    }
}

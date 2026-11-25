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
}

/// Best-effort read of per-game goal flags from `state.json`. Missing or unreadable files return false.
pub fn load_goals(dir: &Path) -> RunGoals {
    fs::read(dir.join("state.json"))
        .ok()
        .and_then(|bytes| serde_json::from_slice::<RunGoals>(&bytes).ok())
        .unwrap_or_default()
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
        let corrupt = load_goals(temp.path());
        assert!(!corrupt.hk_goal_completed && !corrupt.ss_goal_completed);
    }
}

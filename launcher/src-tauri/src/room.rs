//! Archipelago room settings for a run, stored in `ap.json` next to the saves.
//!
//! Matches `ApSettings` in HKSS.Core: the plugins read the same file when a game starts.

use serde::{Deserialize, Serialize};
use std::{fs, io, path::Path};

pub const FILE: &str = "ap.json";

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(default)]
pub struct Room {
    pub host: String,
    pub port: u16,
    pub slot: String,
    pub password: String,
    pub auto_connect: bool,
}

impl Default for Room {
    fn default() -> Self {
        Self { host: "localhost".into(), port: 38281, slot: String::new(), password: String::new(), auto_connect: true }
    }
}

impl Room {
    /// Trims what the player typed and rejects what the plugins could not use.
    pub fn validated(mut self) -> Result<Self, String> {
        self.host = self.host.trim().to_string();
        self.slot = self.slot.trim().to_string();
        if self.host.is_empty() {
            return Err("Enter the room's host.".into());
        }
        if self.port == 0 {
            return Err("Enter the room's port.".into());
        }
        if self.slot.is_empty() {
            return Err("Enter your slot name.".into());
        }
        Ok(self)
    }

    /// The same settings with the password hidden, for bug reports.
    pub fn redacted(&self) -> Self {
        let password = if self.password.is_empty() { String::new() } else { "(hidden)".into() };
        Self { password, ..self.clone() }
    }
}

/// `None` when the run has no room yet.
pub fn load(dir: &Path) -> io::Result<Option<Room>> {
    match fs::read(dir.join(FILE)) {
        Ok(bytes) => Ok(Some(serde_json::from_slice(&bytes)?)),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e),
    }
}

pub fn save(dir: &Path, room: &Room) -> io::Result<()> {
    fs::create_dir_all(dir)?;
    let temp = dir.join("ap.json.new");
    fs::write(&temp, serde_json::to_vec_pretty(room)?)?;
    fs::rename(temp, dir.join(FILE))
}

pub fn clear(dir: &Path) -> io::Result<()> {
    match fs::remove_file(dir.join(FILE)) {
        Err(e) if e.kind() != io::ErrorKind::NotFound => Err(e),
        _ => Ok(()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trips_and_reads_the_plugin_format() {
        let temp = tempfile::tempdir().unwrap();
        assert!(load(temp.path()).unwrap().is_none());
        let room = Room { host: "archipelago.gg".into(), port: 51234, slot: "Knight".into(), ..Room::default() };
        save(temp.path(), &room).unwrap();
        assert_eq!(load(temp.path()).unwrap().unwrap(), room);

        fs::write(temp.path().join(FILE), r#"{"host":"h","port":1,"slot":"s","extra":true}"#).unwrap();
        let read = load(temp.path()).unwrap().unwrap();
        assert!(read.auto_connect && read.port == 1 && read.slot == "s");
        clear(temp.path()).unwrap();
        clear(temp.path()).unwrap();
        assert!(load(temp.path()).unwrap().is_none());
    }

    #[test]
    fn validates_and_redacts() {
        let room = Room { host: "  h ".into(), slot: " s".into(), password: "secret".into(), ..Room::default() };
        let room = room.validated().unwrap();
        assert_eq!((room.host.as_str(), room.slot.as_str()), ("h", "s"));
        assert_eq!(room.redacted().password, "(hidden)");
        assert!(Room::default().validated().is_err());
    }
}

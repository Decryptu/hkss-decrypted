//! Run housekeeping: deleting a run, removing the modded game folders, and bug report archives.
//!
//! Removal never follows a link. The game folders hold junctions into the Steam installs, so a
//! link is removed as a link and whatever it points to is left alone.

use crate::{game::Game, preflight, room, steam};
use std::{
    fmt::Write as _,
    fs, io,
    path::{Path, PathBuf},
    time::{SystemTime, UNIX_EPOCH},
};

/// Removes a folder and its contents without following junctions or symbolic links.
pub fn remove_tree(path: &Path) -> io::Result<()> {
    let meta = match path.symlink_metadata() {
        Ok(meta) => meta,
        Err(e) if e.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e),
    };
    if is_link(path, &meta) {
        return remove_link(path, &meta);
    }
    if !meta.is_dir() {
        return fs::remove_file(path);
    }
    for entry in fs::read_dir(path)? {
        remove_tree(&entry?.path())?;
    }
    fs::remove_dir(path)
}

fn is_link(path: &Path, meta: &fs::Metadata) -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
        if meta.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
            return true;
        }
    }
    meta.file_type().is_symlink() || (meta.is_dir() && junction::exists(path).unwrap_or(false))
}

fn remove_link(path: &Path, meta: &fs::Metadata) -> io::Result<()> {
    // Both calls act on the link itself; `remove_dir` refuses a real folder with contents.
    if meta.is_dir() || junction::exists(path).unwrap_or(false) {
        fs::remove_dir(path)
    } else {
        fs::remove_file(path)
    }
}

/// Deletes one run: both saves, its room, and its logs.
pub fn delete_run(dir: &Path) -> io::Result<()> {
    remove_tree(dir)
}

/// Removes the modded copies of both games. The next run builds them again.
pub fn remove_game_folders(home: &Path) -> io::Result<()> {
    remove_tree(&home.join("games"))
}

/// Writes a ZIP of everything useful for a bug report about one run. The room password is hidden.
pub fn diagnostics(home: &Path, dir: &Path, slot: u8) -> io::Result<PathBuf> {
    let mut zip = Zip::default();
    for name in ["run.json", "state.json"] {
        zip.add_file(name, &dir.join(name));
    }
    if let Ok(Some(room)) = room::load(dir) {
        zip.add(room::FILE, serde_json::to_vec_pretty(&room.redacted())?);
    }
    if let Ok(entries) = fs::read_dir(dir.join("logs")) {
        for entry in entries.flatten() {
            let name = entry.file_name().to_string_lossy().into_owned();
            zip.add_file(&format!("logs/{name}"), &entry.path());
        }
    }
    if let Ok(entries) = fs::read_dir(dir.join("signals")) {
        for entry in entries.flatten() {
            let name = entry.file_name().to_string_lossy().into_owned();
            zip.add_file(&format!("signals/{name}"), &entry.path());
        }
    }
    for game in Game::ALL {
        let mirror = home.join("games").join(game.key());
        let bepinex = mirror.join("BepInEx");
        zip.add_file(&format!("bepinex/{}.log", game.key()), &bepinex.join("LogOutput.log"));
        if let Ok(entries) = fs::read_dir(&mirror) {
            for entry in entries.flatten() {
                let name = entry.file_name().to_string_lossy().into_owned();
                let lower = name.to_ascii_lowercase();
                if lower.starts_with("preloader_") && lower.ends_with(".log") {
                    zip.add_file(&format!("bepinex/{}-{name}", game.key()), &entry.path());
                }
            }
        }
    }
    zip.add("about.txt", build_about(home, slot).into_bytes());

    let out = home.join("diagnostics");
    fs::create_dir_all(&out)?;
    let now = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or_default();
    let path = out.join(format!("hkss-run{slot}-{now}.zip"));
    fs::write(&path, zip.finish())?;
    Ok(path)
}

fn build_about(home: &Path, slot: u8) -> String {
    let mut out = format!("HKSS Launcher {}\nrun {slot}\nsteam_running: {}\n", env!("CARGO_PKG_VERSION"), steam::is_running());
    for game in Game::ALL {
        let mirror = home.join("games").join(game.key());
        let has_winhttp = mirror.join("winhttp.dll").is_file();
        let has_bepinex_log = mirror.join("BepInEx").join("LogOutput.log").is_file();
        match steam::find(game) {
            Some(install) => {
                let report = preflight::inspect(game, &install);
                let _ = writeln!(
                    out,
                    "{}: build={} beta={} unity={} winhttp={} bepinex_log={}",
                    game.key(),
                    install.build,
                    install.beta.as_deref().unwrap_or("none"),
                    report.unity_version.as_deref().unwrap_or("unknown"),
                    has_winhttp,
                    has_bepinex_log,
                );
                for issue in &report.issues {
                    let _ = writeln!(out, "  [{:?}] {}: {}", issue.severity, issue.code, issue.summary);
                }
            }
            None => {
                let _ = writeln!(out, "{}: not found in Steam (winhttp={has_winhttp} bepinex_log={has_bepinex_log})", game.key());
            }
        }
        let plugin_dir = mirror.join("BepInEx").join("plugins").join("HKSS");
        if let Ok(entries) = fs::read_dir(&plugin_dir) {
            let mut files: Vec<(String, u64)> = entries
                .flatten()
                .filter_map(|e| {
                    let meta = e.metadata().ok()?;
                    meta.is_file()
                        .then(|| (e.file_name().to_string_lossy().into_owned(), meta.len()))
                })
                .collect();
            files.sort_by(|a, b| a.0.cmp(&b.0));
            let listing: Vec<String> = files.into_iter().map(|(n, s)| format!("{n} ({s} B)")).collect();
            let _ = writeln!(out, "  plugins: {}", listing.join(", "));
        }
    }
    out
}

/// A minimal stored (uncompressed) ZIP writer: logs are small and any tool can open the result.
#[derive(Default)]
struct Zip {
    data: Vec<u8>,
    central: Vec<u8>,
    count: u16,
}

impl Zip {
    /// Skips files that do not exist or are still locked by a running game.
    fn add_file(&mut self, name: &str, path: &Path) {
        if let Ok(bytes) = fs::read(path) {
            self.add(name, bytes);
        }
    }

    fn add(&mut self, name: &str, bytes: Vec<u8>) {
        let crc = crc32(&bytes);
        let size = bytes.len() as u32;
        let offset = self.data.len() as u32;
        let name = name.as_bytes();
        // Local file header: version 2.0, UTF-8 names, stored, DOS date 1980-01-01.
        let common = |out: &mut Vec<u8>| {
            out.extend_from_slice(&20u16.to_le_bytes());
            out.extend_from_slice(&0x0800u16.to_le_bytes());
            out.extend_from_slice(&0u16.to_le_bytes());
            out.extend_from_slice(&0u16.to_le_bytes());
            out.extend_from_slice(&0x21u16.to_le_bytes());
            out.extend_from_slice(&crc.to_le_bytes());
            out.extend_from_slice(&size.to_le_bytes());
            out.extend_from_slice(&size.to_le_bytes());
            out.extend_from_slice(&(name.len() as u16).to_le_bytes());
            out.extend_from_slice(&0u16.to_le_bytes());
        };
        self.data.extend_from_slice(&0x04034b50u32.to_le_bytes());
        common(&mut self.data);
        self.data.extend_from_slice(name);
        self.data.extend_from_slice(&bytes);

        self.central.extend_from_slice(&0x02014b50u32.to_le_bytes());
        self.central.extend_from_slice(&20u16.to_le_bytes());
        common(&mut self.central);
        self.central.extend_from_slice(&[0; 6]); // comment length (2), disk (2), internal attributes (2)
        self.central.extend_from_slice(&0u32.to_le_bytes()); // external attributes
        self.central.extend_from_slice(&offset.to_le_bytes());
        self.central.extend_from_slice(name);
        self.count += 1;
    }

    fn finish(mut self) -> Vec<u8> {
        let start = self.data.len() as u32;
        let size = self.central.len() as u32;
        self.data.append(&mut self.central);
        self.data.extend_from_slice(&0x06054b50u32.to_le_bytes());
        self.data.extend_from_slice(&[0; 4]);
        self.data.extend_from_slice(&self.count.to_le_bytes());
        self.data.extend_from_slice(&self.count.to_le_bytes());
        self.data.extend_from_slice(&size.to_le_bytes());
        self.data.extend_from_slice(&start.to_le_bytes());
        self.data.extend_from_slice(&0u16.to_le_bytes());
        self.data
    }
}

fn crc32(bytes: &[u8]) -> u32 {
    let mut crc = !0u32;
    for &byte in bytes {
        crc ^= byte as u32;
        for _ in 0..8 {
            crc = if crc & 1 != 0 { (crc >> 1) ^ 0xEDB8_8320 } else { crc >> 1 };
        }
    }
    !crc
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::room::Room;

    #[test]
    fn crc_matches_the_standard() {
        assert_eq!(crc32(b"123456789"), 0xCBF4_3926);
    }

    #[test]
    fn removing_game_folders_leaves_the_install_alone() {
        let temp = tempfile::tempdir().unwrap();
        let steam = temp.path().join("steam/hollow_knight_Data");
        fs::create_dir_all(&steam).unwrap();
        fs::write(steam.join("level0"), "level").unwrap();
        let mirror = temp.path().join("home/games/hollow_knight");
        fs::create_dir_all(mirror.join("BepInEx")).unwrap();
        fs::write(mirror.join("hollow_knight.exe"), "exe").unwrap();
        junction::create(&steam, mirror.join("hollow_knight_Data")).unwrap();

        remove_game_folders(&temp.path().join("home")).unwrap();
        assert!(!temp.path().join("home/games").exists());
        assert_eq!(fs::read_to_string(steam.join("level0")).unwrap(), "level");
    }

    #[test]
    fn diagnostics_hide_the_password() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path().join("runs/slot-2");
        fs::create_dir_all(dir.join("logs")).unwrap();
        fs::write(dir.join("run.json"), r#"{"current":"silksong"}"#).unwrap();
        fs::write(dir.join("logs/launcher.log"), "1 silksong start").unwrap();
        room::save(&dir, &Room { slot: "Knight".into(), password: "hunter2".into(), ..Room::default() }).unwrap();

        let zip = fs::read(diagnostics(temp.path(), &dir, 2).unwrap()).unwrap();
        let text = String::from_utf8_lossy(&zip);
        assert!(zip.starts_with(b"PK\x03\x04"));
        assert!(text.contains("logs/launcher.log") && text.contains("silksong start"));
        assert!(text.contains("(hidden)") && !text.contains("hunter2"));
        assert_eq!(u16::from_le_bytes([zip[zip.len() - 12], zip[zip.len() - 11]]), 4);
        assert_valid_zip(&zip, 4);
    }

    fn assert_valid_zip(zip: &[u8], expected_entries: u16) {
        let eocd = zip.len() - 22;
        assert_eq!(&zip[eocd..eocd + 4], b"PK\x05\x06");
        let count = u16::from_le_bytes([zip[eocd + 10], zip[eocd + 11]]);
        assert_eq!(count, expected_entries);
        let cd_size = u32::from_le_bytes([zip[eocd + 12], zip[eocd + 13], zip[eocd + 14], zip[eocd + 15]]) as usize;
        let cd_offset = u32::from_le_bytes([zip[eocd + 16], zip[eocd + 17], zip[eocd + 18], zip[eocd + 19]]) as usize;
        assert_eq!(cd_offset + cd_size, eocd);

        let mut pos = cd_offset;
        for _ in 0..count {
            assert_eq!(&zip[pos..pos + 4], b"PK\x01\x02", "bad central directory magic at {pos}");
            let name_len = u16::from_le_bytes([zip[pos + 28], zip[pos + 29]]) as usize;
            let extra_len = u16::from_le_bytes([zip[pos + 30], zip[pos + 31]]) as usize;
            let comment_len = u16::from_le_bytes([zip[pos + 32], zip[pos + 33]]) as usize;
            let local_offset = u32::from_le_bytes([zip[pos + 42], zip[pos + 43], zip[pos + 44], zip[pos + 45]]) as usize;
            assert_eq!(&zip[local_offset..local_offset + 4], b"PK\x03\x04", "bad local header magic at {local_offset}");
            pos += 46 + name_len + extra_len + comment_len;
        }
        assert_eq!(pos, eocd);
    }
}

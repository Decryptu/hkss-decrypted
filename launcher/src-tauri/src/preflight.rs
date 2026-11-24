//! Pre-launch checks for Steam game installs: Unity version, conflicting mods, missing files.

use crate::{game::Game, steam::Install};
use serde::Serialize;
use std::{
    fs::{self, File},
    io::Read,
    path::Path,
};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum Severity {
    Error,
    Warning,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
pub struct Issue {
    pub severity: Severity,
    pub code: &'static str,
    pub summary: String,
    pub detail: String,
}

#[derive(Clone, Debug, Default, Serialize)]
pub struct Report {
    pub unity_version: Option<String>,
    pub issues: Vec<Issue>,
}

impl Report {
    pub fn first_error(&self) -> Option<&Issue> {
        self.issues.iter().find(|i| i.severity == Severity::Error)
    }
}

/// Files or folders in `<data_dir>/Managed` left by HK Modding API, Lumafly, Scarab, or other
/// loaders that conflict with BepInEx 5 or patch `Assembly-CSharp.dll`.
const CONFLICTING_MANAGED_ENTRIES: &[&str] = &[
    "Mods",
    "Modding.dll",
    "0Harmony.dll",
    "Mono.Cecil.dll",
    "Mono.Cecil.Mdb.dll",
    "Mono.Cecil.Pdb.dll",
    "Mono.Cecil.Rocks.dll",
    "MonoMod.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "MMHOOK_Assembly-CSharp.dll",
    "Assembly-CSharp.dll.v",
    "Assembly-CSharp.dll.bak",
    "Assembly-CSharp.dll.m",
    "mscorlib.dll.bak",
];

pub fn inspect(game: Game, install: &Install) -> Report {
    let mut report = Report::default();
    let data = install.dir.join(game.data_dir());
    let managed = data.join("Managed");

    // 1. Unity engine version from globalgamemanagers (catches legacy HK 1.5.78 / Unity 2020).
    let unity = read_unity_version(&data.join("globalgamemanagers"));
    let mut wrong_version = false;
    if let Some(ver) = &unity {
        if !ver.starts_with(game.expected_unity_major()) {
            wrong_version = true;
            let (summary, detail) = match &install.beta {
                Some(beta) => (
                    format!("wrong version (Unity {ver}, beta \"{beta}\")"),
                    format!(
                        "{name} is on an older version (Unity {ver}, Steam beta \"{beta}\"). HKSS requires the current Unity 6 build. In Steam, right-click {name} -> Properties -> Betas -> set Beta Participation to None and let it update.",
                        name = game.name()
                    ),
                ),
                None => (
                    format!("wrong version (Unity {ver})"),
                    format!(
                        "{name} is on an older version (Unity {ver}). HKSS requires the current Unity 6 build. In Steam, right-click {name} -> Properties -> Betas -> set Beta Participation to None and update the game.",
                        name = game.name()
                    ),
                ),
            };
            report.issues.push(Issue {
                severity: Severity::Error,
                code: "wrong_version",
                summary,
                detail,
            });
        }
    }
    report.unity_version = unity;

    // 2. Conflicting Lumafly / Scarab / Modding API files in <data_dir>/Managed.
    let conflicts = find_conflicting_managed(&managed);
    if !conflicts.is_empty() {
        let preview = format_preview(&conflicts, 4);
        report.issues.push(Issue {
            severity: Severity::Error,
            code: "conflicting_mods",
            summary: "conflicting mods in Steam folder".into(),
            detail: format!(
                "{name} has leftover Lumafly / Modding API files in {data}\\Managed ({preview}). Steam's Verify does not remove extra files, and they stop HKSS from loading. Uninstall {name} in Steam, delete any remaining files in its Steam folder, and reinstall it cleanly.",
                name = game.name(),
                data = game.data_dir(),
            ),
        });
    }

    // 3. Required core files in the Steam install.
    let mut missing = Vec::new();
    if !install.dir.join("UnityPlayer.dll").is_file() {
        missing.push("UnityPlayer.dll");
    }
    if !install.dir.join("MonoBleedingEdge").is_dir() {
        missing.push("MonoBleedingEdge/");
    }
    if !managed.join("Assembly-CSharp.dll").is_file() {
        missing.push("Managed/Assembly-CSharp.dll");
    }
    if !missing.is_empty() {
        report.issues.push(Issue {
            severity: Severity::Error,
            code: "incomplete_install",
            summary: "incomplete game files".into(),
            detail: format!(
                "{name} is missing required files ({files}). In Steam, right-click {name} -> Properties -> Installed Files -> Verify integrity of game files.",
                name = game.name(),
                files = missing.join(", "),
            ),
        });
    }

    // 4. Steam beta opted in (when not already flagged as wrong Unity version).
    if !wrong_version {
        if let Some(beta) = &install.beta {
            report.issues.push(Issue {
                severity: Severity::Warning,
                code: "steam_beta",
                summary: format!("Steam beta \"{beta}\""),
                detail: format!(
                    "{name} is opted into Steam beta \"{beta}\". If the game fails to start, switch Properties -> Betas to None in Steam.",
                    name = game.name(),
                ),
            });
        }
    }

    // 5. Untested Steam build (still Unity 6, so warn rather than block).
    if !wrong_version && install.build != game.tested_build() {
        report.issues.push(Issue {
            severity: Severity::Warning,
            code: "untested_build",
            summary: format!("untested build {}", install.build),
            detail: format!(
                "{name} is on Steam build {} (tested on {}). If it fails to start, update {name} in Steam or check for a newer HKSS Launcher.",
                install.build,
                game.tested_build(),
                name = game.name(),
            ),
        });
    }

    report
}

/// Reads the Unity engine version string from a `globalgamemanagers` serialized file header.
pub fn read_unity_version(path: &Path) -> Option<String> {
    let mut file = File::open(path).ok()?;
    let mut buf = [0u8; 256];
    let n = file.read(&mut buf).ok()?;
    parse_unity_version(&buf[..n])
}

fn parse_unity_version(header: &[u8]) -> Option<String> {
    if header.len() < 24 {
        return None;
    }
    let format = u32::from_be_bytes([header[8], header[9], header[10], header[11]]);
    let offset = if format >= 22 { 48 } else { 20 };
    if offset >= header.len() {
        return None;
    }
    let slice = &header[offset..];
    let end = slice.iter().position(|&b| b == 0).unwrap_or(slice.len());
    let text = std::str::from_utf8(&slice[..end]).ok()?;
    looks_like_unity_version(text).then(|| text.to_string())
}

fn looks_like_unity_version(text: &str) -> bool {
    let mut parts = text.split('.');
    let Some(major) = parts.next() else { return false };
    let Some(minor) = parts.next() else { return false };
    let Some(rest) = parts.next() else { return false };
    (major.len() == 4 || major.len() == 1)
        && major.bytes().all(|b| b.is_ascii_digit())
        && !minor.is_empty()
        && minor.bytes().all(|b| b.is_ascii_digit())
        && rest.bytes().next().is_some_and(|b| b.is_ascii_digit())
}

fn find_conflicting_managed(managed: &Path) -> Vec<String> {
    let Ok(entries) = fs::read_dir(managed) else { return Vec::new() };
    let mut found = Vec::new();
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().into_owned();
        if CONFLICTING_MANAGED_ENTRIES.iter().any(|c| c.eq_ignore_ascii_case(&name)) {
            let suffix = if entry.file_type().is_ok_and(|t| t.is_dir()) { "/" } else { "" };
            found.push(format!("{name}{suffix}"));
        }
    }
    found.sort_by_key(|s| s.to_ascii_lowercase());
    found
}

fn format_preview(items: &[String], max: usize) -> String {
    if items.len() <= max {
        items.join(", ")
    } else {
        format!("{}, +{} more", items[..max].join(", "), items.len() - max)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn write_ggm(path: &Path, format: u32, version: &str) {
        let mut buf = vec![0u8; 128];
        buf[8..12].copy_from_slice(&format.to_be_bytes());
        let offset = if format >= 22 { 48 } else { 20 };
        buf[offset..offset + version.len()].copy_from_slice(version.as_bytes());
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, buf).unwrap();
    }

    fn make_clean_hk(dir: &Path) -> Install {
        fs::create_dir_all(dir.join("hollow_knight_Data/Managed")).unwrap();
        fs::create_dir_all(dir.join("MonoBleedingEdge")).unwrap();
        fs::write(dir.join("hollow_knight.exe"), "exe").unwrap();
        fs::write(dir.join("UnityPlayer.dll"), "dll").unwrap();
        fs::write(dir.join("hollow_knight_Data/Managed/Assembly-CSharp.dll"), "dll").unwrap();
        write_ggm(&dir.join("hollow_knight_Data/globalgamemanagers"), 22, "6000.0.61f1");
        Install { dir: dir.to_path_buf(), build: Game::HollowKnight.tested_build().into(), beta: None }
    }

    #[test]
    fn clean_install_has_no_issues() {
        let temp = tempfile::tempdir().unwrap();
        let install = make_clean_hk(temp.path());
        let report = inspect(Game::HollowKnight, &install);
        assert_eq!(report.unity_version.as_deref(), Some("6000.0.61f1"));
        assert!(report.issues.is_empty());
    }

    #[test]
    fn detects_legacy_unity_2020_version() {
        let temp = tempfile::tempdir().unwrap();
        let mut install = make_clean_hk(temp.path());
        install.beta = Some("public-beta".into());
        install.build = "7608469".into();
        write_ggm(&temp.path().join("hollow_knight_Data/globalgamemanagers"), 22, "2020.2.2f1");

        let report = inspect(Game::HollowKnight, &install);
        assert_eq!(report.unity_version.as_deref(), Some("2020.2.2f1"));
        let err = report.first_error().expect("expected wrong_version error");
        assert_eq!(err.code, "wrong_version");
        assert!(err.detail.contains("2020.2.2f1"));
        assert!(err.detail.contains("public-beta"));
    }

    #[test]
    fn detects_leftover_lumafly_managed_files() {
        let temp = tempfile::tempdir().unwrap();
        let install = make_clean_hk(temp.path());
        let managed = temp.path().join("hollow_knight_Data/Managed");
        fs::create_dir_all(managed.join("Mods")).unwrap();
        fs::write(managed.join("0Harmony.dll"), "old").unwrap();
        fs::write(managed.join("Mono.Cecil.dll"), "old").unwrap();

        let report = inspect(Game::HollowKnight, &install);
        let err = report.first_error().expect("expected conflicting_mods error");
        assert_eq!(err.code, "conflicting_mods");
        assert!(err.detail.contains("0Harmony.dll"));
        assert!(err.detail.contains("Mods/"));
    }

    #[test]
    fn warns_on_untested_unity_6_build() {
        let temp = tempfile::tempdir().unwrap();
        let mut install = make_clean_hk(temp.path());
        install.build = "99999999".into();

        let report = inspect(Game::HollowKnight, &install);
        assert!(report.first_error().is_none());
        assert_eq!(report.issues.len(), 1);
        assert_eq!(report.issues[0].severity, Severity::Warning);
        assert_eq!(report.issues[0].code, "untested_build");
    }

    #[test]
    fn parses_silksong_branch_unity_version() {
        let mut buf = vec![0u8; 128];
        buf[8..12].copy_from_slice(&22u32.to_be_bytes());
        let ver = b"6000.0.50f1-uum-100966-branch1";
        buf[48..48 + ver.len()].copy_from_slice(ver);
        assert_eq!(parse_unity_version(&buf).as_deref(), Some("6000.0.50f1-uum-100966-branch1"));
    }
}

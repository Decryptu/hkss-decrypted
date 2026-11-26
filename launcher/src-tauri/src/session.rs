//! Plays a run: starts the current game behind the travel screen, and swaps games on request.

use crate::{game::Game, mirror, preflight, process, runs, steam};
use serde::Deserialize;
use std::{
    fs::{self, File, OpenOptions},
    io::{self, Write},
    path::{Path, PathBuf},
    process::{Child, Command},
    thread::sleep,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

const POLL: Duration = Duration::from_millis(30);
/// The game's own boot timeout is 120 s; this only catches a game that stopped answering.
const READY_TIMEOUT: Duration = Duration::from_secs(150);
/// A game whose plugin never reports in is running unmodified, on the player's normal saves.
const PLUGIN_TIMEOUT: Duration = Duration::from_secs(20);
/// The save is already confirmed on disk when a game asks to travel.
const CLOSE_TIMEOUT: Duration = Duration::from_secs(8);
const FADE: Duration = Duration::from_millis(500);

/// The launcher windows, so the loop can be read without Tauri.
pub trait Screen {
    fn raise_cover(&self, to: Game);
    fn lower_cover(&self);
    fn hide_cover(&self);
}

#[derive(Debug, Deserialize, PartialEq)]
struct Signal {
    session: String,
    state: String,
    to: Option<Game>,
    detail: Option<String>,
}

enum Outcome {
    Closed,
    Travel(Game),
}

pub fn play(slot: u8, payload: &Path, screen: &dyn Screen) -> Result<(), String> {
    let result = play_run(slot, payload, screen);
    screen.hide_cover();
    result.map_err(|e| e.to_string())
}

fn play_run(slot: u8, payload: &Path, screen: &dyn Screen) -> io::Result<()> {
    let dir = runs::dir(slot)?;
    let existing = runs::load(&dir)?;
    let mut needs_initial_save = existing.is_none();
    let mut run = existing.unwrap_or(runs::Run { current: Game::HollowKnight });
    let mut log = Log::open(&dir)?;
    let mut place = None;
    loop {
        match play_game(run.current, &dir, payload, screen, &mut place, &mut log, &mut needs_initial_save, &run)? {
            Outcome::Closed => return Ok(()),
            Outcome::Travel(to) => {
                run.current = to;
                runs::save(&dir, &run)?;
            }
        }
    }
}

fn play_game(
    game: Game,
    dir: &Path,
    payload: &Path,
    screen: &dyn Screen,
    place: &mut Option<(i32, i32)>,
    log: &mut Log,
    needs_initial_save: &mut bool,
    run: &runs::Run,
) -> io::Result<Outcome> {
    let install = steam::find(game).ok_or_else(|| io::Error::other(format!("{} is not installed in Steam.", game.name())))?;
    if process::is_running(game.exe()) {
        let msg = format!("{} is already running. Close it before starting a run.", game.name());
        log.write(format_args!("{} blocked: already running", game.key()));
        return Err(io::Error::other(msg));
    }
    let report = preflight::inspect(game, &install);
    if let Some(err) = report.first_error() {
        log.write(format_args!("{} blocked: {} ({})", game.key(), err.code, err.summary));
        return Err(io::Error::other(err.detail.clone()));
    }

    screen.raise_cover(game);
    let mirror_dir = runs::home().join("games").join(game.key());
    let exe = mirror::prepare(game, &install.dir, &mirror_dir, payload)?;
    clear_preloader_logs(&mirror_dir);

    let session = new_session();
    let started = Instant::now();
    let mut child = Command::new(&exe)
        .current_dir(exe.parent().expect("the mirror folder"))
        .env_remove("DOORSTOP_DISABLE")
        .env_remove("DOORSTOP_ENABLED")
        .env("HKSS_RUN_DIR", dir)
        .env("HKSS_SESSION", &session)
        .arg("-logFile")
        .arg(dir.join("logs").join(format!("{}-player.log", game.key())))
        .spawn()?;
    if *needs_initial_save {
        runs::save(dir, run)?;
        *needs_initial_save = false;
    }
    log.write(format_args!(
        "{} start (build {}, unity {})",
        game.key(),
        install.build,
        report.unity_version.as_deref().unwrap_or("unknown")
    ));

    let signals = dir.join("signals");
    let signal_file = signals.join(format!("{}.json", game.key()));
    let mut visible = false;
    let mut signal = None;
    loop {
        // A read can collide with the plugin replacing the file, so keep the last good signal.
        signal = read_signal(&signal_file, &session).or(signal);
        if let Some(signal) = &signal {
            match signal.state.as_str() {
                "travel" => {
                    let to = signal.to.ok_or_else(|| io::Error::other("Travel without a destination"))?;
                    log.memory(game, child.id(), "travel");
                    *place = process::position(child.id());
                    return leave(child, game, to, &session, &signals, screen, log);
                }
                "ready" | "error" if !visible => {
                    visible = true;
                    log.write(format_args!(
                        "{} {} after {:.2} s {}",
                        game.key(),
                        signal.state,
                        started.elapsed().as_secs_f32(),
                        signal.detail.as_deref().unwrap_or("")
                    ));
                    log.memory(game, child.id(), "ready");
                    if let Some(place) = *place {
                        process::move_to(child.id(), place);
                    }
                    process::focus(child.id());
                    screen.lower_cover();
                    sleep(FADE);
                    screen.hide_cover();
                }
                _ => {}
            }
        }
        if let Some(status) = child.try_wait()? {
            log.write(format_args!("{} closed ({status})", game.key()));
            if visible && status.success() && signal.as_ref().is_none_or(|s| s.state != "error") {
                return Ok(Outcome::Closed);
            }
            let reason = signal.filter(|s| s.state == "error").and_then(|s| s.detail).unwrap_or_else(|| {
                if visible { format!("closed unexpectedly ({status})") }
                else { format!("closed while starting ({status})") }
            });
            return Err(io::Error::other(format!("{}: {reason}", game.name())));
        }
        if signal.is_none() && started.elapsed() > PLUGIN_TIMEOUT {
            let _ = child.kill();
            let _ = child.wait();
            log.write(format_args!("{} plugin timeout after {:.1} s", game.key(), started.elapsed().as_secs_f32()));
            let reason = if !mirror_dir.join("winhttp.dll").is_file() {
                "winhttp.dll is missing from the modded game folder (antivirus may have quarantined it). Allow HKSS Launcher in your antivirus and try again."
            } else if has_preloader_log(&mirror_dir) {
                "BepInEx failed during startup. Export a bug report so the preloader log can be checked."
            } else {
                "the mod did not load within 20 seconds (game closed automatically). Make sure the game is up to date in Steam with no Lumafly or other mods installed, and check that antivirus is not blocking winhttp.dll, or export a bug report."
            };
            return Err(io::Error::other(format!("{}: {reason}", game.name())));
        }
        if !visible && started.elapsed() > READY_TIMEOUT {
            // Nothing to lose yet: the player has not seen the game.
            let _ = child.kill();
            let _ = child.wait();
            log.write(format_args!("{} ready timeout after {:.1} s", game.key(), started.elapsed().as_secs_f32()));
            return Err(io::Error::other(format!("{} did not finish starting within 150 seconds.", game.name())));
        }
        sleep(POLL);
    }
}

fn leave(
    mut child: Child,
    game: Game,
    to: Game,
    session: &str,
    signals: &Path,
    screen: &dyn Screen,
    log: &mut Log,
) -> io::Result<Outcome> {
    let asked = Instant::now();
    screen.raise_cover(to);
    sleep(Duration::from_millis(60));
    let cover_payload = format!(r#"{{"session":"{session}","state":"cover"}}"#);
    if let Err(err) = write_cover_with_retry(&signals.join("cover.json"), cover_payload.as_bytes()) {
        let _ = child.kill();
        let _ = child.wait();
        return Err(err);
    }
    while child.try_wait()?.is_none() {
        if asked.elapsed() > CLOSE_TIMEOUT {
            log.write(format_args!("{} had to be stopped", game.key()));
            let _ = child.kill();
            let _ = child.wait();
            break;
        }
        sleep(POLL);
    }
    log.write(format_args!("{} left for {} in {:.2} s", game.key(), to.key(), asked.elapsed().as_secs_f32()));
    Ok(Outcome::Travel(to))
}

fn write_cover_with_retry(target: &Path, bytes: &[u8]) -> io::Result<()> {
    let temp = target.with_extension("json.new");
    fs::write(&temp, bytes)?;
    let deadline = Instant::now() + Duration::from_secs(2);
    loop {
        match fs::rename(&temp, target) {
            Ok(()) => return Ok(()),
            Err(e) if is_sharing_violation(&e) && Instant::now() < deadline => {
                sleep(Duration::from_millis(25));
            }
            Err(e) => {
                let _ = fs::remove_file(&temp);
                return Err(e);
            }
        }
    }
}

fn is_sharing_violation(e: &io::Error) -> bool {
    e.kind() == io::ErrorKind::PermissionDenied || e.raw_os_error() == Some(32)
}

fn clear_preloader_logs(mirror_dir: &Path) {
    if let Ok(entries) = fs::read_dir(mirror_dir) {
        for entry in entries.flatten() {
            let name = entry.file_name().to_string_lossy().to_ascii_lowercase();
            if name.starts_with("preloader_") && name.ends_with(".log") {
                let _ = fs::remove_file(entry.path());
            }
        }
    }
}

fn has_preloader_log(mirror_dir: &Path) -> bool {
    fs::read_dir(mirror_dir).ok().is_some_and(|entries| {
        entries.flatten().any(|e| {
            let name = e.file_name().to_string_lossy().to_ascii_lowercase();
            name.starts_with("preloader_") && name.ends_with(".log")
        })
    })
}

fn read_signal(file: &Path, session: &str) -> Option<Signal> {
    parse_signal(&fs::read(file).ok()?, session)
}

fn parse_signal(bytes: &[u8], session: &str) -> Option<Signal> {
    serde_json::from_slice::<Signal>(bytes).ok().filter(|signal| signal.session == session)
}

fn new_session() -> String {
    let now = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or_default();
    format!("{now:x}-{}", std::process::id())
}

struct Log {
    file: File,
    path: PathBuf,
}

impl Log {
    fn open(dir: &Path) -> io::Result<Self> {
        let path = dir.join("logs").join("launcher.log");
        fs::create_dir_all(dir.join("logs"))?;
        Ok(Self { file: OpenOptions::new().create(true).append(true).open(&path)?, path })
    }

    fn write(&mut self, line: std::fmt::Arguments) {
        let now = SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or_default();
        if let Err(e) = writeln!(self.file, "{now} {line}") {
            eprintln!("{}: {e}", self.path.display());
        }
    }

    fn memory(&mut self, game: Game, pid: u32, when: &str) {
        if let Some((now, peak, private)) = process::memory_mb(pid) {
            self.write(format_args!("{} memory at {when}: {now} MB, peak {peak} MB, private {private} MB", game.key()));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_what_the_plugins_write() {
        let signal = parse_signal(br#"{"session":"s1","state":"travel","to":"silksong"}"#, "s1").unwrap();
        assert_eq!(signal.state, "travel");
        assert_eq!(signal.to, Some(Game::Silksong));
    }

    #[test]
    fn ignores_earlier_launches_and_half_written_files() {
        assert_eq!(parse_signal(br#"{"session":"old","state":"ready"}"#, "s1"), None);
        assert_eq!(parse_signal(br#"{"session":"s1","#, "s1"), None);
    }

    #[test]
    fn sessions_differ() {
        assert_ne!(new_session(), {
            sleep(Duration::from_millis(1));
            new_session()
        });
    }

    struct RecordingScreen {
        raised: std::sync::Mutex<Vec<Game>>,
    }

    impl Screen for RecordingScreen {
        fn raise_cover(&self, to: Game) {
            self.raised.lock().unwrap().push(to);
        }
        fn lower_cover(&self) {}
        fn hide_cover(&self) {}
    }

    #[test]
    fn leave_completes_travel_even_if_outgoing_game_crashes() {
        let temp = tempfile::tempdir().unwrap();
        let signals = temp.path().join("signals");
        fs::create_dir_all(&signals).unwrap();
        let mut log = Log::open(temp.path()).unwrap();
        let screen = RecordingScreen { raised: std::sync::Mutex::new(Vec::new()) };

        // Simulate an outgoing game that exits with a non-zero crash code right after sending travel.
        let child = Command::new("cmd").args(["/C", "exit", "1"]).spawn().unwrap();
        let outcome = leave(child, Game::HollowKnight, Game::Silksong, "s-crash", &signals, &screen, &mut log).unwrap();

        assert!(matches!(outcome, Outcome::Travel(Game::Silksong)));
        assert_eq!(*screen.raised.lock().unwrap(), vec![Game::Silksong]);
        let cover = fs::read_to_string(signals.join("cover.json")).unwrap();
        assert!(cover.contains("\"s-crash\""));
    }
}

//! Plays a run: starts the current game behind the travel screen, and swaps games on request.

use crate::{game::Game, mirror, preflight, process, runs, settings, steam};
use serde::{Deserialize, Serialize};
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
/// A paused game that does not answer in this time is closed and started again from its save.
const RESUME_TIMEOUT: Duration = Duration::from_secs(20);
/// Free memory needed to keep a game paused while the other one starts (about 2 GB for the
/// newcomer, plus room for the rest of the PC).
const PARK_MIN_FREE_MB: u64 = 3072;

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
    Travel(Game, Option<Parked>),
}

/// A game kept paused in memory after it saved and asked to travel (instant travel). The run
/// ends it if the player never goes back: its save is already on disk.
struct Parked {
    game: Game,
    child: Option<Child>,
    session: String,
    window: Option<isize>,
    record: PathBuf,
}

impl Drop for Parked {
    fn drop(&mut self) {
        if let Some(mut child) = self.child.take() {
            let _ = child.kill();
            let _ = child.wait();
        }
        let _ = fs::remove_file(&self.record);
    }
}

#[derive(Serialize, Deserialize)]
struct ParkedRecord {
    pid: u32,
}

fn parked_record(game: Game) -> PathBuf {
    runs::home().join(format!("parked-{}.json", game.key()))
}

/// Ends games an earlier launcher left paused (it crashed or was killed), so they never linger
/// unseen. Each one had saved before it was paused.
pub fn end_left_behind() {
    let games = runs::home().join("games");
    for game in Game::ALL {
        let record = parked_record(game);
        if let Some(parked) = fs::read(&record).ok().and_then(|b| serde_json::from_slice::<ParkedRecord>(&b).ok()) {
            process::kill_left_behind(parked.pid, &games);
        }
        let _ = fs::remove_file(record);
    }
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
    let loaded_settings = settings::load(&runs::home());
    let instant = loaded_settings.instant_travel;
    let fast_endings = loaded_settings.fast_endings;
    end_left_behind();
    // Dropping this when the run ends, however it ends, closes any game still paused.
    let mut parked: Vec<Parked> = Vec::new();
    loop {
        let resume = parked.iter().position(|p| p.game == run.current).map(|i| parked.remove(i));
        // Waking a paused game needs no new memory, so parking this one is always affordable then.
        let other_paused = !parked.is_empty();
        let travel = TravelMode { instant, fast_endings, resume, other_paused };
        match play_game(run.current, &dir, payload, screen, &mut place, &mut log, &mut needs_initial_save, &run, travel)? {
            Outcome::Closed => return Ok(()),
            Outcome::Travel(to, left) => {
                parked.extend(left);
                run.current = to;
                runs::save(&dir, &run)?;
            }
        }
    }
}

struct TravelMode {
    /// Keep the game the player leaves paused instead of closing it.
    instant: bool,
    /// Ending/credits skip preference forwarded to both games.
    fast_endings: settings::FastEndingsMode,
    /// This game, paused when the player left it.
    resume: Option<Parked>,
    /// The other game is paused, so travelling wakes it instead of starting it.
    other_paused: bool,
}

#[allow(clippy::too_many_arguments)]
fn play_game(
    game: Game,
    dir: &Path,
    payload: &Path,
    screen: &dyn Screen,
    place: &mut Option<(i32, i32)>,
    log: &mut Log,
    needs_initial_save: &mut bool,
    run: &runs::Run,
    travel: TravelMode,
) -> io::Result<Outcome> {
    let signals = dir.join("signals");
    let signal_file = signals.join(format!("{}.json", game.key()));
    let mirror_dir = runs::home().join("games").join(game.key());
    let resumed = travel.resume.and_then(|parked| {
        screen.raise_cover(game);
        resume(parked, &signals, &signal_file, log)
    });
    let (mut child, session, started) = match resumed {
        Some(resumed) => resumed,
        None => start(game, dir, payload, screen, &mirror_dir, log, needs_initial_save, run, travel.fast_endings)?,
    };

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
                    let park = travel.instant && (travel.other_paused || memory_allows_parking(log));
                    return leave(child, game, to, &session, &signals, screen, log, park);
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

/// Starts a game from its save behind the travel screen.
#[allow(clippy::too_many_arguments)]
fn start(
    game: Game,
    dir: &Path,
    payload: &Path,
    screen: &dyn Screen,
    mirror_dir: &Path,
    log: &mut Log,
    needs_initial_save: &mut bool,
    run: &runs::Run,
    fast_endings: settings::FastEndingsMode,
) -> io::Result<(Child, String, Instant)> {
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
    let exe = mirror::prepare(game, &install.dir, mirror_dir, payload)?;
    clear_preloader_logs(mirror_dir);

    let session = new_session();
    let started = Instant::now();
    let child = Command::new(&exe)
        .current_dir(exe.parent().expect("the mirror folder"))
        .env_remove("DOORSTOP_DISABLE")
        .env_remove("DOORSTOP_ENABLED")
        .env("HKSS_RUN_DIR", dir)
        .env("HKSS_SESSION", &session)
        .env("HKSS_FAST_ENDINGS", fast_endings.as_str())
        .env("HKSS_SETTINGS_FILE", settings::file_path(&runs::home()))
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

    Ok((child, session, started))
}

/// Wakes a paused game behind the travel screen. `None` when it does not answer: it is then
/// closed, and the caller starts it again from the save it wrote before it was paused.
fn resume(mut parked: Parked, signals: &Path, signal_file: &Path, log: &mut Log) -> Option<(Child, String, Instant)> {
    let game = parked.game;
    let mut child = parked.child.take()?;
    let session = parked.session.clone();
    let window = parked.window;
    drop(parked);
    let started = Instant::now();
    let asked = format!(r#"{{"session":"{session}","state":"resume"}}"#);
    if write_cover_with_retry(&signals.join("cover.json"), asked.as_bytes()).is_ok() && process::resume(&child) {
        if let Some(window) = window {
            process::show_window(window);
        }
        while started.elapsed() < RESUME_TIMEOUT {
            if !matches!(child.try_wait(), Ok(None)) {
                break;
            }
            if read_signal(signal_file, &session).is_some_and(|s| s.state == "ready") {
                log.write(format_args!("{} resumed in {:.2} s", game.key(), started.elapsed().as_secs_f32()));
                return Some((child, session, started));
            }
            sleep(POLL);
        }
    }
    log.write(format_args!("{} did not resume, starting it again", game.key()));
    let _ = child.kill();
    let _ = child.wait();
    None
}

fn memory_allows_parking(log: &mut Log) -> bool {
    match process::system_memory_mb() {
        Some((_, free)) if free >= PARK_MIN_FREE_MB => true,
        Some((_, free)) => {
            log.write(format_args!("instant travel skipped: {free} MB free, {PARK_MIN_FREE_MB} MB needed"));
            false
        }
        None => false,
    }
}

#[allow(clippy::too_many_arguments)]
fn leave(
    mut child: Child,
    game: Game,
    to: Game,
    session: &str,
    signals: &Path,
    screen: &dyn Screen,
    log: &mut Log,
    park: bool,
) -> io::Result<Outcome> {
    let asked = Instant::now();
    screen.raise_cover(to);
    sleep(Duration::from_millis(60));
    let cover_payload = if park {
        format!(r#"{{"session":"{session}","state":"cover","park":true}}"#)
    } else {
        format!(r#"{{"session":"{session}","state":"cover"}}"#)
    };
    let signal_file = signals.join(format!("{}.json", game.key()));
    if let Err(err) = write_cover_with_retry(&signals.join("cover.json"), cover_payload.as_bytes()) {
        let _ = child.kill();
        let _ = child.wait();
        return Err(err);
    }
    while child.try_wait()?.is_none() {
        // A plugin without instant travel just closes, which is handled like any other close.
        if park && read_signal(&signal_file, session).is_some_and(|s| s.state == "parked") {
            match park_game(child, game, session) {
                Ok(parked) => {
                    log.write(format_args!("{} paused for {} in {:.2} s", game.key(), to.key(), asked.elapsed().as_secs_f32()));
                    return Ok(Outcome::Travel(to, Some(parked)));
                }
                Err(mut child) => {
                    // It saved and is waiting to be woken, so closing it loses nothing.
                    log.write(format_args!("{} could not be paused, closing it", game.key()));
                    let _ = child.kill();
                    let _ = child.wait();
                    return Ok(Outcome::Travel(to, None));
                }
            }
        }
        if asked.elapsed() > CLOSE_TIMEOUT {
            log.write(format_args!("{} had to be stopped", game.key()));
            let _ = child.kill();
            let _ = child.wait();
            break;
        }
        sleep(POLL);
    }
    log.write(format_args!("{} left for {} in {:.2} s", game.key(), to.key(), asked.elapsed().as_secs_f32()));
    Ok(Outcome::Travel(to, None))
}

/// Hides a game that saved and is waiting, then stops its threads. Gives the child back if it
/// could not be paused.
fn park_game(child: Child, game: Game, session: &str) -> Result<Parked, Child> {
    let window = process::window_id(child.id());
    if let Some(window) = window {
        process::hide_window(window);
    }
    if !process::suspend(&child) {
        if let Some(window) = window {
            process::show_window(window);
        }
        return Err(child);
    }
    let record = parked_record(game);
    let _ = fs::write(&record, serde_json::to_vec(&ParkedRecord { pid: child.id() }).unwrap_or_default());
    Ok(Parked { game, child: Some(child), session: session.to_owned(), window, record })
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
        let outcome = leave(child, Game::HollowKnight, Game::Silksong, "s-crash", &signals, &screen, &mut log, true).unwrap();

        assert!(matches!(outcome, Outcome::Travel(Game::Silksong, None)));
        assert_eq!(*screen.raised.lock().unwrap(), vec![Game::Silksong]);
        let cover = fs::read_to_string(signals.join("cover.json")).unwrap();
        assert!(cover.contains("\"s-crash\""));
    }

    #[test]
    fn a_parked_game_is_paused_then_woken_where_it_was() {
        let temp = tempfile::tempdir().unwrap();
        std::env::set_var("HKSS_HOME", temp.path());
        let signals = temp.path().join("signals");
        fs::create_dir_all(&signals).unwrap();
        let mut log = Log::open(temp.path()).unwrap();
        let screen = RecordingScreen { raised: std::sync::Mutex::new(Vec::new()) };
        let game_file = signals.join("hollow_knight.json");

        // The plugin answers a cover that asks to park by saying it is parked.
        fs::write(&game_file, r#"{"session":"s-park","state":"parked"}"#).unwrap();
        let child = Command::new("ping").args(["-n", "60", "127.0.0.1"]).stdout(std::process::Stdio::null()).spawn().unwrap();
        let outcome = leave(child, Game::HollowKnight, Game::Silksong, "s-park", &signals, &screen, &mut log, true).unwrap();
        let Outcome::Travel(Game::Silksong, Some(parked)) = outcome else { panic!("the game was not parked") };
        assert!(fs::read_to_string(signals.join("cover.json")).unwrap().contains(r#""park":true"#));
        assert!(parked_record(Game::HollowKnight).is_file());

        // Waking it asks the plugin to resume; it answers ready and keeps its session.
        fs::write(&game_file, r#"{"session":"s-park","state":"ready"}"#).unwrap();
        let (mut child, session, _) = resume(parked, &signals, &game_file, &mut log).unwrap();
        assert_eq!(session, "s-park");
        assert!(child.try_wait().unwrap().is_none());
        assert!(fs::read_to_string(signals.join("cover.json")).unwrap().contains(r#""state":"resume""#));
        assert!(!parked_record(Game::HollowKnight).exists());
        child.kill().unwrap();
        child.wait().unwrap();
    }

    #[test]
    fn a_parked_game_closes_when_the_run_ends() {
        let child = Command::new("ping").args(["-n", "60", "127.0.0.1"]).stdout(std::process::Stdio::null()).spawn().unwrap();
        let pid = child.id();
        let temp = tempfile::tempdir().unwrap();
        let record = temp.path().join("parked.json");
        fs::write(&record, "{}").unwrap();
        drop(Parked { game: Game::Silksong, child: Some(child), session: "s".into(), window: None, record: record.clone() });
        assert!(!record.exists());
        let listed = Command::new("tasklist").args(["/FI", &format!("PID eq {pid}"), "/NH"]).output().unwrap();
        assert!(!String::from_utf8_lossy(&listed.stdout).contains(&pid.to_string()));
    }
}

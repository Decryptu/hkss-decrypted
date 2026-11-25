#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod game;
mod manage;
mod mirror;
mod preflight;
mod process;
mod room;
mod room_check;
mod runs;
mod session;
mod steam;

use game::Game;
use serde::Serialize;
use std::{
    env,
    path::PathBuf,
    sync::atomic::{AtomicBool, Ordering},
    thread,
};
use tauri::{AppHandle, Emitter, Manager, WebviewWindow};

static PLAYING: AtomicBool = AtomicBool::new(false);

#[derive(Serialize)]
struct GameStatus {
    key: &'static str,
    name: &'static str,
    build: Option<String>,
    tested: bool,
    unity_version: Option<String>,
    issues: Vec<preflight::Issue>,
}

#[derive(Serialize)]
struct SlotStatus {
    id: u8,
    current: Option<Game>,
    room: Option<room::Room>,
    hk_goal_completed: bool,
    ss_goal_completed: bool,
    error: Option<String>,
}

#[derive(Serialize)]
struct Status {
    games: Vec<GameStatus>,
    slots: Vec<SlotStatus>,
    steam_running: bool,
    version: &'static str,
}

#[tauri::command]
fn status() -> Result<Status, String> {
    let playing = PLAYING.load(Ordering::SeqCst);
    let games = Game::ALL.map(|game| match steam::find(game) {
        Some(install) => {
            let tested = install.build == game.tested_build();
            let mut report = preflight::inspect(game, &install);
            if !playing && process::is_running(game.exe()) {
                report.issues.insert(
                    0,
                    preflight::Issue {
                        severity: preflight::Severity::Error,
                        code: "already_running",
                        summary: "already running".into(),
                        detail: format!("{} is already running. Close it before starting a run.", game.name()),
                    },
                );
            }
            GameStatus {
                key: game.key(),
                name: game.name(),
                build: Some(install.build),
                tested,
                unity_version: report.unity_version,
                issues: report.issues,
            }
        }
        None => GameStatus {
            key: game.key(),
            name: game.name(),
            build: None,
            tested: false,
            unity_version: None,
            issues: vec![preflight::Issue {
                severity: preflight::Severity::Error,
                code: "not_installed",
                summary: "not found in Steam".into(),
                detail: format!(
                    "{} was not found in Steam. Install it in Steam, then focus or reopen this window.",
                    game.name()
                ),
            }],
        },
    });
    let mut slots = Vec::new();
    for id in runs::SLOTS {
        let mut errors = Vec::new();
        let dir = runs::dir(id);
        let current = match dir.as_ref().map(|d| runs::load(d)) {
            Ok(Ok(run)) => run.map(|run| run.current),
            Ok(Err(e)) => {
                errors.push(format!("run.json: {e}"));
                None
            }
            Err(e) => {
                errors.push(e.to_string());
                None
            }
        };
        let room = match dir.as_ref().map(|d| room::load(d)) {
            Ok(Ok(room)) => room,
            Ok(Err(e)) => {
                errors.push(format!("ap.json: {e}"));
                None
            }
            Err(e) => {
                if errors.is_empty() {
                    errors.push(e.to_string());
                }
                None
            }
        };
        let goals = dir.as_ref().map(|d| runs::load_goals(d)).unwrap_or_default();
        let error = if errors.is_empty() { None } else { Some(errors.join("; ")) };
        slots.push(SlotStatus {
            id,
            current,
            room,
            hk_goal_completed: goals.hk_goal_completed,
            ss_goal_completed: goals.ss_goal_completed,
            error,
        });
    }
    Ok(Status { games: games.into(), slots, steam_running: steam::is_running(), version: env!("CARGO_PKG_VERSION") })
}

#[tauri::command]
fn play(app: AppHandle, slot: u8) -> Result<(), String> {
    if !steam::is_running() {
        return Err("Start Steam first.".into());
    }
    if PLAYING.swap(true, Ordering::SeqCst) {
        return Err("A run is already open.".into());
    }
    thread::spawn(move || {
        let main = app.get_webview_window("main");
        let result = payload(&app).and_then(|payload| {
            let screen = Windows { cover: app.get_webview_window("cover") };
            if let Some(main) = &main {
                let _ = main.hide();
            }
            session::play(slot, &payload, &screen)
        });
        PLAYING.store(false, Ordering::SeqCst);
        if let Err(e) = &result {
            eprintln!("{e}");
        }
        if let Some(main) = &main {
            let _ = main.show();
            let _ = main.set_focus();
        }
        let _ = app.emit("session-ended", result.err());
        if env::var_os("HKSS_DEV_PLAY").is_some() {
            app.exit(0);
        }
    });
    Ok(())
}

/// Changes to a run's files wait until no game is open.
fn idle() -> Result<(), String> {
    if PLAYING.load(Ordering::SeqCst) {
        return Err("Close the game first.".into());
    }
    Ok(())
}

#[tauri::command]
async fn test_connection(room: room::Room) -> Result<room_check::CheckResult, String> {
    idle()?;
    room_check::check(room).await
}

#[tauri::command]
fn save_room(slot: u8, room: room::Room) -> Result<(), String> {
    idle()?;
    let room = room.validated()?;
    let dir = runs::dir(slot).map_err(|e| e.to_string())?;
    room::save(&dir, &room).map_err(|e| format!("The room could not be saved: {e}"))
}

#[tauri::command]
fn clear_room(slot: u8) -> Result<(), String> {
    idle()?;
    let dir = runs::dir(slot).map_err(|e| e.to_string())?;
    room::clear(&dir).map_err(|e| format!("The room could not be removed: {e}"))
}

#[tauri::command]
fn delete_run(slot: u8) -> Result<(), String> {
    idle()?;
    let dir = runs::dir(slot).map_err(|e| e.to_string())?;
    manage::delete_run(&dir).map_err(|e| format!("Run {slot} could not be deleted: {e}"))
}

#[tauri::command]
fn remove_game_folders() -> Result<(), String> {
    idle()?;
    manage::remove_game_folders(&runs::home()).map_err(|e| format!("The modded game folders could not be removed: {e}"))
}

#[tauri::command]
fn export_diagnostics(slot: u8) -> Result<String, String> {
    let dir = runs::dir(slot).map_err(|e| e.to_string())?;
    let path = manage::diagnostics(&runs::home(), &dir, slot).map_err(|e| format!("The report could not be written: {e}"))?;
    let _ = std::process::Command::new("explorer").arg("/select,").arg(&path).spawn();
    Ok(path.display().to_string())
}

#[tauri::command]
fn open_releases() {
    let _ = std::process::Command::new("explorer").arg(RELEASES).spawn();
}

const RELEASES: &str = "https://github.com/Decryptu/hkss-decrypted/releases/latest";

/// BepInEx and the plugins: next to the installed launcher, or a folder from `tools/build-payload.ps1`.
fn payload(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = match env::var_os("HKSS_PAYLOAD") {
        Some(dir) => PathBuf::from(dir),
        None => app.path().resource_dir().map_err(|e| e.to_string())?.join("payload"),
    };
    dir.join("bepinex").is_dir().then_some(dir.clone()).ok_or(format!("The mod files are missing from {}", dir.display()))
}

struct Windows {
    cover: Option<WebviewWindow>,
}

impl session::Screen for Windows {
    fn raise_cover(&self, to: Game) {
        let Some(cover) = &self.cover else { return };
        let _ = cover.emit("cover-raise", to.key());
        let _ = cover.set_fullscreen(true);
        let _ = cover.set_always_on_top(true);
        let _ = cover.show();
        let _ = cover.set_focus();
    }

    fn lower_cover(&self) {
        if let Some(cover) = &self.cover {
            let _ = cover.emit("cover-lower", ());
        }
    }

    fn hide_cover(&self) {
        if let Some(cover) = &self.cover {
            let _ = cover.hide();
        }
    }
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![status, play, test_connection, save_room, clear_room, delete_run, remove_game_folders, export_diagnostics, open_releases])
        .on_window_event(|window, event| {
            // The hidden cover must not keep the launcher alive, and closing it must not orphan a game.
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                if window.label() == "cover" || PLAYING.load(Ordering::SeqCst) {
                    api.prevent_close();
                } else {
                    window.app_handle().exit(0);
                }
            }
        })
        .setup(|app| {
            // Development: HKSS_DEV_PLAY=<slot> starts a run without a click and exits when it ends.
            if let Some(slot) = env::var("HKSS_DEV_PLAY").ok().and_then(|slot| slot.parse().ok()) {
                play(app.handle().clone(), slot)?;
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("the launcher could not start");
}

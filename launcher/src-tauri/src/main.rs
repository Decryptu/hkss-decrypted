#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod game;
mod mirror;
mod process;
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
}

#[derive(Serialize)]
struct SlotStatus {
    id: u8,
    current: Option<Game>,
}

#[derive(Serialize)]
struct Status {
    games: Vec<GameStatus>,
    slots: Vec<SlotStatus>,
    steam_running: bool,
}

#[tauri::command]
fn status() -> Result<Status, String> {
    let games = Game::ALL.map(|game| {
        let build = steam::find(game).map(|install| install.build);
        let tested = build.as_deref() == Some(game.tested_build());
        GameStatus { key: game.key(), name: game.name(), build, tested }
    });
    let mut slots = Vec::new();
    for id in runs::SLOTS {
        let run = runs::dir(id).and_then(|dir| runs::load(&dir)).map_err(|e| format!("Run {id}: {e}"))?;
        slots.push(SlotStatus { id, current: run.map(|run| run.current) });
    }
    Ok(Status { games: games.into(), slots, steam_running: steam::is_running() })
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
        .invoke_handler(tauri::generate_handler![status, play])
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

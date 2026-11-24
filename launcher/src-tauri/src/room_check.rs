//! Short Archipelago login probe for the launcher. It never sends checks or updates.

use crate::room::Room;
use futures_util::{SinkExt, StreamExt};
use serde::Serialize;
use serde_json::{json, Value};
use std::{cmp::Ordering, time::Duration};
use tokio_tungstenite::{
    connect_async_with_config,
    tungstenite::{http::Uri, protocol::WebSocketConfig, Message},
};

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct CheckResult {
    pub world_version: Option<String>,
    pub version_warning: Option<String>,
}

pub async fn check(room: Room) -> Result<CheckResult, String> {
    let room = room.validated()?;
    let address = if room.host.contains("://") {
        room.host.clone()
    } else {
        format!("ws://{}", room.host)
    };
    let endpoint: Uri = format!("{address}:{}/", room.port)
        .parse()
        .map_err(|_| invalid_host())?;
    if !matches!(endpoint.scheme_str(), Some("ws" | "wss"))
        || endpoint.path() != "/"
        || endpoint.query().is_some()
        || endpoint.port_u16() != Some(room.port)
        || endpoint
            .authority()
            .is_some_and(|a| a.as_str().contains('@'))
    {
        return Err(invalid_host());
    }
    tokio::time::timeout(Duration::from_secs(10), login(endpoint, &room))
        .await
        .map_err(|_| {
            "The room took too long to respond. Check the host and port with your host.".to_string()
        })?
}

fn invalid_host() -> String {
    "Enter the room host by itself, with the port in the Port field. Use ws:// or wss:// if your host specifies one.".into()
}

async fn login(endpoint: Uri, room: &Room) -> Result<CheckResult, String> {
    let config = WebSocketConfig::default().max_message_size(Some(16 * 1024 * 1024));
    let (mut socket, _) = connect_async_with_config(endpoint, Some(config), false)
        .await
        .map_err(|_| {
            "Could not reach the room. Check the host, port and whether the room is running."
                .to_string()
        })?;
    let mut sent_connect = false;
    while let Some(message) = socket.next().await {
        let Ok(Message::Text(text)) = message else {
            match message {
                Ok(Message::Ping(payload)) => {
                    socket
                        .send(Message::Pong(payload))
                        .await
                        .map_err(|_| disconnected())?;
                }
                Ok(Message::Pong(_)) => {}
                _ => return Err(disconnected()),
            }
            continue;
        };
        let packets: Vec<Value> = serde_json::from_str(&text)
            .map_err(|_| "The server did not reply with Archipelago packets.".to_string())?;
        for packet in packets {
            match (packet["cmd"].as_str(), sent_connect) {
                (Some("RoomInfo"), false) => {
                    let connect = json!([{
                        "cmd": "Connect",
                        "game": "Hollow Knight x Silksong",
                        "name": room.slot,
                        "password": if room.password.is_empty() { None } else { Some(&room.password) },
                        "version": { "class": "Version", "major": 0, "minor": 6, "build": 8 },
                        "items_handling": 7,
                        "tags": ["AP"],
                        "uuid": null,
                        "slot_data": true
                    }]);
                    socket
                        .send(Message::Text(connect.to_string().into()))
                        .await
                        .map_err(|_| disconnected())?;
                    sent_connect = true;
                }
                (Some("Connected"), true) => {
                    let world_version = packet["slot_data"]["world_version"]
                        .as_str()
                        .map(str::trim)
                        .filter(|v| !v.is_empty())
                        .map(str::to_string);
                    let version_warning =
                        version_warning(world_version.as_deref(), env!("CARGO_PKG_VERSION"));
                    let _ = socket.close(None).await;
                    return Ok(CheckResult { world_version, version_warning });
                }
                (Some("ConnectionRefused"), true) => return Err(refusal(&packet)),
                _ => {}
            }
        }
    }
    Err(disconnected())
}

fn parse_semver(version: &str) -> Option<(u64, u64, u64)> {
    let core = version.trim().trim_start_matches('v').split('-').next()?;
    let mut parts = core.split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next()?.parse().ok()?;
    let patch = parts.next().unwrap_or("0").parse().ok()?;
    if parts.next().is_some() {
        return None;
    }
    Some((major, minor, patch))
}

fn version_warning(world_version: Option<&str>, launcher_version: &str) -> Option<String> {
    let Some(world) = world_version else {
        return Some(format!(
            "This room did not report an apworld version (generated with apworld 0.1.2 or earlier), while this launcher is v{launcher_version}."
        ));
    };
    let world_clean = world.trim().trim_start_matches('v');
    let launcher_clean = launcher_version.trim().trim_start_matches('v');
    if world_clean == launcher_clean {
        return None;
    }
    match (parse_semver(world_clean), parse_semver(launcher_clean)) {
        (Some(w), Some(l)) => match w.cmp(&l) {
            Ordering::Equal => None,
            Ordering::Greater => Some(format!(
                "This room was generated with apworld v{world_clean}, which is newer than this launcher (v{launcher_clean}). Update the launcher so both match."
            )),
            Ordering::Less => Some(format!(
                "This room was generated with apworld v{world_clean}, while this launcher is v{launcher_clean}. Items or logic may not match if the apworld changed."
            )),
        },
        _ => Some(format!(
            "This room uses apworld version {world_clean}, while this launcher is v{launcher_clean}."
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::net::TcpListener;
    use tokio_tungstenite::accept_async;

    #[test]
    fn checks_the_combo_login_and_reports_refusals() {
        tauri::async_runtime::block_on(async {
            let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
            let port = listener.local_addr().unwrap().port();
            let current = env!("CARGO_PKG_VERSION");
            let server = tauri::async_runtime::spawn(async move {
                for response in [
                    json!({"cmd": "Connected", "slot_data": {"world_version": current}}),
                    json!({"cmd": "Connected", "slot_data": {"world_version": "9.9.9"}}),
                    json!({"cmd": "Connected", "slot_data": {}}),
                    json!({"cmd": "ConnectionRefused", "errors": ["InvalidPassword", "InvalidSlot"]}),
                ] {
                    let (stream, _) = listener.accept().await.unwrap();
                    let mut socket = accept_async(stream).await.unwrap();
                    socket
                        .send(Message::Text(r#"[{"cmd":"RoomInfo"}]"#.into()))
                        .await
                        .unwrap();
                    let request = socket.next().await.unwrap().unwrap();
                    let packets: Value = serde_json::from_str(request.to_text().unwrap()).unwrap();
                    assert_eq!(packets[0]["cmd"], "Connect");
                    assert_eq!(packets[0]["game"], "Hollow Knight x Silksong");
                    assert_eq!(packets[0]["name"], "Knight");
                    assert_eq!(packets[0]["password"], "room password");
                    assert_eq!(packets[0]["slot_data"], true);
                    socket
                        .send(Message::Text(json!([response]).to_string().into()))
                        .await
                        .unwrap();
                }
            });
            let room = Room {
                host: "127.0.0.1".into(),
                port,
                slot: "Knight".into(),
                password: "room password".into(),
                ..Room::default()
            };
            assert_eq!(
                check(room.clone()).await,
                Ok(CheckResult {
                    world_version: Some(current.into()),
                    version_warning: None,
                })
            );
            let newer = check(room.clone()).await.unwrap();
            assert_eq!(newer.world_version.as_deref(), Some("9.9.9"));
            assert!(newer
                .version_warning
                .as_deref()
                .is_some_and(|w| w.contains("newer than this launcher")));

            let legacy = check(room.clone()).await.unwrap();
            assert_eq!(legacy.world_version, None);
            assert!(legacy
                .version_warning
                .as_deref()
                .is_some_and(|w| w.contains("did not report an apworld version")));

            let error = check(room).await.unwrap_err();
            assert!(
                error.contains("password is incorrect")
                    && error.contains("slot name was not found")
            );
            server.await.unwrap();
        });
    }

    #[test]
    fn compares_world_and_launcher_versions() {
        assert_eq!(version_warning(Some("0.1.2"), "0.1.2"), None);
        assert!(version_warning(Some("0.1.3"), "0.1.2")
            .unwrap()
            .contains("Update the launcher"));
        assert!(version_warning(Some("0.1.1"), "0.1.2")
            .unwrap()
            .contains("while this launcher is v0.1.2"));
        assert!(version_warning(None, "0.1.2")
            .unwrap()
            .contains("0.1.2 or earlier"));
    }
}

fn disconnected() -> String {
    "The room disconnected before accepting the slot. Check the room details and try again.".into()
}

fn refusal(packet: &Value) -> String {
    let mut messages = Vec::new();
    for error in packet["errors"].as_array().into_iter().flatten() {
        let message = match error.as_str() {
            Some("InvalidPassword") => "The room password is incorrect.",
            Some("InvalidSlot") => {
                "The slot name was not found. Use the exact name from your host."
            }
            Some("InvalidGame") => {
                "This slot is for a different game. Ask your host for your combo slot."
            }
            Some("IncompatibleVersion") => "The room rejected this client's Archipelago version.",
            Some("SlotAlreadyTaken") => {
                "The slot is already in use. Close the game or check with your host."
            }
            _ => continue,
        };
        messages.push(message);
    }
    if messages.is_empty() {
        return "The room refused the login. Check the slot name and password with your host."
            .into();
    }
    messages.join(" ")
}

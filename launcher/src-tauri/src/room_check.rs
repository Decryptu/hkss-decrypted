//! Short Archipelago login probe for the launcher. It never sends checks or updates.

use crate::room::Room;
use futures_util::{SinkExt, StreamExt};
use serde_json::{json, Value};
use std::time::Duration;
use tokio_tungstenite::{
    connect_async_with_config,
    tungstenite::{http::Uri, protocol::WebSocketConfig, Message},
};

pub async fn check(room: Room) -> Result<(), String> {
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

async fn login(endpoint: Uri, room: &Room) -> Result<(), String> {
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
                        "slot_data": false
                    }]);
                    socket
                        .send(Message::Text(connect.to_string().into()))
                        .await
                        .map_err(|_| disconnected())?;
                    sent_connect = true;
                }
                (Some("Connected"), true) => {
                    let _ = socket.close(None).await;
                    return Ok(());
                }
                (Some("ConnectionRefused"), true) => return Err(refusal(&packet)),
                _ => {}
            }
        }
    }
    Err(disconnected())
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
            let server = tauri::async_runtime::spawn(async move {
                for response in [
                    json!({"cmd": "Connected"}),
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
                    assert_eq!(packets[0]["slot_data"], false);
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
            assert_eq!(check(room.clone()).await, Ok(()));
            let error = check(room).await.unwrap_err();
            assert!(
                error.contains("password is incorrect")
                    && error.contains("slot name was not found")
            );
            server.await.unwrap();
        });
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

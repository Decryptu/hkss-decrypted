//! In-place launcher updates: checks GitHub Releases, downloads the Windows NSIS installer,
//! verifies its SHA-256 digest and size, and launches it in passive update mode.

use futures_util::StreamExt;
use reqwest::{redirect::Policy, Client, Url};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    fs,
    path::{Path, PathBuf},
    process::Command,
    time::Duration,
};
use tokio::io::AsyncWriteExt;

const LATEST_RELEASE_API: &str = "https://api.github.com/repos/Decryptu/hkss-decrypted/releases/latest";
const ALLOWED_DOWNLOAD_PREFIX: &str = "https://github.com/Decryptu/hkss-decrypted/releases/download/";
const ALLOWED_DOWNLOAD_HOSTS: &[&str] = &[
    "github.com",
    "objects.githubusercontent.com",
    "release-assets.githubusercontent.com",
    "github-releases.githubusercontent.com",
];
const USER_AGENT: &str = concat!("HKSS-Launcher/", env!("CARGO_PKG_VERSION"));
const MAX_INSTALLER_BYTES: u64 = 256 * 1024 * 1024;
const PROGRESS_STEP_BYTES: u64 = 64 * 1024;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct UpdateInfo {
    pub version: String,
    pub tag: String,
    pub asset_name: String,
    pub download_url: String,
    pub sha256: Option<String>,
    pub size: u64,
    pub release_url: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct UpdateProgress {
    pub stage: &'static str,
    pub downloaded: u64,
    pub total: Option<u64>,
}

#[derive(Deserialize)]
struct GitHubRelease {
    tag_name: String,
    #[serde(default)]
    draft: bool,
    #[serde(default)]
    prerelease: bool,
    #[serde(default)]
    html_url: String,
    #[serde(default)]
    assets: Vec<GitHubAsset>,
}

#[derive(Deserialize)]
struct GitHubAsset {
    name: String,
    size: u64,
    #[serde(default)]
    digest: Option<String>,
    browser_download_url: String,
}

/// Checks GitHub Releases for a newer stable launcher installer.
pub async fn check_latest(current_version: &str) -> Result<Option<UpdateInfo>, String> {
    let client = api_client()?;
    let response = client
        .get(LATEST_RELEASE_API)
        .header("Accept", "application/vnd.github+json")
        .send()
        .await
        .map_err(|_| "Could not reach GitHub to check for updates.".to_string())?;
    if !response.status().is_success() {
        return Err(format!("GitHub returned status {} while checking for updates.", response.status().as_u16()));
    }
    let release: GitHubRelease = response
        .json()
        .await
        .map_err(|_| "GitHub returned an unexpected release response.".to_string())?;
    Ok(parse_release(release, current_version))
}

/// Downloads and verifies the update installer into `<home>/updates/`.
pub async fn download_and_verify<F>(
    home: &Path,
    info: &UpdateInfo,
    on_progress: F,
) -> Result<PathBuf, String>
where
    F: FnMut(UpdateProgress),
{
    validate_update_info(info, true)?;
    let client = download_client(true)?;
    download_and_verify_with_client(&client, home, info, on_progress).await
}

/// Starts the verified NSIS installer in passive update mode with automatic relaunch.
pub fn launch_installer(path: &Path) -> Result<(), String> {
    Command::new(path)
        .args(["/P", "/UPDATE", "/R"])
        .spawn()
        .map(|_| ())
        .map_err(|e| format!("Could not start the installer: {e}"))
}

/// Removes downloaded installers left behind by earlier updates.
pub fn cleanup_stale_updates(home: &Path) {
    let dir = home.join("updates");
    let Ok(entries) = fs::read_dir(&dir) else { return };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_file() {
            let _ = fs::remove_file(path);
        }
    }
}

fn api_client() -> Result<Client, String> {
    Client::builder()
        .user_agent(USER_AGENT)
        .timeout(Duration::from_secs(10))
        .https_only(true)
        .build()
        .map_err(|e| format!("Could not start the update client: {e}"))
}

fn download_client(strict_https: bool) -> Result<Client, String> {
    let mut builder = Client::builder()
        .user_agent(USER_AGENT)
        .connect_timeout(Duration::from_secs(15))
        .timeout(Duration::from_secs(300));
    if strict_https {
        builder = builder.https_only(true).redirect(Policy::custom(|attempt| {
            if attempt.previous().len() >= 5 {
                return attempt.error("too many redirects");
            }
            if is_allowed_redirect_url(attempt.url()) {
                attempt.follow()
            } else {
                attempt.error("redirect pointed outside GitHub release hosts")
            }
        }));
    }
    builder
        .build()
        .map_err(|e| format!("Could not start the download client: {e}"))
}

fn is_allowed_redirect_url(url: &Url) -> bool {
    url.scheme() == "https"
        && url
            .host_str()
            .is_some_and(|host| ALLOWED_DOWNLOAD_HOSTS.iter().any(|allowed| host.eq_ignore_ascii_case(allowed)))
}

fn parse_release(release: GitHubRelease, current_version: &str) -> Option<UpdateInfo> {
    if release.draft || release.prerelease {
        return None;
    }
    let tag = release.tag_name.trim().to_string();
    let version = tag.trim_start_matches('v').to_string();
    if !is_newer(&version, current_version) {
        return None;
    }
    let asset = select_installer_asset(&release.assets, &version)?;
    if !is_safe_asset_name(&asset.name) || !is_allowed_download_url(&asset.browser_download_url) {
        return None;
    }
    let sha256 = asset.digest.as_deref().and_then(parse_sha256_digest);
    let release_url = if release.html_url.starts_with("https://github.com/Decryptu/hkss-decrypted/") {
        release.html_url
    } else {
        format!("https://github.com/Decryptu/hkss-decrypted/releases/tag/{tag}")
    };
    Some(UpdateInfo {
        version,
        tag,
        asset_name: asset.name.clone(),
        download_url: asset.browser_download_url.clone(),
        sha256,
        size: asset.size,
        release_url,
    })
}

fn select_installer_asset<'a>(assets: &'a [GitHubAsset], version: &str) -> Option<&'a GitHubAsset> {
    let exact = format!("HKSS.Launcher_{version}_x64-setup.exe");
    assets
        .iter()
        .find(|asset| asset.name.eq_ignore_ascii_case(&exact))
        .or_else(|| {
            assets.iter().find(|asset| {
                let lower = asset.name.to_ascii_lowercase();
                lower.starts_with("hkss.launcher_") && lower.ends_with("_x64-setup.exe")
            })
        })
}

fn parse_version(value: &str) -> Option<(u64, u64, u64)> {
    let trimmed = value.trim().trim_start_matches('v');
    let mut parts = trimmed.split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next()?.parse().ok()?;
    let patch_part = parts.next()?;
    let patch_digits: String = patch_part.chars().take_while(|c| c.is_ascii_digit()).collect();
    if patch_digits.is_empty() {
        return None;
    }
    let patch = patch_digits.parse().ok()?;
    Some((major, minor, patch))
}

pub fn is_newer(candidate: &str, current: &str) -> bool {
    match (parse_version(candidate), parse_version(current)) {
        (Some(a), Some(b)) => a > b,
        _ => false,
    }
}

fn parse_sha256_digest(raw: &str) -> Option<String> {
    let hex = raw.trim().strip_prefix("sha256:")?;
    if hex.len() == 64 && hex.bytes().all(|b| b.is_ascii_hexdigit()) {
        Some(hex.to_ascii_lowercase())
    } else {
        None
    }
}

fn is_safe_asset_name(name: &str) -> bool {
    !name.is_empty()
        && !name.contains('/')
        && !name.contains('\\')
        && !name.contains("..")
        && name.to_ascii_lowercase().ends_with(".exe")
}

fn is_allowed_download_url(url: &str) -> bool {
    url.starts_with(ALLOWED_DOWNLOAD_PREFIX)
        && Url::parse(url).ok().is_some_and(|parsed| is_allowed_redirect_url(&parsed))
}

fn validate_update_info(info: &UpdateInfo, strict_url: bool) -> Result<String, String> {
    if !is_safe_asset_name(&info.asset_name) {
        return Err("The release installer filename is invalid.".into());
    }
    if strict_url && !is_allowed_download_url(&info.download_url) {
        return Err("The release download URL is not a GitHub release asset for this repository.".into());
    }
    if info.size == 0 || info.size > MAX_INSTALLER_BYTES {
        return Err("The release installer size is outside the expected range.".into());
    }
    let Some(expected) = info.sha256.as_deref() else {
        return Err("This release does not include a SHA-256 digest. Download it manually from the release page.".into());
    };
    if expected.len() != 64 || !expected.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err("The release SHA-256 digest is malformed.".into());
    }
    Ok(expected.to_ascii_lowercase())
}

async fn download_and_verify_with_client<F>(
    client: &Client,
    home: &Path,
    info: &UpdateInfo,
    mut on_progress: F,
) -> Result<PathBuf, String>
where
    F: FnMut(UpdateProgress),
{
    let expected_sha256 = validate_update_info(info, false)?;
    let updates_dir = home.join("updates");
    fs::create_dir_all(&updates_dir)
        .map_err(|e| format!("Could not prepare the update folder: {e}"))?;

    let part_path = updates_dir.join(format!("{}.part", info.asset_name));
    let final_path = updates_dir.join(&info.asset_name);

    let result = async {
        let response = client
            .get(&info.download_url)
            .send()
            .await
            .map_err(|e| format!("Could not download the update: {e}"))?;
        if !response.status().is_success() {
            return Err(format!("Download failed with HTTP status {}.", response.status().as_u16()));
        }

        let total = response.content_length().or(Some(info.size));
        if let Some(len) = total {
            if len > MAX_INSTALLER_BYTES {
                return Err("The downloaded installer exceeds the maximum allowed size.".into());
            }
        }

        let mut file = tokio::fs::File::create(&part_path)
            .await
            .map_err(|e| format!("Could not create temporary installer file: {e}"))?;
        let mut hasher = Sha256::new();
        let mut stream = response.bytes_stream();
        let mut downloaded: u64 = 0;
        let mut last_reported: u64 = 0;
        let mut magic = [0u8; 2];
        let mut magic_len = 0usize;

        on_progress(UpdateProgress {
            stage: "downloading",
            downloaded: 0,
            total,
        });

        while let Some(chunk) = stream.next().await {
            let chunk = chunk.map_err(|e| format!("Download interrupted: {e}"))?;
            if magic_len < 2 {
                let take = (2 - magic_len).min(chunk.len());
                magic[magic_len..magic_len + take].copy_from_slice(&chunk[..take]);
                magic_len += take;
            }
            downloaded = downloaded
                .checked_add(chunk.len() as u64)
                .ok_or_else(|| "Download size overflow.".to_string())?;
            if downloaded > MAX_INSTALLER_BYTES || downloaded > info.size {
                return Err("The downloaded installer is larger than expected.".into());
            }
            hasher.update(&chunk);
            file.write_all(&chunk)
                .await
                .map_err(|e| format!("Could not write temporary installer file: {e}"))?;

            if downloaded.saturating_sub(last_reported) >= PROGRESS_STEP_BYTES {
                last_reported = downloaded;
                on_progress(UpdateProgress {
                    stage: "downloading",
                    downloaded,
                    total,
                });
            }
        }

        file.flush()
            .await
            .map_err(|e| format!("Could not finish writing the installer: {e}"))?;
        drop(file);

        on_progress(UpdateProgress {
            stage: "verifying",
            downloaded,
            total,
        });

        if downloaded != info.size {
            return Err(format!(
                "Download was incomplete ({downloaded} of {} bytes). Try again.",
                info.size
            ));
        }
        if magic_len < 2 || &magic != b"MZ" {
            return Err("The downloaded file is not a valid Windows installer.".into());
        }

        let actual_sha256 = format!("{:x}", hasher.finalize());
        if actual_sha256 != expected_sha256 {
            return Err("Installer checksum verification failed. The download was discarded.".into());
        }

        if final_path.exists() {
            fs::remove_file(&final_path)
                .map_err(|e| format!("Could not replace an earlier downloaded installer: {e}"))?;
        }
        fs::rename(&part_path, &final_path)
            .map_err(|e| format!("Could not finalize the downloaded installer: {e}"))?;

        on_progress(UpdateProgress {
            stage: "installing",
            downloaded,
            total,
        });

        Ok(final_path.clone())
    }
    .await;

    if result.is_err() {
        let _ = fs::remove_file(&part_path);
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;

    fn sample_release(tag: &str, digest: Option<&str>) -> GitHubRelease {
        GitHubRelease {
            tag_name: tag.into(),
            draft: false,
            prerelease: false,
            html_url: format!("https://github.com/Decryptu/hkss-decrypted/releases/tag/{tag}"),
            assets: vec![
                GitHubAsset {
                    name: "hkss.apworld".into(),
                    size: 2_000_000,
                    digest: Some("sha256:0000000000000000000000000000000000000000000000000000000000000000".into()),
                    browser_download_url: format!("https://github.com/Decryptu/hkss-decrypted/releases/download/{tag}/hkss.apworld"),
                },
                GitHubAsset {
                    name: format!("HKSS.Launcher_{}_x64-setup.exe", tag.trim_start_matches('v')),
                    size: 12_345_678,
                    digest: digest.map(str::to_string),
                    browser_download_url: format!(
                        "https://github.com/Decryptu/hkss-decrypted/releases/download/{tag}/HKSS.Launcher_{}_x64-setup.exe",
                        tag.trim_start_matches('v')
                    ),
                },
            ],
        }
    }

    #[test]
    fn parses_and_compares_versions() {
        assert!(is_newer("0.1.17", "0.1.16"));
        assert!(is_newer("v0.2.0", "0.1.99"));
        assert!(is_newer("1.0.0", "0.9.9"));
        assert!(!is_newer("0.1.16", "0.1.16"));
        assert!(!is_newer("0.1.15", "0.1.16"));
        assert!(!is_newer("not-a-version", "0.1.16"));
    }

    #[test]
    fn selects_windows_nsis_asset_and_sha256_digest() {
        let digest = "sha256:7E1E3E45119E4C965AEAA18DA27D227E6BAF3DEE2480B2AD1108F96BB11B646A";
        let info = parse_release(sample_release("v0.1.17", Some(digest)), "0.1.16").unwrap();
        assert_eq!(info.version, "0.1.17");
        assert_eq!(info.tag, "v0.1.17");
        assert_eq!(info.asset_name, "HKSS.Launcher_0.1.17_x64-setup.exe");
        assert_eq!(info.size, 12_345_678);
        assert_eq!(
            info.sha256.as_deref(),
            Some("7e1e3e45119e4c965aeaa18da27d227e6baf3dee2480b2ad1108f96bb11b646a")
        );
    }

    #[test]
    fn ignores_draft_prerelease_and_older_versions() {
        let mut draft = sample_release("v0.2.0", None);
        draft.draft = true;
        assert!(parse_release(draft, "0.1.16").is_none());

        let mut prerelease = sample_release("v0.2.0", None);
        prerelease.prerelease = true;
        assert!(parse_release(prerelease, "0.1.16").is_none());

        assert!(parse_release(sample_release("v0.1.16", None), "0.1.16").is_none());
        assert!(parse_release(sample_release("v0.1.15", None), "0.1.16").is_none());
    }

    #[test]
    fn rejects_untrusted_urls_and_path_traversal() {
        let good_sha = "7e1e3e45119e4c965aeaa18da27d227e6baf3dee2480b2ad1108f96bb11b646a".to_string();
        let bad_url = UpdateInfo {
            version: "0.1.17".into(),
            tag: "v0.1.17".into(),
            asset_name: "HKSS.Launcher_0.1.17_x64-setup.exe".into(),
            download_url: "https://example.com/HKSS.Launcher_0.1.17_x64-setup.exe".into(),
            sha256: Some(good_sha.clone()),
            size: 1024,
            release_url: "https://github.com/Decryptu/hkss-decrypted/releases/tag/v0.1.17".into(),
        };
        assert!(validate_update_info(&bad_url, true).is_err());

        let bad_name = UpdateInfo {
            asset_name: "..\\evil.exe".into(),
            download_url: "https://github.com/Decryptu/hkss-decrypted/releases/download/v0.1.17/HKSS.Launcher_0.1.17_x64-setup.exe".into(),
            ..bad_url.clone()
        };
        assert!(validate_update_info(&bad_name, true).is_err());

        let missing_sha = UpdateInfo {
            sha256: None,
            download_url: "https://github.com/Decryptu/hkss-decrypted/releases/download/v0.1.17/HKSS.Launcher_0.1.17_x64-setup.exe".into(),
            ..bad_url
        };
        assert!(validate_update_info(&missing_sha, true).is_err());
    }

    #[test]
    fn downloads_verifies_and_discards_corrupt_installers() {
        tauri::async_runtime::block_on(async {
            let payload: Vec<u8> = b"MZ\x90\x00fake-nsis-installer-payload".to_vec();
            let valid_sha = format!("{:x}", Sha256::digest(&payload));

            let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
            let port = listener.local_addr().unwrap().port();
            let body_for_server = payload.clone();
            let server = tauri::async_runtime::spawn(async move {
                for _ in 0..2 {
                    let (mut stream, _) = listener.accept().await.unwrap();
                    let mut req_buf = [0u8; 1024];
                    let _ = stream.read(&mut req_buf).await;
                    let header = format!(
                        "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
                        body_for_server.len()
                    );
                    stream.write_all(header.as_bytes()).await.unwrap();
                    stream.write_all(&body_for_server).await.unwrap();
                }
            });

            let temp = tempfile::tempdir().unwrap();
            let client = download_client(false).unwrap();
            let base = UpdateInfo {
                version: "0.1.17".into(),
                tag: "v0.1.17".into(),
                asset_name: "HKSS.Launcher_0.1.17_x64-setup.exe".into(),
                download_url: format!("http://127.0.0.1:{port}/HKSS.Launcher_0.1.17_x64-setup.exe"),
                sha256: Some(valid_sha),
                size: payload.len() as u64,
                release_url: "https://github.com/Decryptu/hkss-decrypted/releases/tag/v0.1.17".into(),
            };

            let mut stages = Vec::new();
            let path = download_and_verify_with_client(&client, temp.path(), &base, |p| {
                stages.push(p.stage);
            })
            .await
            .unwrap();
            assert_eq!(fs::read(&path).unwrap(), payload);
            assert!(stages.contains(&"downloading"));
            assert!(stages.contains(&"verifying"));
            assert!(stages.contains(&"installing"));

            let bad_sha = UpdateInfo {
                sha256: Some("0".repeat(64)),
                ..base
            };
            let err = download_and_verify_with_client(&client, temp.path(), &bad_sha, |_| {})
                .await
                .unwrap_err();
            assert!(err.contains("checksum verification failed"));
            assert!(!temp.path().join("updates/HKSS.Launcher_0.1.17_x64-setup.exe.part").exists());

            cleanup_stale_updates(temp.path());
            assert!(!path.exists());

            server.await.unwrap();
        });
    }
}

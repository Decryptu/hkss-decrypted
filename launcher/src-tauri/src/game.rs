use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Game {
    HollowKnight,
    Silksong,
}

impl Game {
    pub const ALL: [Game; 2] = [Game::HollowKnight, Game::Silksong];

    /// Matches `GameIds.Key` in HKSS.Core.
    pub fn key(self) -> &'static str {
        match self {
            Game::HollowKnight => "hollow_knight",
            Game::Silksong => "silksong",
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Game::HollowKnight => "Hollow Knight",
            Game::Silksong => "Silksong",
        }
    }

    pub fn app_id(self) -> u32 {
        match self {
            Game::HollowKnight => 367520,
            Game::Silksong => 1030300,
        }
    }

    pub fn exe(self) -> &'static str {
        match self {
            Game::HollowKnight => "hollow_knight.exe",
            Game::Silksong => "Hollow Knight Silksong.exe",
        }
    }

    pub fn data_dir(self) -> &'static str {
        match self {
            Game::HollowKnight => "hollow_knight_Data",
            Game::Silksong => "Hollow Knight Silksong_Data",
        }
    }

    /// Expected Unity major version prefix (`6000.` for Unity 6).
    pub fn expected_unity_major(self) -> &'static str {
        match self {
            Game::HollowKnight | Game::Silksong => "6000.",
        }
    }

    /// The Steam build the plugins were last verified on.
    pub fn tested_build(self) -> &'static str {
        match self {
            Game::HollowKnight => "22529139",
            Game::Silksong => "22479045",
        }
    }
}

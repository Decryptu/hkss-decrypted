using System;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>
/// Per-game statistics for a combo run, stored in <c>state.json</c> so the other game and the
/// launcher can show a combined end-of-run summary.
/// </summary>
public sealed class GameStats
{
    /// <summary>Play time from the game's own timer, in seconds.</summary>
    [JsonProperty("play_seconds")]
    public double PlaySeconds { get; set; }

    [JsonProperty("deaths")]
    public int Deaths { get; set; }

    [JsonProperty("kills")]
    public int Kills { get; set; }

    /// <summary>The game's own completion percentage.</summary>
    [JsonProperty("completion_percent")]
    public double CompletionPercent { get; set; }

    /// <summary>Archipelago locations of this game checked in the slot (as of the last sync).</summary>
    [JsonProperty("checks_found")]
    public int ChecksFound { get; set; }

    /// <summary>Archipelago locations of this game in the slot (0 until a connection reports it).</summary>
    [JsonProperty("checks_total")]
    public int ChecksTotal { get; set; }

    /// <summary>True once this game has written any stats (it has been played in this run).</summary>
    [JsonIgnore]
    public bool Played => PlaySeconds > 0 || Deaths > 0 || Kills > 0 || ChecksFound > 0;

    public GameStats Clone() => (GameStats)MemberwiseClone();
}

/// <summary>Combined totals across both games, built from <see cref="RunState"/>.</summary>
public sealed class RunSummary
{
    public GameStats HollowKnight { get; }
    public GameStats Silksong { get; }
    public bool HkGoalCompleted { get; }
    public bool SsGoalCompleted { get; }
    public bool DoubleSteelSoul { get; }
    public bool SteelSoulDefeated { get; }
    public string SteelSoulDefeatedGame { get; }
    public string SteelSoulDefeatedScene { get; }

    public RunSummary(RunState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        HollowKnight = state.GetStats(GameId.HollowKnight).Clone();
        Silksong = state.GetStats(GameId.Silksong).Clone();
        HkGoalCompleted = state.HkGoalCompleted;
        SsGoalCompleted = state.SsGoalCompleted;
        DoubleSteelSoul = state.DoubleSteelSoul;
        SteelSoulDefeated = state.SteelSoulDefeated;
        SteelSoulDefeatedGame = state.SteelSoulDefeatedGame ?? string.Empty;
        SteelSoulDefeatedScene = state.SteelSoulDefeatedScene ?? string.Empty;
    }

    public GameStats Get(GameId game) => game == GameId.HollowKnight ? HollowKnight : Silksong;

    public double TotalPlaySeconds => HollowKnight.PlaySeconds + Silksong.PlaySeconds;
    public int TotalDeaths => HollowKnight.Deaths + Silksong.Deaths;
    public int TotalKills => HollowKnight.Kills + Silksong.Kills;
    public int TotalChecksFound => HollowKnight.ChecksFound + Silksong.ChecksFound;
    public int TotalChecks => HollowKnight.ChecksTotal + Silksong.ChecksTotal;

    /// <summary>Formats seconds as <c>H:MM:SS</c>.</summary>
    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        long s = (long)Math.Floor(seconds);
        return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }

    /// <summary>Formats checks as <c>found / total</c>, or just <c>found</c> when the total is unknown.</summary>
    public static string FormatChecks(int found, int total) => total > 0 ? $"{found} / {total}" : found.ToString();
}

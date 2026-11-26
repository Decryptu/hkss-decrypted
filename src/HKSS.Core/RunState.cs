using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>
/// Gameplay progress shared across both games in a combo run. Stored separately from
/// <c>run.json</c> (which the launcher owns and rewrites on every game switch) and
/// <c>ap.json</c> (which holds room connection settings).
/// </summary>
public sealed class RunState
{
    /// <summary>
    /// Archipelago room seed + slot binding (<c>seed|slot</c>) so reusing a run slot for a new room
    /// resets processed item indices and offline checks cleanly.
    /// </summary>
    [JsonProperty("seed_key")]
    public string SeedKey { get; set; } = string.Empty;

    /// <summary>True once Hollow Knight's goal has been achieved in this run (persisted for offline retry).</summary>
    [JsonProperty("hk_goal_completed")]
    public bool HkGoalCompleted { get; set; }

    /// <summary>True once Silksong's goal has been achieved in this run (persisted for offline retry).</summary>
    [JsonProperty("ss_goal_completed")]
    public bool SsGoalCompleted { get; set; }

    /// <summary>
    /// True once the player has reached Bone Bottom in Silksong, opening its return gate
    /// and enabling town-to-town travel and town warping.
    /// </summary>
    [JsonProperty("bonetown_gate_open")]
    public bool BonetownGateOpen { get; set; }

    /// <summary>
    /// Number of entries in the Archipelago slot's received item stream already processed
    /// by Hollow Knight. Stored per game so items found while the other game was active
    /// are applied on the next load.
    /// </summary>
    [JsonProperty("hk_processed_items")]
    public int HkProcessedItems { get; set; }

    /// <summary>
    /// Number of entries in the Archipelago slot's received item stream already processed
    /// by Silksong.
    /// </summary>
    [JsonProperty("ss_processed_items")]
    public int SsProcessedItems { get; set; }

    /// <summary>Combined location IDs checked in Hollow Knight (flushed to AP on connect).</summary>
    [JsonProperty("hk_checked_locations")]
    public List<long> HkCheckedLocations { get; set; } = new();

    /// <summary>Combined location IDs checked in Silksong (flushed to AP on connect).</summary>
    [JsonProperty("ss_checked_locations")]
    public List<long> SsCheckedLocations { get; set; } = new();

    /// <summary>
    /// Combined item IDs of cross-game seal keys already received, so sealed areas stay open
    /// while the game is offline.
    /// </summary>
    [JsonProperty("seal_keys")]
    public List<long> SealKeys { get; set; } = new();

    /// <summary>Hollow Knight statistics for the end-of-run summary.</summary>
    [JsonProperty("hk_stats")]
    public GameStats HkStats { get; set; } = new();

    /// <summary>Silksong statistics for the end-of-run summary.</summary>
    [JsonProperty("ss_stats")]
    public GameStats SsStats { get; set; } = new();

    /// <summary>Trips between the two games in this run.</summary>
    [JsonProperty("travels")]
    public int Travels { get; set; }

    /// <summary>True when Double Steel Soul is active for this combo run.</summary>
    [JsonProperty("double_steel_soul")]
    public bool DoubleSteelSoul { get; set; }

    /// <summary>True once a Steel Soul death in either game has defeated this combo run.</summary>
    [JsonProperty("steel_soul_defeated")]
    public bool SteelSoulDefeated { get; set; }

    /// <summary>Game key (<c>hollow_knight</c> or <c>silksong</c>) where the Steel Soul run fell.</summary>
    [JsonProperty("steel_soul_defeated_game")]
    public string SteelSoulDefeatedGame { get; set; } = string.Empty;

    /// <summary>Scene name where the Steel Soul run fell, if known.</summary>
    [JsonProperty("steel_soul_defeated_scene")]
    public string SteelSoulDefeatedScene { get; set; } = string.Empty;

    /// <summary>The run's title, derived from its stats (for the launcher; recomputed on every save).</summary>
    [JsonProperty("epithet")]
    public string Epithet { get; set; } = string.Empty;

    /// <summary>Feats earned so far, derived from the stats (for the launcher; recomputed on every save).</summary>
    [JsonProperty("feats")]
    public List<Feat> Feats { get; set; } = new();

    /// <summary>Area with the most deaths, as <c>Name (count)</c> (for the launcher; recomputed on every save).</summary>
    [JsonProperty("deadliest_area")]
    public string DeadliestArea { get; set; } = string.Empty;

    /// <summary>Area with the most play time, as <c>Name (H:MM:SS)</c> (for the launcher; recomputed on every save).</summary>
    [JsonProperty("favourite_area")]
    public string FavouriteArea { get; set; } = string.Empty;

    /// <summary>Recomputes the derived summary fields from the stats.</summary>
    public void RefreshSummary()
    {
        var summary = new RunSummary(this);
        Epithet = summary.Epithet;
        Feats = new List<Feat>(summary.Feats);
        DeadliestArea = summary.DeadliestArea is { } d ? $"{d.Name} ({d.Value:0})" : string.Empty;
        FavouriteArea = summary.FavouriteArea is { } f ? $"{f.Name} ({RunSummary.FormatDuration(f.Value)})" : string.Empty;
    }

    public GameStats GetStats(GameId game) => game switch
    {
        GameId.HollowKnight => HkStats ??= new GameStats(),
        GameId.Silksong => SsStats ??= new GameStats(),
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public int GetProcessedItems(GameId game) => game switch
    {
        GameId.HollowKnight => HkProcessedItems,
        GameId.Silksong => SsProcessedItems,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public void SetProcessedItems(GameId game, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, null);
        switch (game)
        {
            case GameId.HollowKnight: HkProcessedItems = count; break;
            case GameId.Silksong: SsProcessedItems = count; break;
            default: throw new ArgumentOutOfRangeException(nameof(game), game, null);
        }
    }

    public List<long> GetCheckedLocations(GameId game) => game switch
    {
        GameId.HollowKnight => HkCheckedLocations ??= new List<long>(),
        GameId.Silksong => SsCheckedLocations ??= new List<long>(),
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public bool GetGoalCompleted(GameId game) => game switch
    {
        GameId.HollowKnight => HkGoalCompleted,
        GameId.Silksong => SsGoalCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public void SetGoalCompleted(GameId game, bool completed)
    {
        switch (game)
        {
            case GameId.HollowKnight: HkGoalCompleted = completed; break;
            case GameId.Silksong: SsGoalCompleted = completed; break;
            default: throw new ArgumentOutOfRangeException(nameof(game), game, null);
        }
    }
}

/// <summary>Atomic reader/writer for <c>state.json</c> inside a combo run folder.</summary>
public sealed class RunStateStore
{
    public const string StateFileName = "state.json";

    private readonly string _path;

    public RunStateStore(RunContext run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        _path = Path.Combine(run.RunDir, StateFileName);
    }

    public RunState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new RunState();
            return JsonConvert.DeserializeObject<RunState>(File.ReadAllText(_path)) ?? new RunState();
        }
        catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
        {
            return new RunState();
        }
    }

    public void Save(RunState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        state.RefreshSummary();
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonConvert.SerializeObject(state, Formatting.Indented);
        string temp = _path + ".new";
        File.WriteAllText(temp, json);
        if (File.Exists(_path)) File.Replace(temp, _path, null);
        else File.Move(temp, _path);
    }

    /// <summary>Marks Bone Bottom's gate as open if it was not already open. Returns true if changed.</summary>
    public bool MarkBonetownGateOpen()
    {
        RunState state = Load();
        if (state.BonetownGateOpen) return false;
        state.BonetownGateOpen = true;
        Save(state);
        return true;
    }

    public int GetProcessedItems(GameId game) => Load().GetProcessedItems(game);

    public void SetProcessedItems(GameId game, int count)
    {
        RunState state = Load();
        if (state.GetProcessedItems(game) == count) return;
        state.SetProcessedItems(game, count);
        Save(state);
    }

    public bool RecordCheckedLocation(GameId game, long combinedLocationId)
    {
        if (IdSpace.GameForId(combinedLocationId) != game)
            throw new ArgumentOutOfRangeException(nameof(combinedLocationId), combinedLocationId,
                $"Location ID {combinedLocationId} does not belong to {game}.");

        RunState state = Load();
        List<long> list = state.GetCheckedLocations(game);
        if (list.Contains(combinedLocationId)) return false;
        list.Add(combinedLocationId);
        Save(state);
        return true;
    }

    public IReadOnlyList<long> GetCheckedLocations(GameId game) => Load().GetCheckedLocations(game);

    /// <summary>
    /// Binds <c>state.json</c> to the connected Archipelago room seed/slot. Returns <c>true</c> if
    /// the run was previously bound to a different seed and its AP counters/checks were reset.
    /// </summary>
    public bool EnsureSeedBinding(string seedKey)
    {
        if (string.IsNullOrWhiteSpace(seedKey)) return false;

        RunState state = Load();
        if (string.IsNullOrEmpty(state.SeedKey))
        {
            state.SeedKey = seedKey;
            Save(state);
            return false;
        }
        if (string.Equals(state.SeedKey, seedKey, StringComparison.Ordinal))
            return false;

        state.SeedKey = seedKey;
        state.HkProcessedItems = 0;
        state.SsProcessedItems = 0;
        state.HkCheckedLocations.Clear();
        state.SsCheckedLocations.Clear();
        state.HkGoalCompleted = false;
        state.SsGoalCompleted = false;
        state.HkStats = new GameStats();
        state.SsStats = new GameStats();
        state.Travels = 0;
        state.SealKeys = new List<long>();
        state.DoubleSteelSoul = false;
        state.SteelSoulDefeated = false;
        state.SteelSoulDefeatedGame = string.Empty;
        state.SteelSoulDefeatedScene = string.Empty;
        Save(state);
        return true;
    }

    public bool IsDoubleSteelSoul() => Load().DoubleSteelSoul;

    public bool SetDoubleSteelSoul(bool enabled)
    {
        RunState state = Load();
        if (state.DoubleSteelSoul == enabled) return false;
        state.DoubleSteelSoul = enabled;
        Save(state);
        return true;
    }

    public bool IsSteelSoulDefeated() => Load().SteelSoulDefeated;

    /// <summary>
    /// Marks the combo run defeated by a Steel Soul death in <paramref name="game"/>.
    /// Idempotent: preserves the first game and scene that caused the defeat.
    /// </summary>
    public bool MarkSteelSoulDefeated(GameId game, string? scene = null)
    {
        RunState state = Load();
        if (state.SteelSoulDefeated) return false;
        state.DoubleSteelSoul = true;
        state.SteelSoulDefeated = true;
        state.SteelSoulDefeatedGame = game.Key();
        state.SteelSoulDefeatedScene = scene?.Trim() ?? string.Empty;
        Save(state);
        return true;
    }

    public IReadOnlyList<long> GetSealKeys() => Load().SealKeys ?? new List<long>();

    /// <summary>Remembers received seal keys. Returns true if any key was new.</summary>
    public bool RecordSealKeys(IEnumerable<long> keyIds)
    {
        if (keyIds == null) throw new ArgumentNullException(nameof(keyIds));
        RunState state = Load();
        state.SealKeys ??= new List<long>();
        bool changed = false;
        foreach (long id in keyIds)
        {
            if (state.SealKeys.Contains(id)) continue;
            state.SealKeys.Add(id);
            changed = true;
        }
        if (changed) Save(state);
        return changed;
    }

    public RunSummary LoadSummary() => new(Load());

    /// <summary>
    /// Applies a stats update in a single load/save. <paramref name="update"/> may change either
    /// game's stats (check counts for both games come from the shared slot).
    /// </summary>
    public void UpdateStats(Action<RunState> update)
    {
        if (update == null) throw new ArgumentNullException(nameof(update));
        RunState state = Load();
        update(state);
        Save(state);
    }

    public bool IsGoalCompleted(GameId game) => Load().GetGoalCompleted(game);

    public bool MarkGoalCompleted(GameId game)
    {
        RunState state = Load();
        if (state.GetGoalCompleted(game)) return false;
        state.SetGoalCompleted(game, true);
        GameStats stats = state.GetStats(game);
        if (stats.GoalAtSeconds <= 0)
            stats.GoalAtSeconds = state.GetStats(GameId.HollowKnight).PlaySeconds + state.GetStats(GameId.Silksong).PlaySeconds;
        Save(state);
        return true;
    }

    public bool IsHkGoalCompleted() => IsGoalCompleted(GameId.HollowKnight);

    public void MarkHkGoalCompleted() => MarkGoalCompleted(GameId.HollowKnight);
}

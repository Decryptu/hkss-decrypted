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

    /// <summary>Hollow Knight statistics for the end-of-run summary.</summary>
    [JsonProperty("hk_stats")]
    public GameStats HkStats { get; set; } = new();

    /// <summary>Silksong statistics for the end-of-run summary.</summary>
    [JsonProperty("ss_stats")]
    public GameStats SsStats { get; set; } = new();

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
        Save(state);
        return true;
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
        Save(state);
        return true;
    }

    public bool IsHkGoalCompleted() => IsGoalCompleted(GameId.HollowKnight);

    public void MarkHkGoalCompleted() => MarkGoalCompleted(GameId.HollowKnight);
}

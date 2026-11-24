using System;
using System.IO;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>
/// Gameplay progress shared across both games in a combo run. Stored separately from
/// <c>run.json</c> (which the launcher owns and rewrites on every game switch).
/// </summary>
public sealed class RunState
{
    /// <summary>
    /// True once the player has reached Bone Bottom in Silksong, opening its return gate
    /// and enabling town-to-town travel and town warping.
    /// </summary>
    [JsonProperty("bonetown_gate_open")]
    public bool BonetownGateOpen { get; set; }
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
}

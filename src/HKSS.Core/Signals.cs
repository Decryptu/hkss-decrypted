using System;
using System.IO;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>What a running game tells the launcher. Field names are the wire format.</summary>
public sealed class Signal
{
    public const string Booting = "booting";
    public const string Ready = "ready";
    public const string Travel = "travel";
    public const string Error = "error";
    /// <summary>Saved and waiting in memory for the launcher to pause it (instant travel).</summary>
    public const string Parked = "parked";
    /// <summary>Cover state: the launcher hides the game, which may now close (or park).</summary>
    public const string Cover = "cover";
    /// <summary>Cover state: the launcher woke this parked game and is waiting for it.</summary>
    public const string Resume = "resume";

    [JsonProperty("session")] public string Session { get; set; } = "";
    [JsonProperty("state")] public string State { get; set; } = "";
    [JsonProperty("to", NullValueHandling = NullValueHandling.Ignore)] public string? To { get; set; }
    [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)] public string? Detail { get; set; }
    /// <summary>On a cover: stay open, saved and still, instead of closing (instant travel).</summary>
    [JsonProperty("park", NullValueHandling = NullValueHandling.Ignore)] public bool? Park { get; set; }
}

/// <summary>One file per game in the run's signals folder, replaced atomically.</summary>
public sealed class SignalChannel
{
    private readonly RunContext _run;

    public SignalChannel(RunContext run) => _run = run;

    private string GameFile => Path.Combine(_run.SignalDir, _run.Game.Key() + ".json");
    private string CoverFile => Path.Combine(_run.SignalDir, "cover.json");

    public void Send(string state, GameId? to = null, string? detail = null)
    {
        Directory.CreateDirectory(_run.SignalDir);
        string json = JsonConvert.SerializeObject(new Signal
        {
            Session = _run.Session,
            State = state,
            To = to?.Key(),
            Detail = detail,
        });
        string temp = GameFile + ".new";
        File.WriteAllText(temp, json);
        if (File.Exists(GameFile)) File.Replace(temp, GameFile, null);
        else File.Move(temp, GameFile);
    }

    /// <summary>True once the launcher's travel screen hides this game, so it can close unseen.</summary>
    public bool CoverIsUp() => ReadCover()?.State == Signal.Cover;

    /// <summary>True when the launcher woke this game from instant travel.</summary>
    public bool ResumeRequested() => ReadCover()?.State == Signal.Resume;

    /// <summary>The launcher's cover file, if it was written for this launch of this game.</summary>
    public Signal? ReadCover()
    {
        try
        {
            if (!File.Exists(CoverFile)) return null;
            using var stream = new FileStream(
                CoverFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            Signal? cover = JsonConvert.DeserializeObject<Signal>(reader.ReadToEnd());
            return cover?.Session == _run.Session ? cover : null;
        }
        catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
        {
            return null;
        }
    }
}

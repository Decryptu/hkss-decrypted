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

    [JsonProperty("session")] public string Session { get; set; } = "";
    [JsonProperty("state")] public string State { get; set; } = "";
    [JsonProperty("to", NullValueHandling = NullValueHandling.Ignore)] public string? To { get; set; }
    [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)] public string? Detail { get; set; }
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
    public bool CoverIsUp()
    {
        try
        {
            if (!File.Exists(CoverFile)) return false;
            using var stream = new FileStream(
                CoverFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonConvert.DeserializeObject<Signal>(reader.ReadToEnd())?.Session == _run.Session;
        }
        catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }
}

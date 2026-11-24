using System;
using System.IO;

namespace HKSS.Core;

/// <summary>
/// The combo run a game was launched for. The launcher passes it through environment variables;
/// a game started without them must not continue, because its saves would not be isolated.
/// </summary>
public sealed class RunContext
{
    public const string RunDirVariable = "HKSS_RUN_DIR";
    public const string SessionVariable = "HKSS_SESSION";
    public const string MarkerFile = "run.json";

    /// <summary>Each game has exactly one save per run.</summary>
    public const int SaveSlot = 1;

    private RunContext(GameId game, string runDir, string session)
    {
        Game = game;
        RunDir = runDir;
        Session = session;
        SaveDir = Path.Combine(runDir, "saves", game.Key());
        SignalDir = Path.Combine(runDir, "signals");
    }

    public GameId Game { get; }
    public string RunDir { get; }
    public string SaveDir { get; }
    public string SignalDir { get; }

    /// <summary>Changes on every launch, so signals left by an earlier launch are never trusted.</summary>
    public string Session { get; }

    public static RunContext Create(GameId game, Func<string, string?> variable)
    {
        string? dir = variable(RunDirVariable);
        string? session = variable(SessionVariable);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(session))
            throw new InvalidOperationException("This game was not started by the HKSS Launcher.");
        if (!Path.IsPathRooted(dir))
            throw new InvalidOperationException($"{RunDirVariable} must be an absolute path.");
        string full = Path.GetFullPath(dir!);
        if (!File.Exists(Path.Combine(full, MarkerFile)))
            throw new InvalidOperationException($"{full} is not a combo run folder.");
        return new RunContext(game, full, session!);
    }

    /// <summary>True when <paramref name="path"/> is this game's save folder or inside it.</summary>
    public bool OwnsSavePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string full = Path.GetFullPath(path!).TrimEnd(Path.DirectorySeparatorChar);
        return full.Equals(SaveDir, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(SaveDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

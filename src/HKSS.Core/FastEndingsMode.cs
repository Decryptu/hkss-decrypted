using System;
using System.Collections.Generic;

namespace HKSS.Core;

/// <summary>
/// Controls how ending cinematics, credits, and completion screens are handled in both games.
/// </summary>
public enum FastEndingsMode
{
    /// <summary>Vanilla playback with no automatic skipping.</summary>
    Off = 0,
    /// <summary>Ending cinematics remain watchable (with 1-press skip unlocked); credits and completion screens auto-skip.</summary>
    Credits = 1,
    /// <summary>Ending cinematics, credits, and completion screens all auto-skip cleanly back to gameplay.</summary>
    All = 2,
}

/// <summary>
/// Shared scene classification and environment-variable parsing for fast ending and credits handling.
/// Explicitly excludes gameplay, Steel Soul permadeath, Act 3 awakening, and manual menu credits.
/// </summary>
public static class FastEndingsPolicy
{
    public const string EnvVariable = "HKSS_FAST_ENDINGS";

    private static readonly HashSet<string> EndingCinematicScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cinematic_MrMushroom",
        "GG_End_Sequence",
    };

    private static readonly HashSet<string> CreditsAndCompletionScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "End_Credits",
        "End_Credits_Scroll",
        "End_Game_Completion",
        "PermaDeath_Unlock",
        "BetaEnd",
    };

    public static FastEndingsMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return FastEndingsMode.Credits;
        return raw!.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "vanilla" or "false" or "0" => FastEndingsMode.Off,
            "all" or "endings" or "full" => FastEndingsMode.All,
            "credits" or "true" or "1" => FastEndingsMode.Credits,
            _ => FastEndingsMode.Credits,
        };
    }

    public static string ToKey(FastEndingsMode mode) => mode switch
    {
        FastEndingsMode.Off => "off",
        FastEndingsMode.All => "all",
        _ => "credits",
    };

    public static string Label(FastEndingsMode mode) => mode switch
    {
        FastEndingsMode.Off => "Off (vanilla)",
        FastEndingsMode.All => "Skip endings & credits",
        _ => "Skip credits",
    };

    public static FastEndingsMode Next(FastEndingsMode mode) => mode switch
    {
        FastEndingsMode.Credits => FastEndingsMode.All,
        FastEndingsMode.All => FastEndingsMode.Off,
        _ => FastEndingsMode.Credits,
    };

    public static bool IsEndingCinematicScene(string? sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return sceneName!.StartsWith("Cinematic_Ending_", StringComparison.OrdinalIgnoreCase)
            || EndingCinematicScenes.Contains(sceneName);
    }

    public static bool IsCreditsOrCompletionScene(string? sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return CreditsAndCompletionScenes.Contains(sceneName!);
    }

    /// <summary>True when returning to the title screen after this scene should auto-reload the run's save.</summary>
    public static bool IsReloadAfterEndingScene(string? sceneName) =>
        IsEndingCinematicScene(sceneName) || IsCreditsOrCompletionScene(sceneName);

    /// <summary>True when manual 1-press skip should be unlocked for this scene.</summary>
    public static bool ShouldUnlockManualSkip(FastEndingsMode mode, string? sceneName) =>
        mode != FastEndingsMode.Off && (IsEndingCinematicScene(sceneName) || IsCreditsOrCompletionScene(sceneName));

    /// <summary>True when this scene should automatically advance via the game's native skip flow.</summary>
    public static bool ShouldAutoSkip(FastEndingsMode mode, string? sceneName) => mode switch
    {
        FastEndingsMode.Credits => IsCreditsOrCompletionScene(sceneName),
        FastEndingsMode.All => IsEndingCinematicScene(sceneName) || IsCreditsOrCompletionScene(sceneName),
        _ => false,
    };
}

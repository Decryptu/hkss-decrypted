using System;

namespace HKSS.Core;

/// <summary>
/// Tracks per-game completion keys in Archipelago data storage and evaluates whether the
/// combined slot's configured goal is satisfied.
/// </summary>
public static class GoalTracker
{
    public const string GamesBoth = "both";
    public const string GamesHollowKnightOnly = "hollow_knight_only";
    public const string GamesSilksongOnly = "silksong_only";

    public const string ComboGoalBoth = "both";
    public const string ComboGoalEither = "either";

    public static string DataStorageKey(int team, int slot, GameId game) => game switch
    {
        GameId.HollowKnight => $"hkss_{team}_{slot}_goal_hk",
        GameId.Silksong => $"hkss_{team}_{slot}_goal_ss",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };

    public static bool IsComboGoalSatisfied(string? gamesMode, string? comboGoal, bool hkDone, bool ssDone)
    {
        if (string.Equals(gamesMode, GamesHollowKnightOnly, StringComparison.OrdinalIgnoreCase))
            return hkDone;
        if (string.Equals(gamesMode, GamesSilksongOnly, StringComparison.OrdinalIgnoreCase))
            return ssDone;
        if (string.Equals(comboGoal, ComboGoalEither, StringComparison.OrdinalIgnoreCase))
            return hkDone || ssDone;
        return hkDone && ssDone;
    }
}

using System;

namespace HKSS.Core;

public enum GameId
{
    HollowKnight,
    Silksong,
}

public static class GameIds
{
    /// <summary>The name used in folders, signals and the launcher.</summary>
    public static string Key(this GameId game) => game switch
    {
        GameId.HollowKnight => "hollow_knight",
        GameId.Silksong => "silksong",
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    public static GameId Other(this GameId game) =>
        game == GameId.HollowKnight ? GameId.Silksong : GameId.HollowKnight;
}

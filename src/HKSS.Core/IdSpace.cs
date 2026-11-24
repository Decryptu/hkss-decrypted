using System;

namespace HKSS.Core;

/// <summary>
/// ID blocks and name prefixes for the combined Archipelago world.
/// Mirrored from <c>apworld/hkss/ids.py</c>.
/// </summary>
public static class IdSpace
{
    public const string GameName = "Hollow Knight x Silksong";

    public const string HollowKnightPrefix = "HK ";
    public const string SilksongPrefix = "SS ";

    public const long ComboBlockStart = 84_000_000;
    public const long ComboBlockEnd = 84_099_999;

    public const long HollowKnightUpstreamBase = 0x1000000;
    public const long HollowKnightBlockStart = 84_100_000;
    public const long HollowKnightBlockEnd = 84_199_999;

    public const long SilksongUpstreamBase = 835_000;
    public const long SilksongBlockStart = 84_200_000;
    public const long SilksongBlockEnd = 84_299_999;

    public static bool IsHollowKnight(long combinedId) =>
        combinedId >= HollowKnightBlockStart && combinedId <= HollowKnightBlockEnd;

    public static bool IsSilksong(long combinedId) =>
        combinedId >= SilksongBlockStart && combinedId <= SilksongBlockEnd;

    public static bool IsCombo(long combinedId) =>
        combinedId >= ComboBlockStart && combinedId <= ComboBlockEnd;

    public static GameId? GameForId(long combinedId)
    {
        if (IsHollowKnight(combinedId)) return GameId.HollowKnight;
        if (IsSilksong(combinedId)) return GameId.Silksong;
        return null;
    }

    public static long ToCombined(GameId game, long upstreamId)
    {
        long mapped = game switch
        {
            GameId.HollowKnight => HollowKnightBlockStart + (upstreamId - HollowKnightUpstreamBase),
            GameId.Silksong => SilksongBlockStart + (upstreamId - SilksongUpstreamBase),
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
        if (GameForId(mapped) != game)
            throw new ArgumentOutOfRangeException(nameof(upstreamId), upstreamId,
                $"Upstream ID maps outside the {game} block ({mapped}).");
        return mapped;
    }

    public static long ToUpstream(GameId game, long combinedId) => game switch
    {
        GameId.HollowKnight when IsHollowKnight(combinedId) =>
            HollowKnightUpstreamBase + (combinedId - HollowKnightBlockStart),
        GameId.Silksong when IsSilksong(combinedId) =>
            SilksongUpstreamBase + (combinedId - SilksongBlockStart),
        _ => throw new ArgumentOutOfRangeException(nameof(combinedId), combinedId,
            $"ID is not in the {game} block."),
    };

    public static string StripPrefix(string name)
    {
        if (name.StartsWith(HollowKnightPrefix, StringComparison.Ordinal))
            return name.Substring(HollowKnightPrefix.Length);
        if (name.StartsWith(SilksongPrefix, StringComparison.Ordinal))
            return name.Substring(SilksongPrefix.Length);
        return name;
    }
}

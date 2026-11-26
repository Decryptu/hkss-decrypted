using System;
using System.Collections.Generic;

namespace HKSS.Core;

/// <summary>
/// A shared ability in the combo ID block that unlocks paired abilities in both games.
/// Mirrored from <c>apworld/hkss/cross_game_items.py</c>.
/// </summary>
public sealed class CrossGameItem
{
    public long Id { get; }
    public string Name { get; }
    public string HollowKnightItem { get; }
    public string SilksongItem { get; }

    public CrossGameItem(long id, string name, string hollowKnightItem, string silksongItem)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        HollowKnightItem = hollowKnightItem ?? throw new ArgumentNullException(nameof(hollowKnightItem));
        SilksongItem = silksongItem ?? throw new ArgumentNullException(nameof(silksongItem));
    }

    public string LocalItemName(GameId game) => game switch
    {
        GameId.HollowKnight => HollowKnightItem,
        GameId.Silksong => SilksongItem,
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
    };
}

/// <summary>
/// Registry of cross-game shared abilities in the combo ID block (<see cref="IdSpace.ComboBlockStart"/>).
/// Mirrored from <c>apworld/hkss/cross_game_items.py</c>.
/// </summary>
public static class CrossGameItems
{
    public const string SlotDataKey = "cross_game_items";

    public static readonly IReadOnlyList<CrossGameItem> All = new[]
    {
        new CrossGameItem(IdSpace.ComboBlockStart + 1, "Shared Dash", "Mothwing_Cloak", "Swift Step"),
        new CrossGameItem(IdSpace.ComboBlockStart + 2, "Shared Wall Jump", "Mantis_Claw", "Cling Grip"),
        new CrossGameItem(IdSpace.ComboBlockStart + 3, "Shared Double Jump", "Monarch_Wings", "Faydown Cloak"),
        new CrossGameItem(IdSpace.ComboBlockStart + 4, "Shared Swim", "Swim", "Swim"),
        new CrossGameItem(IdSpace.ComboBlockStart + 5, "Shared Dash (Left)", "Left_Mothwing_Cloak", "Swift Step (Left)"),
        new CrossGameItem(IdSpace.ComboBlockStart + 6, "Shared Dash (Right)", "Right_Mothwing_Cloak", "Swift Step (Right)"),
        new CrossGameItem(IdSpace.ComboBlockStart + 7, "Shared Wall Jump (Left)", "Left_Mantis_Claw", "Cling Grip (Left)"),
        new CrossGameItem(IdSpace.ComboBlockStart + 8, "Shared Wall Jump (Right)", "Right_Mantis_Claw", "Cling Grip (Right)"),
    };

    private static readonly Dictionary<long, CrossGameItem> ById;
    private static readonly Dictionary<string, CrossGameItem> ByName;

    static CrossGameItems()
    {
        ById = new Dictionary<long, CrossGameItem>(All.Count);
        ByName = new Dictionary<string, CrossGameItem>(All.Count, StringComparer.OrdinalIgnoreCase);
        foreach (CrossGameItem item in All)
        {
            ById[item.Id] = item;
            ByName[item.Name] = item;
        }
    }

    public static bool TryGet(long combinedId, out CrossGameItem? item) =>
        ById.TryGetValue(combinedId, out item);

    public static bool TryGetByName(string? name, out CrossGameItem? item)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            item = null;
            return false;
        }
        return ByName.TryGetValue(name!, out item);
    }

    /// <summary>Returns the local game item name for a cross-game item ID, or null if not a cross-game item.</summary>
    public static string? LocalItemName(GameId game, long combinedId) =>
        ById.TryGetValue(combinedId, out CrossGameItem? item) ? item.LocalItemName(game) : null;

    /// <summary>True when <paramref name="combinedId"/> belongs to <paramref name="game"/> or is a shared cross-game item.</summary>
    public static bool IsForGame(GameId game, long combinedId) =>
        IdSpace.GameForId(combinedId) == game || ById.ContainsKey(combinedId);
}

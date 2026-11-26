using System;

namespace HKSS.Core;

public enum RouteAction
{
    SkipAlreadyProcessed,
    ApplyLocally,
    CrossGameNotice,
    SkipOtherGameBacklog,
    IgnoreUnknown,
}

public readonly struct RoutedItem
{
    public RouteAction Action { get; }
    public int Index { get; }
    public long CombinedItemId { get; }
    public long? UpstreamItemId { get; }
    public GameId? TargetGame { get; }
    public string RawName { get; }
    public string DisplayName { get; }
    public string SenderName { get; }
    public string Message { get; }

    public RoutedItem(
        RouteAction action,
        int index,
        long combinedItemId,
        long? upstreamItemId,
        GameId? targetGame,
        string rawName,
        string displayName,
        string senderName,
        string message)
    {
        Action = action;
        Index = index;
        CombinedItemId = combinedItemId;
        UpstreamItemId = upstreamItemId;
        TargetGame = targetGame;
        RawName = rawName;
        DisplayName = displayName;
        SenderName = senderName;
        Message = message;
    }
}

/// <summary>
/// Routes incoming Archipelago items for a single shared slot to either the currently running
/// game or a cross-game notice for the other game, using <see cref="IdSpace"/>.
/// </summary>
public static class ItemRouter
{
    public static string WorldName(GameId game) => game switch
    {
        GameId.HollowKnight => "Hallownest",
        GameId.Silksong => "Pharloom",
        _ => game.ToString(),
    };

    public static RoutedItem Route(
        GameId thisGame,
        int itemIndex,
        int processedCount,
        long combinedItemId,
        string? itemName,
        string? senderName,
        bool isLiveReceipt)
    {
        string raw = string.IsNullOrWhiteSpace(itemName) ? $"Item #{combinedItemId}" : itemName!;
        string display = IdSpace.StripPrefix(raw);
        string sender = string.IsNullOrWhiteSpace(senderName) ? "Archipelago" : senderName!;
        bool isShared = CrossGameItems.TryGet(combinedItemId, out CrossGameItem? shared);
        GameId? owner = isShared ? thisGame : IdSpace.GameForId(combinedItemId);

        if (itemIndex < processedCount)
        {
            return new RoutedItem(
                RouteAction.SkipAlreadyProcessed,
                itemIndex,
                combinedItemId,
                !isShared && owner.HasValue ? IdSpace.ToUpstream(owner.Value, combinedItemId) : null,
                owner,
                raw,
                isShared ? shared!.Name : display,
                sender,
                string.Empty);
        }

        if (isShared)
        {
            string sharedName = shared!.Name;
            string msg = $"Received {sharedName} from {sender} (both worlds)";
            return new RoutedItem(
                RouteAction.ApplyLocally,
                itemIndex,
                combinedItemId,
                null,
                thisGame,
                raw,
                sharedName,
                sender,
                msg);
        }

        if (owner == thisGame)
        {
            long upstreamId = IdSpace.ToUpstream(thisGame, combinedItemId);
            string msg = $"Received {display} from {sender}";
            return new RoutedItem(
                RouteAction.ApplyLocally,
                itemIndex,
                combinedItemId,
                upstreamId,
                owner,
                raw,
                display,
                sender,
                msg);
        }

        if (owner.HasValue)
        {
            long upstreamId = IdSpace.ToUpstream(owner.Value, combinedItemId);
            if (!isLiveReceipt)
            {
                return new RoutedItem(
                    RouteAction.SkipOtherGameBacklog,
                    itemIndex,
                    combinedItemId,
                    upstreamId,
                    owner,
                    raw,
                    display,
                    sender,
                    string.Empty);
            }

            string msg = $"{display} sent to {WorldName(owner.Value)}";
            return new RoutedItem(
                RouteAction.CrossGameNotice,
                itemIndex,
                combinedItemId,
                upstreamId,
                owner,
                raw,
                display,
                sender,
                msg);
        }

        return new RoutedItem(
            RouteAction.IgnoreUnknown,
            itemIndex,
            combinedItemId,
            null,
            null,
            raw,
            display,
            sender,
            string.Empty);
    }
}

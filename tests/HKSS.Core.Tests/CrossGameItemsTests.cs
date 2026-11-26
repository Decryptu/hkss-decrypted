using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public class CrossGameItemsTests
{
    [Fact]
    public void AllEntriesLiveInComboBlockWithUniqueIdsAndNames()
    {
        Assert.NotEmpty(CrossGameItems.All);
        var ids = new System.Collections.Generic.HashSet<long>();
        var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (CrossGameItem item in CrossGameItems.All)
        {
            Assert.True(IdSpace.IsCombo(item.Id));
            Assert.True(ids.Add(item.Id));
            Assert.True(names.Add(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.HollowKnightItem));
            Assert.False(string.IsNullOrWhiteSpace(item.SilksongItem));
        }
    }

    [Fact]
    public void LookupAndIsForGameWorkForBothGames()
    {
        long dashId = IdSpace.ComboBlockStart + 1;
        Assert.True(CrossGameItems.TryGet(dashId, out CrossGameItem? dash));
        Assert.NotNull(dash);
        Assert.Equal("Shared Dash", dash!.Name);
        Assert.Equal("Mothwing_Cloak", CrossGameItems.LocalItemName(GameId.HollowKnight, dashId));
        Assert.Equal("Swift Step", CrossGameItems.LocalItemName(GameId.Silksong, dashId));

        Assert.True(CrossGameItems.IsForGame(GameId.HollowKnight, dashId));
        Assert.True(CrossGameItems.IsForGame(GameId.Silksong, dashId));
        Assert.False(CrossGameItems.IsForGame(GameId.HollowKnight, IdSpace.SilksongBlockStart + 1));
        Assert.True(CrossGameItems.IsForGame(GameId.Silksong, IdSpace.SilksongBlockStart + 1));
        Assert.False(CrossGameItems.TryGet(IdSpace.ComboBlockEnd, out _));
    }

    [Fact]
    public void ItemRouterAppliesSharedItemsLocallyInBothGames()
    {
        long dashId = IdSpace.ComboBlockStart + 1;

        RoutedItem hk = ItemRouter.Route(GameId.HollowKnight, 0, 0, dashId, "Shared Dash", "Alice", isLiveReceipt: true);
        Assert.Equal(RouteAction.ApplyLocally, hk.Action);
        Assert.Equal(GameId.HollowKnight, hk.TargetGame);
        Assert.Null(hk.UpstreamItemId);
        Assert.Equal("Shared Dash", hk.DisplayName);
        Assert.Contains("both worlds", hk.Message);

        RoutedItem ss = ItemRouter.Route(GameId.Silksong, 0, 0, dashId, "Shared Dash", "Alice", isLiveReceipt: false);
        Assert.Equal(RouteAction.ApplyLocally, ss.Action);
        Assert.Equal(GameId.Silksong, ss.TargetGame);
        Assert.Null(ss.UpstreamItemId);
        Assert.Equal("Shared Dash", ss.DisplayName);

        RoutedItem skipped = ItemRouter.Route(GameId.HollowKnight, 0, 1, dashId, "Shared Dash", "Alice", isLiveReceipt: true);
        Assert.Equal(RouteAction.SkipAlreadyProcessed, skipped.Action);
        Assert.Null(skipped.UpstreamItemId);
    }
}

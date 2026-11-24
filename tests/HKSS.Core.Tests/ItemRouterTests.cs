using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class ItemRouterTests
{
    private static readonly long HkMothwingCloak = IdSpace.ToCombined(GameId.HollowKnight, IdSpace.HollowKnightUpstreamBase + 10);
    private static readonly long SsSwiftStep = IdSpace.ToCombined(GameId.Silksong, IdSpace.SilksongUpstreamBase + 20);

    [Fact]
    public void AppliesOwnGameItemAndStripsPrefix()
    {
        RoutedItem routed = ItemRouter.Route(
            GameId.HollowKnight,
            itemIndex: 0,
            processedCount: 0,
            combinedItemId: HkMothwingCloak,
            itemName: "HK Mothwing_Cloak",
            senderName: "Alice",
            isLiveReceipt: true);

        Assert.Equal(RouteAction.ApplyLocally, routed.Action);
        Assert.Equal(IdSpace.HollowKnightUpstreamBase + 10, routed.UpstreamItemId);
        Assert.Equal("Mothwing_Cloak", routed.DisplayName);
        Assert.Contains("Mothwing_Cloak", routed.Message);
        Assert.Contains("Alice", routed.Message);
    }

    [Fact]
    public void LiveOtherGameItemProducesCrossGameNotice()
    {
        RoutedItem routed = ItemRouter.Route(
            GameId.HollowKnight,
            itemIndex: 1,
            processedCount: 1,
            combinedItemId: SsSwiftStep,
            itemName: "SS Swift Step",
            senderName: "Bob",
            isLiveReceipt: true);

        Assert.Equal(RouteAction.CrossGameNotice, routed.Action);
        Assert.Equal(GameId.Silksong, routed.TargetGame);
        Assert.Equal("Swift Step sent to Pharloom", routed.Message);
    }

    [Fact]
    public void BacklogOtherGameItemIsSkippedQuietly()
    {
        RoutedItem routed = ItemRouter.Route(
            GameId.Silksong,
            itemIndex: 0,
            processedCount: 0,
            combinedItemId: HkMothwingCloak,
            itemName: "HK Mothwing_Cloak",
            senderName: "Alice",
            isLiveReceipt: false);

        Assert.Equal(RouteAction.SkipOtherGameBacklog, routed.Action);
        Assert.Equal(string.Empty, routed.Message);
    }

    [Fact]
    public void AlreadyProcessedIndexIsSkipped()
    {
        RoutedItem routed = ItemRouter.Route(
            GameId.HollowKnight,
            itemIndex: 0,
            processedCount: 1,
            combinedItemId: HkMothwingCloak,
            itemName: "HK Mothwing_Cloak",
            senderName: "Alice",
            isLiveReceipt: false);

        Assert.Equal(RouteAction.SkipAlreadyProcessed, routed.Action);
    }

    [Fact]
    public void SilksongCatchesUpOnItemsFoundWhileInHollowKnight()
    {
        // While in HK, indices 0 (HK) and 1 (SS) arrived; HK processed both, SS processed 0.
        int ssProcessed = 0;

        RoutedItem first = ItemRouter.Route(GameId.Silksong, 0, ssProcessed, HkMothwingCloak, "HK Mothwing_Cloak", "Alice", isLiveReceipt: false);
        Assert.Equal(RouteAction.SkipOtherGameBacklog, first.Action);
        ssProcessed = 1;

        RoutedItem second = ItemRouter.Route(GameId.Silksong, 1, ssProcessed, SsSwiftStep, "SS Swift Step", "Alice", isLiveReceipt: false);
        Assert.Equal(RouteAction.ApplyLocally, second.Action);
        Assert.Equal("Swift Step", second.DisplayName);
        Assert.Equal(IdSpace.SilksongUpstreamBase + 20, second.UpstreamItemId);
    }
}

public sealed class GoalTrackerTests
{
    [Fact]
    public void DataStorageKeysFollowConvention()
    {
        Assert.Equal("hkss_0_3_goal_hk", GoalTracker.DataStorageKey(0, 3, GameId.HollowKnight));
        Assert.Equal("hkss_0_3_goal_ss", GoalTracker.DataStorageKey(0, 3, GameId.Silksong));
    }

    [Theory]
    [InlineData("both", "both", false, false, false)]
    [InlineData("both", "both", true, false, false)]
    [InlineData("both", "both", false, true, false)]
    [InlineData("both", "both", true, true, true)]
    [InlineData("both", "either", false, false, false)]
    [InlineData("both", "either", true, false, true)]
    [InlineData("both", "either", false, true, true)]
    [InlineData("hollow_knight_only", "both", true, false, true)]
    [InlineData("silksong_only", "both", false, true, true)]
    public void EvaluatesComboGoalModes(string games, string goal, bool hkDone, bool ssDone, bool expected)
    {
        Assert.Equal(expected, GoalTracker.IsComboGoalSatisfied(games, goal, hkDone, ssDone));
    }
}

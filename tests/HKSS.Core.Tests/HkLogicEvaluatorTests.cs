using System.Collections.Generic;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public class HkLogicEvaluatorTests
{
    [Theory]
    [InlineData(1, "Right_Mothwing_Cloak", HkReachability.Unreachable)]
    [InlineData(1, "Left_Mothwing_Cloak", HkReachability.Reachable)]
    [InlineData(0, "Vengeful_Spirit", HkReachability.Reachable)]
    public void CrossroadsSpikeGrub_RequiresLeftDashWhenCloakIsSplit(int split, string item, HkReachability expected)
    {
        HkLogicState state = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { item },
            Options = new Dictionary<string, int> { ["SplitMothwingCloak"] = split },
        });

        Assert.True(state.IsNodeReached("Crossroads_31[right1]"));
        Assert.Equal(expected, state.EvaluateLocation("Grub-Crossroads_Spike"));
    }

    [Fact]
    public void SealedScenes_BlockRoomsAndRoutesThroughThem()
    {
        string[] items = { "Vengeful_Spirit", "Mothwing_Cloak", "Mantis_Claw", "Lumafly_Lantern" };
        HkLogicState open = HkLogicEvaluator.Evaluate(new HkLogicInput { OwnedItems = items });
        Assert.True(open.IsNodeReached("Mines_01[left1]"));
        Assert.False(open.IsSceneSealed("Mines_01"));

        // Seal every Crystal Peak scene: its rooms, transitions and waypoints stay out of reach.
        HkLogicState sealedState = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = items,
            SealedScenes = new[] { "Mines_01", "Mines_02", "Mines_03", "Mines_04", "Mines_05", "Mines_06",
                "Mines_07", "Mines_10", "Mines_11", "Mines_13", "Mines_16", "Mines_17", "Mines_18",
                "Mines_19", "Mines_20", "Mines_23", "Mines_24", "Mines_25", "Mines_28", "Mines_29",
                "Mines_30", "Mines_31", "Mines_32", "Mines_33", "Mines_34", "Mines_35", "Mines_36", "Mines_37" },
        });
        Assert.False(sealedState.IsNodeReached("Mines_01[left1]"));
        Assert.False(sealedState.IsNodeReached("Mines_33[left1]"));
        Assert.True(sealedState.IsSceneSealed("Mines_31"));
        Assert.Equal(HkReachability.Unreachable, sealedState.EvaluateLocation("Crystal_Heart"));
        Assert.True(sealedState.IsNodeReached("Town"));
    }

    [Fact]
    public void FreshStart_ReachesCrossroadsBasics_BlocksDeepProgression()
    {
        HkLogicState state = HkLogicEvaluator.Evaluate(new HkLogicInput());

        Assert.True(state.IsNodeReached("Tutorial_01"));
        Assert.True(state.IsNodeReached("Town"));
        Assert.Equal(HkReachability.Reachable, state.EvaluateLocation("Vengeful_Spirit"));
        Assert.Equal(HkReachability.Reachable, state.EvaluateLocation("City_Crest"));

        // Mantis Claw requires reaching Fungal Wastes (needs Vengeful Spirit or movement)
        Assert.Equal(HkReachability.Unreachable, state.EvaluateLocation("Mantis_Claw"));
        // White Palace requires Awoken Dream Nail
        Assert.Equal(HkReachability.Unreachable, state.EvaluateLocation("King_Fragment"));
    }

    [Fact]
    public void AddingMovementAndLantern_UnlocksExpectedChecks()
    {
        HkLogicState withoutLantern = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Vengeful_Spirit", "Mothwing_Cloak", "Mantis_Claw" },
        });
        Assert.Equal(HkReachability.Reachable, withoutLantern.EvaluateLocation("Mantis_Claw"));
        // Crystal Heart requires Crystal Peak access (Lantern or Desolate Dive)
        Assert.Equal(HkReachability.Unreachable, withoutLantern.EvaluateLocation("Crystal_Heart"));

        HkLogicState withLantern = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Vengeful_Spirit", "Mothwing_Cloak", "Mantis_Claw", "Lumafly_Lantern" },
        });
        Assert.Equal(HkReachability.Reachable, withLantern.EvaluateLocation("Crystal_Heart"));
    }

    [Fact]
    public void DarkRoomsOption_BypassesLanternRequirement()
    {
        HkLogicState darkRoomsOff = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Vengeful_Spirit", "Mothwing_Cloak", "Mantis_Claw" },
            Options = new Dictionary<string, int> { ["DarkRooms"] = 0 },
        });
        Assert.Equal(HkReachability.Unreachable, darkRoomsOff.EvaluateLocation("Mask_Shard-Stone_Sanctuary"));

        HkLogicState darkRoomsOn = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Vengeful_Spirit", "Mothwing_Cloak", "Mantis_Claw" },
            Options = new Dictionary<string, int> { ["DarkRooms"] = 1 },
        });
        Assert.Equal(HkReachability.Reachable, darkRoomsOn.EvaluateLocation("Mask_Shard-Stone_Sanctuary"));
    }

    [Fact]
    public void ShopLocationCosts_GateGrubfatherAndExplainShortfall()
    {
        var costs = new Dictionary<string, Dictionary<string, int>>
        {
            ["Grubfather_1"] = new() { ["GRUBS"] = 5 },
            ["Iselda_1"] = new() { ["GEO"] = 250 },
        };

        HkLogicState fewGrubs = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Grub", "Grub" },
            LocationCosts = costs,
        });

        // Geo cost is ignored in logic; Iselda is reachable in Dirtmouth
        Assert.Equal(HkReachability.Reachable, fewGrubs.EvaluateLocation("Iselda_1"));
        // Grubfather_1 needs 5 grubs
        Assert.Equal(HkReachability.Unreachable, fewGrubs.EvaluateLocation("Grubfather_1"));
        var reasons = fewGrubs.ExplainBlocked("Grubfather_1");
        Assert.Contains(reasons, r => r.Contains("5 Grubs") && r.Contains("have 2"));

        HkLogicState enoughGrubs = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Grub", "Grub", "Grub", "Grub", "Grub" },
            LocationCosts = costs,
        });
        Assert.Equal(HkReachability.Reachable, enoughGrubs.EvaluateLocation("Grubfather_1"));
    }
}

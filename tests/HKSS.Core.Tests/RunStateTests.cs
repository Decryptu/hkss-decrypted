using System;
using System.IO;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class RunStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hkss-state-" + Guid.NewGuid().ToString("N"));

    public RunStateTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, RunContext.MarkerFile), "{\"current\":\"hollow_knight\"}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private RunContext MakeContext(GameId game) => RunContext.Create(game, name => name switch
    {
        RunContext.RunDirVariable => _dir,
        RunContext.SessionVariable => "test-session",
        _ => null,
    });

    [Fact]
    public void DefaultsToBonetownGateClosed()
    {
        var store = new RunStateStore(MakeContext(GameId.Silksong));
        RunState state = store.Load();
        Assert.False(state.BonetownGateOpen);
    }

    [Fact]
    public void MarkBonetownGateOpenPersistsAcrossGames()
    {
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));

        Assert.True(ssStore.MarkBonetownGateOpen());
        Assert.False(ssStore.MarkBonetownGateOpen());
        Assert.True(hkStore.Load().BonetownGateOpen);
    }

    [Fact]
    public void CorruptStateFileFallsBackToDefault()
    {
        File.WriteAllText(Path.Combine(_dir, RunStateStore.StateFileName), "{not-json");
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        Assert.False(store.Load().BonetownGateOpen);
    }

    [Fact]
    public void ProcessedItemCountsAreIndependentPerGame()
    {
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        Assert.Equal(0, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(0, store.GetProcessedItems(GameId.Silksong));

        store.SetProcessedItems(GameId.HollowKnight, 5);
        Assert.Equal(5, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(0, store.GetProcessedItems(GameId.Silksong));

        store.SetProcessedItems(GameId.Silksong, 3);
        Assert.Equal(5, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(3, store.GetProcessedItems(GameId.Silksong));
    }

    [Fact]
    public void RecordCheckedLocationPersistsAndRejectsOtherGame()
    {
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        long hkLoc = IdSpace.HollowKnightBlockStart + 1;
        long ssLoc = IdSpace.SilksongBlockStart + 1;

        Assert.True(store.RecordCheckedLocation(GameId.HollowKnight, hkLoc));
        Assert.False(store.RecordCheckedLocation(GameId.HollowKnight, hkLoc));
        Assert.Equal(new[] { hkLoc }, store.GetCheckedLocations(GameId.HollowKnight));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.RecordCheckedLocation(GameId.HollowKnight, ssLoc));
    }

    [Fact]
    public void EnsureSeedBindingPreservesInitialRunAndResetsOnSeedChange()
    {
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        long hkLoc = IdSpace.HollowKnightBlockStart + 10;
        store.SetProcessedItems(GameId.HollowKnight, 7);
        store.SetProcessedItems(GameId.Silksong, 4);
        store.RecordCheckedLocation(GameId.HollowKnight, hkLoc);
        store.MarkGoalCompleted(GameId.HollowKnight);
        store.MarkGoalCompleted(GameId.Silksong);

        // First bind attaches the seed without wiping existing progress.
        Assert.False(store.EnsureSeedBinding("seed-A|KnightHornet"));
        Assert.Equal(7, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(4, store.GetProcessedItems(GameId.Silksong));
        Assert.Single(store.GetCheckedLocations(GameId.HollowKnight));
        Assert.True(store.IsGoalCompleted(GameId.HollowKnight));
        Assert.True(store.IsGoalCompleted(GameId.Silksong));

        // Same seed is a no-op.
        Assert.False(store.EnsureSeedBinding("seed-A|KnightHornet"));
        Assert.Equal(7, store.GetProcessedItems(GameId.HollowKnight));

        // Different seed resets all AP counters, queued checks, and goal state.
        Assert.True(store.EnsureSeedBinding("seed-B|KnightHornet"));
        Assert.Equal(0, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(0, store.GetProcessedItems(GameId.Silksong));
        Assert.Empty(store.GetCheckedLocations(GameId.HollowKnight));
        Assert.False(store.IsGoalCompleted(GameId.HollowKnight));
        Assert.False(store.IsGoalCompleted(GameId.Silksong));
    }

    [Fact]
    public void GoalCompletionPersistsIndependentlyAcrossGames()
    {
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));

        Assert.False(hkStore.IsGoalCompleted(GameId.HollowKnight));
        Assert.False(hkStore.IsGoalCompleted(GameId.Silksong));

        Assert.True(ssStore.MarkGoalCompleted(GameId.Silksong));
        Assert.False(ssStore.MarkGoalCompleted(GameId.Silksong));
        Assert.True(hkStore.IsGoalCompleted(GameId.Silksong));
        Assert.False(hkStore.IsGoalCompleted(GameId.HollowKnight));

        Assert.True(hkStore.MarkGoalCompleted(GameId.HollowKnight));
        Assert.True(ssStore.IsGoalCompleted(GameId.HollowKnight));
    }

    [Fact]
    public void StatsPersistAcrossGamesAndSummarize()
    {
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));

        hkStore.UpdateStats(s =>
        {
            GameStats hk = s.GetStats(GameId.HollowKnight);
            hk.PlaySeconds = 3725.9;
            hk.Deaths = 4;
            hk.Kills = 100;
            hk.ChecksFound = 10;
            hk.ChecksTotal = 200;
        });
        ssStore.UpdateStats(s =>
        {
            GameStats ss = s.GetStats(GameId.Silksong);
            ss.PlaySeconds = 60;
            ss.Deaths = 1;
            ss.Kills = 5;
        });
        hkStore.MarkGoalCompleted(GameId.HollowKnight);

        RunSummary sum = ssStore.LoadSummary();
        Assert.Equal(4, sum.HollowKnight.Deaths);
        Assert.Equal(5, sum.TotalDeaths);
        Assert.Equal(105, sum.TotalKills);
        Assert.Equal(10, sum.TotalChecksFound);
        Assert.Equal(200, sum.TotalChecks);
        Assert.True(sum.HkGoalCompleted);
        Assert.False(sum.SsGoalCompleted);
        Assert.True(sum.Silksong.Played);
        Assert.Equal("1:03:05", RunSummary.FormatDuration(sum.TotalPlaySeconds));
        Assert.Equal("10 / 200", RunSummary.FormatChecks(10, 200));
        Assert.Equal("3", RunSummary.FormatChecks(3, 0));

        hkStore.EnsureSeedBinding("seed-A|x");
        Assert.True(hkStore.EnsureSeedBinding("seed-B|x"));
        Assert.False(hkStore.LoadSummary().HollowKnight.Played);
    }

    [Fact]
    public void ExtendedStatsDeriveRecordsAndFeats()
    {
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));

        hkStore.UpdateStats(s =>
        {
            GameStats hk = s.GetStats(GameId.HollowKnight);
            hk.PlaySeconds = 5000;
            hk.RecordDeath(1000, "DEEPNEST");
            hk.RecordDeath(1100, "DEEPNEST");
            hk.RecordDeath(4000, "GREEN_PATH");
            hk.Kills = 30;
            hk.Distance = 1534;
            hk.RoomsVisited.Add("Town");
            hk.SecondsByArea["CROSSROADS"] = 1200;
            s.Travels = 9;
        });
        ssStore.UpdateStats(s =>
        {
            GameStats ss = s.GetStats(GameId.Silksong);
            ss.PlaySeconds = 5000;
            ss.SecondsByArea["BONETOWN"] = 1800;
        });
        hkStore.MarkGoalCompleted(GameId.HollowKnight);
        ssStore.UpdateStats(s => s.GetStats(GameId.Silksong).PlaySeconds = 5600);
        ssStore.MarkGoalCompleted(GameId.Silksong);

        RunSummary sum = hkStore.LoadSummary();
        Assert.Equal(3, sum.HollowKnight.Deaths);
        Assert.Equal(2900, sum.HollowKnight.LongestLifeSeconds);
        Assert.Equal(5600, sum.LongestLifeSeconds); // Silksong is deathless so far
        Assert.Equal(10.0, sum.KillsPerDeath);
        Assert.Equal("Deepnest", sum.DeadliestArea?.Name);
        Assert.Equal(2, sum.DeadliestArea?.Value);
        Assert.Equal("Bone Bottom", sum.FavouriteArea?.Name);
        Assert.Equal(10000, sum.HollowKnight.GoalAtSeconds);
        Assert.Equal(600, sum.GoalGapSeconds);
        Assert.Equal(1, sum.TotalRoomsVisited);
        Assert.Equal("1.5 km", RunSummary.FormatDistance(sum.TotalDistance));
        Assert.Equal("Cog Core Test", RunSummary.AreaName(GameId.Silksong, "COG_CORE_TEST"));
        Assert.Contains(sum.Feats, f => f.Title == "Twin Crowns");
        Assert.Contains(sum.Feats, f => f.Title == "Perfect Balance");
        Assert.Contains(sum.Feats, f => f.Title == "Frequent Flier");
        Assert.Contains(sum.Feats, f => f.Title == "Swift Pilgrim");
        Assert.Equal("Knight and Hornet", sum.Epithet);
        string json = File.ReadAllText(Path.Combine(_dir, RunStateStore.StateFileName));
        Assert.Contains("\"epithet\": \"Knight and Hornet\"", json);
        Assert.Contains("\"title\": \"Twin Crowns\"", json);

        Assert.True(hkStore.EnsureSeedBinding("seed-B|x") || hkStore.EnsureSeedBinding("seed-C|x"));
        Assert.Equal(0, hkStore.LoadSummary().Travels);
    }

    [Fact]
    public void OldStateWithoutStatsLoadsEmptyStats()
    {
        File.WriteAllText(Path.Combine(_dir, RunStateStore.StateFileName), "{\"hk_goal_completed\": true, \"hk_stats\": null}");
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        RunSummary sum = store.LoadSummary();
        Assert.False(sum.HollowKnight.Played);
        Assert.Equal(0, sum.TotalDeaths);
    }

    [Fact]
    public void SealKeysPersistAcrossGamesAndResetOnSeedChange()
    {
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));
        hkStore.EnsureSeedBinding("seed-A|Slot");

        Assert.True(hkStore.RecordSealKeys(new[] { 84200100L }));
        Assert.False(hkStore.RecordSealKeys(new[] { 84200100L }));
        Assert.Equal(new[] { 84200100L }, ssStore.GetSealKeys());

        Assert.True(ssStore.EnsureSeedBinding("seed-B|Slot"));
        Assert.Empty(hkStore.GetSealKeys());
    }

    [Fact]
    public void DoubleSteelSoulDefeatPersistsAcrossGamesAndResetsOnSeedChange()
    {
        var hkStore = new RunStateStore(MakeContext(GameId.HollowKnight));
        var ssStore = new RunStateStore(MakeContext(GameId.Silksong));

        Assert.False(hkStore.IsDoubleSteelSoul());
        Assert.False(hkStore.IsSteelSoulDefeated());

        Assert.True(hkStore.SetDoubleSteelSoul(true));
        Assert.False(hkStore.SetDoubleSteelSoul(true));
        Assert.True(ssStore.IsDoubleSteelSoul());

        Assert.True(hkStore.MarkSteelSoulDefeated(GameId.HollowKnight, "Deepnest_01"));
        Assert.False(ssStore.MarkSteelSoulDefeated(GameId.Silksong, "Bone_01"));
        Assert.False(hkStore.SetDoubleSteelSoul(false));
        Assert.True(hkStore.IsDoubleSteelSoul());

        RunSummary sum = ssStore.LoadSummary();
        Assert.True(sum.DoubleSteelSoul);
        Assert.True(sum.SteelSoulDefeated);
        Assert.Equal("hollow_knight", sum.SteelSoulDefeatedGame);
        Assert.Equal("Deepnest_01", sum.SteelSoulDefeatedScene);
        Assert.Equal("Fallen, but not forgotten", sum.Epithet);

        hkStore.EnsureSeedBinding("seed-A|Slot");
        Assert.True(hkStore.IsSteelSoulDefeated());

        Assert.True(ssStore.EnsureSeedBinding("seed-B|Slot"));
        Assert.False(hkStore.IsDoubleSteelSoul());
        Assert.False(hkStore.IsSteelSoulDefeated());
        Assert.Equal(string.Empty, hkStore.Load().SteelSoulDefeatedGame);
        Assert.Equal(string.Empty, hkStore.Load().SteelSoulDefeatedScene);
    }
}

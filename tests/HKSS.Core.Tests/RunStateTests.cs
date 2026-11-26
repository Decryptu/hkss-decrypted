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
    public void OldStateWithoutStatsLoadsEmptyStats()
    {
        File.WriteAllText(Path.Combine(_dir, RunStateStore.StateFileName), "{\"hk_goal_completed\": true, \"hk_stats\": null}");
        var store = new RunStateStore(MakeContext(GameId.HollowKnight));
        RunSummary sum = store.LoadSummary();
        Assert.False(sum.HollowKnight.Played);
        Assert.Equal(0, sum.TotalDeaths);
    }
}

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
        store.MarkHkGoalCompleted();

        // First bind attaches the seed without wiping existing progress.
        Assert.False(store.EnsureSeedBinding("seed-A|KnightHornet"));
        Assert.Equal(7, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(4, store.GetProcessedItems(GameId.Silksong));
        Assert.Single(store.GetCheckedLocations(GameId.HollowKnight));
        Assert.True(store.IsHkGoalCompleted());

        // Same seed is a no-op.
        Assert.False(store.EnsureSeedBinding("seed-A|KnightHornet"));
        Assert.Equal(7, store.GetProcessedItems(GameId.HollowKnight));

        // Different seed resets all AP counters, queued checks, and goal state.
        Assert.True(store.EnsureSeedBinding("seed-B|KnightHornet"));
        Assert.Equal(0, store.GetProcessedItems(GameId.HollowKnight));
        Assert.Equal(0, store.GetProcessedItems(GameId.Silksong));
        Assert.Empty(store.GetCheckedLocations(GameId.HollowKnight));
        Assert.False(store.IsHkGoalCompleted());
    }
}

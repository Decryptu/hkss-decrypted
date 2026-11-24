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
}

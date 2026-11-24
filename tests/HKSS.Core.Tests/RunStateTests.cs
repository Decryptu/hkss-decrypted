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
}

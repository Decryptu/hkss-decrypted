using HKSS.Core;
using Xunit;

public sealed class RunTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hkss-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RunContext Run(GameId game = GameId.HollowKnight, string session = "s1")
    {
        File.WriteAllText(Path.Combine(_dir, RunContext.MarkerFile), "{}");
        return RunContext.Create(game, name => name == RunContext.RunDirVariable ? _dir : session);
    }

    [Fact]
    public void RefusesToRunWithoutTheLauncher() =>
        Assert.Throws<InvalidOperationException>(() => RunContext.Create(GameId.Silksong, _ => null));

    [Fact]
    public void RefusesAFolderThatIsNotARun() =>
        Assert.Throws<InvalidOperationException>(() => RunContext.Create(GameId.Silksong, _ => _dir));

    [Fact]
    public void RefusesARelativeRunFolder() =>
        Assert.Throws<InvalidOperationException>(() => RunContext.Create(GameId.Silksong, _ => "run"));

    [Fact]
    public void OwnsOnlyItsOwnGameSaves()
    {
        RunContext run = Run();
        Assert.True(run.OwnsSavePath(run.SaveDir));
        Assert.True(run.OwnsSavePath(Path.Combine(run.SaveDir, "hkss_user1.dat")));
        Assert.False(run.OwnsSavePath(Run(GameId.Silksong).SaveDir));
        Assert.False(run.OwnsSavePath(run.SaveDir + "_other"));
        Assert.False(run.OwnsSavePath(Path.Combine(run.SaveDir, "..", "silksong")));
        Assert.False(run.OwnsSavePath(null));
    }

    [Fact]
    public void SignalsReplaceEachOther()
    {
        RunContext run = Run();
        var channel = new SignalChannel(run);
        channel.Send(Signal.Ready);
        channel.Send(Signal.Travel, GameId.Silksong);
        Assert.Equal("""{"session":"s1","state":"travel","to":"silksong"}""",
            File.ReadAllText(Path.Combine(run.SignalDir, "hollow_knight.json")));
    }

    [Fact]
    public void IgnoresACoverFromAnEarlierLaunch()
    {
        RunContext run = Run();
        Directory.CreateDirectory(run.SignalDir);
        string cover = Path.Combine(run.SignalDir, "cover.json");
        File.WriteAllText(cover, """{"session":"old","state":"cover"}""");
        Assert.False(new SignalChannel(run).CoverIsUp());
        File.WriteAllText(cover, """{"session":"s1","state":"cover"}""");
        Assert.True(new SignalChannel(run).CoverIsUp());
    }
}

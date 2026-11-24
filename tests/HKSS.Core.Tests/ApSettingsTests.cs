using System;
using System.IO;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class ApSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hkss-ap-" + Guid.NewGuid().ToString("N"));

    public ApSettingsTests()
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
    public void SaveAndLoadRoundTripsAcrossGames()
    {
        var hkStore = new ApSettingsStore(MakeContext(GameId.HollowKnight));
        var ssStore = new ApSettingsStore(MakeContext(GameId.Silksong));

        hkStore.Save(new ApSettings
        {
            Host = "archipelago.gg",
            Port = 54321,
            Slot = "ComboRunner",
            Password = "secret",
            AutoConnect = true,
        });

        ApSettings loaded = ssStore.Load();
        Assert.True(loaded.IsConfigured);
        Assert.Equal("archipelago.gg", loaded.Host);
        Assert.Equal(54321, loaded.Port);
        Assert.Equal("ComboRunner", loaded.Slot);
        Assert.Equal("secret", loaded.Password);
    }

    [Fact]
    public void EnvOverridesTakePrecedenceOverFile()
    {
        var store = new ApSettingsStore(MakeContext(GameId.HollowKnight));
        store.Save(new ApSettings { Host = "saved.example", Port = 11111, Slot = "Saved" });

        ApSettings loaded = store.LoadWithEnvOverrides(name => name switch
        {
            ApSettingsStore.HostEnvVar => "localhost:38281",
            ApSettingsStore.SlotEnvVar => "EnvSlot",
            ApSettingsStore.PasswordEnvVar => "pw",
            _ => null,
        });

        Assert.Equal("localhost", loaded.Host);
        Assert.Equal(38281, loaded.Port);
        Assert.Equal("EnvSlot", loaded.Slot);
        Assert.Equal("pw", loaded.Password);
    }

    [Theory]
    [InlineData("localhost:38281", "localhost", 38281)]
    [InlineData("wss://archipelago.gg:44321", "wss://archipelago.gg", 44321)]
    [InlineData("archipelago.gg", "archipelago.gg", ApSettings.DefaultPort)]
    public void TryParseAddressHandlesHostAndPort(string input, string expectedHost, int expectedPort)
    {
        Assert.True(ApSettings.TryParseAddress(input, out string host, out int port));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }
}

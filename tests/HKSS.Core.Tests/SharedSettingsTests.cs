using System;
using System.IO;
using HKSS.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class SharedSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hkss-shared-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void MissingOrCorruptFileFallsBackToDefaults()
    {
        string path = Path.Combine(_dir, "launcher.json");
        SharedSettings missing = SharedSettings.Load(path);
        Assert.Equal(FastEndingsMode.Credits, missing.FastEndings);
        Assert.True(missing.BulkRelicTurnIn);
        Assert.True(missing.ShowMapLogic);
        Assert.False(missing.AutoQuill);
        Assert.False(missing.DamageNumbers);

        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{bad json");
        SharedSettings corrupt = SharedSettings.Load(path, "all");
        Assert.Equal(FastEndingsMode.All, corrupt.FastEndings);
        Assert.True(corrupt.BulkRelicTurnIn);
    }

    [Fact]
    public void SavePreservesLauncherOnlyFieldsAndRoundTrips()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "launcher.json");
        File.WriteAllText(path, """{"instant_travel": true, "fast_endings": "credits"}""");

        var updated = new SharedSettings
        {
            FastEndings = FastEndingsMode.All,
            BulkRelicTurnIn = false,
            AutoQuill = true,
            ShowMapLogic = false,
            ShowRecentItems = true,
            DamageNumbers = true,
            EnemyHealthBars = true,
            BossHealthBar = true,
        };
        Assert.True(SharedSettings.Save(path, updated));

        // Launcher-only field must remain untouched.
        JObject raw = JObject.Parse(File.ReadAllText(path));
        Assert.True(raw["instant_travel"]!.Value<bool>());
        Assert.Equal("all", raw["fast_endings"]!.Value<string>());

        SharedSettings loaded = SharedSettings.Load(path);
        Assert.Equal(FastEndingsMode.All, loaded.FastEndings);
        Assert.False(loaded.BulkRelicTurnIn);
        Assert.True(loaded.AutoQuill);
        Assert.False(loaded.ShowMapLogic);
        Assert.True(loaded.ShowRecentItems);
        Assert.True(loaded.DamageNumbers);
        Assert.True(loaded.EnemyHealthBars);
        Assert.True(loaded.BossHealthBar);
    }
}

using System;
using System.IO;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class SkinsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hkss-skins-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Touch(params string[] parts)
    {
        string path = Path.Combine(_root, Path.Combine(parts));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1 });
        return path;
    }

    [Fact]
    public void RootForRun_IsNextToRuns()
    {
        string run = Path.Combine(_root, "runs", "slot-2");
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "skins")), SkinLibrary.RootForRun(run));
    }

    [Fact]
    public void List_FindsKnightSkinsIncludingNestedArchives()
    {
        Touch("hollow_knight", "Red Knight", "Knight.png");
        Touch("hollow_knight", "Archive", "Inner", "Knight.png");
        Touch("hollow_knight", "Charms Only", "Charms", "Charm_1.png");
        Directory.CreateDirectory(Path.Combine(_root, "hollow_knight", "Empty"));

        var skins = SkinLibrary.List(_root, GameId.HollowKnight);

        Assert.Equal(new[] { "Archive", "Charms Only", "Red Knight" }, Ids(skins));
        Assert.EndsWith(Path.Combine("Archive", "Inner"), skins[0].ContentDir);
        Assert.Equal("Red Knight", skins[2].DisplayName);
    }

    [Fact]
    public void List_FindsPatchworkAndStitchworkPacks()
    {
        Touch("silksong", "Author-Crystal_Hornet", "plugins", "Stitchwork", "Sprites", "Knight", "atlas2", "idle0000.png");
        Touch("silksong", "Bare", "Spritesheets", "Knight", "atlas0.png");
        Touch("silksong", "Named", "Patchwork", "Sprites", "T2D", "health_pip.png");
        File.WriteAllText(Path.Combine(_root, "silksong", "Named", "Patchwork", "pack.json"), "{\"name\":\"Gold Hornet\"}");
        Touch("silksong", "NotAPack", "readme.txt");

        var skins = SkinLibrary.List(_root, GameId.Silksong);

        Assert.Equal(new[] { "Author-Crystal_Hornet", "Bare", "Named" }, Ids(skins));
        Assert.Equal("Crystal Hornet", skins[0].DisplayName);
        Assert.EndsWith("Stitchwork", skins[0].ContentDir);
        Assert.Equal("Gold Hornet", skins[2].DisplayName);
    }

    [Fact]
    public void PickRandom_AvoidsTheCurrentSkin()
    {
        Touch("hollow_knight", "A", "Knight.png");
        Touch("hollow_knight", "B", "Knight.png");
        var skins = SkinLibrary.List(_root, GameId.HollowKnight);
        var rng = new Random(1);

        for (int i = 0; i < 20; i++)
            Assert.Equal("B", SkinLibrary.PickRandom(skins, "a", rng));
        Assert.Equal(SkinLibrary.Vanilla, SkinLibrary.PickRandom(Array.Empty<SkinInfo>(), "A", rng));
        Assert.Equal("A", SkinLibrary.PickRandom(new[] { skins[0] }, "A", rng));
    }

    [Fact]
    public void PackIndex_ClassifiesEveryPath()
    {
        string content = Path.Combine(_root, "pack");
        string idle = Touch("pack", "Sprites", "Knight", "atlas2", "idle0000.png");
        string sheet = Touch("pack", "Spritesheets", "Knight", "atlas0.png");
        string pip = Touch("pack", "Sprites", "T2D", "Hud", "health_pip.png");
        string flat = Touch("pack", "Sprites", "T2D", "silk_orb.png");
        string t2dSheet = Touch("pack", "Spritesheets", "T2D", "Hud.png");
        Touch("pack", "Sprites", "stray.png");

        var index = SkinPackIndex.Load(content);

        Assert.True(index.Touches("knight"));
        Assert.False(index.Touches("Enemy"));
        Assert.Equal(idle, index.Sprites("Knight", "ATLAS2")!["Idle0000"]);
        Assert.Equal(sheet, index.Sheet("Knight", "atlas0"));
        Assert.Null(index.Sheet("Knight", "atlas2"));
        Assert.Equal(pip, index.T2DSprite("Hud", "health_pip"));
        Assert.Equal(flat, index.T2DSprite("Other", "silk_orb"));
        Assert.Equal(flat, index.T2DSprite(null, "silk_orb"));
        Assert.Null(index.T2DSprite("Hud", "missing"));
        Assert.Equal(t2dSheet, index.T2DSheet("hud"));
        Assert.Equal(1, index.SpriteCount);
        Assert.Equal(1, index.SheetCount);
        Assert.Equal(3, index.T2DCount);
    }

    private static string[] Ids(System.Collections.Generic.IReadOnlyList<SkinInfo> skins)
    {
        var ids = new string[skins.Count];
        for (int i = 0; i < skins.Count; i++) ids[i] = skins[i].Id;
        return ids;
    }
}

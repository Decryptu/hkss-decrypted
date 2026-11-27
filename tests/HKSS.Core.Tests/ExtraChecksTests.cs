using System.Linq;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class ExtraChecksTests
{
    [Fact]
    public void CatalogLoadsHollowKnightAndSilksongExtraChecksInValidIdBlocks()
    {
        Assert.NotEmpty(ExtraChecksCatalog.HollowKnightEntries);
        Assert.NotEmpty(ExtraChecksCatalog.SilksongEntries);

        Assert.Contains(ExtraChecksCatalog.HollowKnightEntries, e => e.Kind == ExtraCheckKind.Lever);
        Assert.Contains(ExtraChecksCatalog.HollowKnightEntries, e => e.Kind == ExtraCheckKind.BreakableWall);
        Assert.Contains(ExtraChecksCatalog.HollowKnightEntries, e => e.Kind == ExtraCheckKind.Grass);

        Assert.Contains(ExtraChecksCatalog.SilksongEntries, e => e.Kind == ExtraCheckKind.Lever);
        Assert.Contains(ExtraChecksCatalog.SilksongEntries, e => e.Kind == ExtraCheckKind.BreakableWall);
        Assert.Contains(ExtraChecksCatalog.SilksongEntries, e => e.Kind == ExtraCheckKind.Grass);

        foreach (ExtraCheckEntry entry in ExtraChecksCatalog.HollowKnightEntries)
        {
            Assert.True(IdSpace.IsHollowKnight(entry.Id), $"HK extra check {entry.Name} ID {entry.Id} out of HK block");
            Assert.True(ExtraChecksCatalog.TryGetById(entry.Id, out ExtraCheckEntry? byId) && byId == entry);
            Assert.True(ExtraChecksCatalog.TryGetHkByName(entry.Name, out ExtraCheckEntry? byName) && byName == entry);
        }

        foreach (ExtraCheckEntry entry in ExtraChecksCatalog.SilksongEntries)
        {
            Assert.True(IdSpace.IsSilksong(entry.Id), $"SS extra check {entry.Name} ID {entry.Id} out of SS block");
            Assert.True(ExtraChecksCatalog.TryGetById(entry.Id, out ExtraCheckEntry? byId) && byId == entry);
            Assert.True(ExtraChecksCatalog.TryGetSsByName(entry.Name, out ExtraCheckEntry? byName) && byName == entry);
        }
    }

    [Fact]
    public void ObjectAndGrassLookupMatchesEntries()
    {
        ExtraCheckEntry hkLever = ExtraChecksCatalog.HollowKnightEntries.First(e => e.Kind == ExtraCheckKind.Lever);
        ExtraCheckEntry? foundHkLever = ExtraChecksCatalog.FindObjectCheck(
            GameId.HollowKnight, hkLever.Scene, ExtraCheckKind.Lever, hkLever.ObjectName, hkLever.X + 0.1f, hkLever.Y - 0.1f);
        Assert.Equal(hkLever.Id, foundHkLever?.Id);

        ExtraCheckEntry hkGrass = ExtraChecksCatalog.HollowKnightEntries.First(e => e.Kind == ExtraCheckKind.Grass);
        ExtraCheckEntry? foundHkGrass = ExtraChecksCatalog.FindGrassCheck(GameId.HollowKnight, hkGrass.Scene);
        Assert.Equal(hkGrass.Id, foundHkGrass?.Id);

        ExtraCheckEntry ssLever = ExtraChecksCatalog.SilksongEntries.First(e => e.Kind == ExtraCheckKind.Lever);
        ExtraCheckEntry? foundSsLever = ExtraChecksCatalog.FindObjectCheck(
            GameId.Silksong, ssLever.Scene, ExtraCheckKind.Lever, ssLever.ObjectName, ssLever.X, ssLever.Y);
        Assert.Equal(ssLever.Id, foundSsLever?.Id);
    }

    [Fact]
    public void HkLogicEvaluatorEvaluatesExtraChecksAndRespectsNailAndQuake()
    {
        // Grass in Tutorial_01 is reachable from start with vanilla nail, but blocked when nail is randomized and missing.
        HkLogicState withNail = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = new[] { "Leftslash", "Rightslash", "Upslash", "Downslash" },
        });
        Assert.Equal(HkReachability.Reachable, withNail.EvaluateLocation("Grass-Tutorial_01"));

        HkLogicState withoutNail = HkLogicEvaluator.Evaluate(new HkLogicInput
        {
            OwnedItems = System.Array.Empty<string>(),
            Options = new System.Collections.Generic.Dictionary<string, int>
            {
                ["RandomizeNail"] = 1,
            },
        });
        Assert.Equal(HkReachability.Unreachable, withoutNail.EvaluateLocation("Grass-Tutorial_01"));

        // A quake floor requires Desolate Dive / QUAKE even when the room is reached.
        ExtraCheckEntry? quakeWall = ExtraChecksCatalog.HollowKnightEntries.FirstOrDefault(e => e.RequiresQuake);
        Assert.NotNull(quakeWall);
        Assert.Equal(HkReachability.Unreachable, withoutNail.EvaluateLocation(quakeWall!.Name));
    }
}

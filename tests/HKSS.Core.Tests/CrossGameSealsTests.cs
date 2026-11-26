using System.Collections.Generic;
using System.Linq;
using HKSS.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class CrossGameSealsTests
{
    private static Dictionary<string, object> SlotData(string json) => new()
    {
        [CrossGameSeals.SlotDataKey] = JArray.Parse(json),
    };

    private const string TwoSeals = @"[
        {""game"": ""hollow_knight"", ""area"": ""Deepnest"", ""scenes"": [""Deepnest_01"", ""Deepnest_02""],
         ""key"": ""SS Faydown Cloak"", ""key_id"": 84200100},
        {""game"": ""silksong"", ""area"": ""Shellwood"", ""scenes"": [""Shellwood_01""],
         ""key"": ""HK Mantis_Claw"", ""key_id"": 84100050}
    ]";

    [Fact]
    public void ParsesSealsFromSlotData()
    {
        CrossGameSeals seals = CrossGameSeals.Parse(SlotData(TwoSeals));

        Assert.Equal(2, seals.All.Count);
        CrossGameSeal deepnest = seals.For(GameId.HollowKnight).Single();
        Assert.Equal("Deepnest", deepnest.Area);
        Assert.Equal(84200100, deepnest.KeyId);
        Assert.True(deepnest.Contains("Deepnest_02"));
        Assert.Equal("Faydown Cloak", deepnest.KeyDisplayName);
        Assert.Equal("Pharloom", deepnest.KeyWorld);

        CrossGameSeal shellwood = seals.For(GameId.Silksong).Single();
        Assert.Equal("Mantis Claw", shellwood.KeyDisplayName);
        Assert.Equal("Hallownest", shellwood.KeyWorld);
        Assert.Equal("Shellwood needs Mantis Claw from Hallownest", shellwood.Describe());
    }

    [Fact]
    public void MissingOrMalformedSlotDataMeansNoSeals()
    {
        Assert.Empty(CrossGameSeals.Parse(null).All);
        Assert.Empty(CrossGameSeals.Parse(new Dictionary<string, object>()).All);
        Assert.Empty(CrossGameSeals.Parse(SlotData(@"[{""game"": ""zelda"", ""area"": ""x"", ""scenes"": [""a""], ""key"": ""k"", ""key_id"": 1}]")).All);
        Assert.Empty(CrossGameSeals.Parse(SlotData(@"[{""game"": ""silksong"", ""area"": ""x"", ""scenes"": [], ""key"": ""k"", ""key_id"": 1}]")).All);
        Assert.Empty(CrossGameSeals.Parse(SlotData(@"[{""game"": ""silksong"", ""area"": ""x"", ""scenes"": [""a""], ""key"": ""k""}]")).All);
    }

    [Fact]
    public void BlocksEnteringSealedAreaWithoutKey()
    {
        CrossGameSeals seals = CrossGameSeals.Parse(SlotData(TwoSeals));

        CrossGameSeal? blocked = seals.Blocking(GameId.HollowKnight, "Fungus2_25", "Deepnest_01", _ => false);
        Assert.NotNull(blocked);
        Assert.Equal("Deepnest", blocked!.Area);

        Assert.Null(seals.Blocking(GameId.HollowKnight, "Fungus2_25", "Deepnest_01", id => id == 84200100));
    }

    [Fact]
    public void AllowsMovesThatDoNotEnterASealedArea()
    {
        CrossGameSeals seals = CrossGameSeals.Parse(SlotData(TwoSeals));

        // Inside the area already (reached by a warp, for example): never trapped.
        Assert.Null(seals.Blocking(GameId.HollowKnight, "Deepnest_01", "Deepnest_02", _ => false));
        // Leaving the area, or a bounce back into the same scene.
        Assert.Null(seals.Blocking(GameId.HollowKnight, "Deepnest_01", "Fungus2_25", _ => false));
        Assert.Null(seals.Blocking(GameId.HollowKnight, "Deepnest_01", "Deepnest_01", _ => false));
        // A Silksong seal never applies in Hollow Knight, even with a matching scene name.
        Assert.Null(seals.Blocking(GameId.HollowKnight, "Town", "Shellwood_01", _ => false));
        Assert.NotNull(seals.Blocking(GameId.Silksong, "Bone_East_01", "Shellwood_01", _ => false));
    }
}

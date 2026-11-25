using System.Collections.Generic;
using HKSS.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class BenchAndLinkTests
{
    [Fact]
    public void BenchStore_RecordsDeduplicatesGroupsAndSerializes()
    {
        var store = new BenchStore(GameId.HollowKnight);

        Assert.False(store.RecordBench("Town", "HKSS_Gate_Respawn"));
        Assert.False(store.RecordBench("Tut_01", "Death Respawn Marker Init"));

        Assert.True(store.RecordBench("Town", "RestBench", 1, "TOWN"));
        Assert.False(store.RecordBench("Town", "RestBench", 1, "TOWN"));
        Assert.True(store.RecordBench("Ruins1_29", "RestBench", 1, "CITY"));
        Assert.True(store.RecordBench("Custom_Room_01", "Bench_Left", 1, null));

        Assert.Equal(3, store.Count);
        Assert.Equal("Custom_Room_01", store.LastBench?.SceneName);

        var groups = store.GetGroupedByArea("city");
        Assert.Single(groups);
        Assert.Equal("City of Tears", groups[0].Area);
        Assert.Equal("City Storerooms", groups[0].Benches[0].DisplayName);

        string json = store.Serialize();
        var reloaded = new BenchStore(GameId.HollowKnight);
        reloaded.LoadFromJson(json);
        Assert.Equal(3, reloaded.Count);
        Assert.Equal("Custom_Room_01", reloaded.LastBench?.SceneName);
    }

    [Fact]
    public void GiftProtocol_ClassifiesTraitsAndFallbackCleanly()
    {
        var geoGift = GiftProtocol.CreateGeoGift(0, 1, 2, 250);
        Assert.Equal((GiftEffectKind.Geo, 250), GiftProtocol.ClassifyGift(geoGift));

        var eggGift = GiftProtocol.CreateRancidEggGift(0, 1, 2);
        Assert.Equal((GiftEffectKind.RancidEgg, 1), GiftProtocol.ClassifyGift(eggGift));

        var healGift = new ApGift
        {
            ItemName = "Apple",
            Amount = 2,
            Traits = new List<ApGiftTrait> { new() { Trait = "Food" } },
        };
        Assert.Equal((GiftEffectKind.Heal, 2), GiftProtocol.ClassifyGift(healGift));

        var trapGift = new ApGift
        {
            ItemName = "Ice Trap",
            Amount = 1,
            Traits = new List<ApGiftTrait> { new() { Trait = "Trap" } },
        };
        Assert.Equal((GiftEffectKind.Trap, 1), GiftProtocol.ClassifyGift(trapGift));

        var unknownGift = new ApGift
        {
            ItemName = "Mysterious Widget",
            Amount = 1,
            ItemValue = 200,
        };
        Assert.Equal((GiftEffectKind.Geo, 100), GiftProtocol.ClassifyGift(unknownGift));
    }

    [Fact]
    public void LinkMath_ParsesKnockbackPacketAndClampsPools()
    {
        Assert.Equal(99, LinkMath.ComputePredictedPool(90, 25, 0, LinkMath.HkBaseSoulCapacity));
        Assert.Equal(0, LinkMath.ComputePredictedPool(10, -50, 0, LinkMath.HkBaseSoulCapacity));

        var data = new Dictionary<string, JToken>
        {
            ["source"] = "Hornet",
            ["uuid"] = "abc-123",
            ["time"] = 1700000000.5,
            ["value"] = new JObject { ["x"] = 30.0, ["y"] = 40.0, ["z"] = 0.0 },
        };

        bool ok = LinkMath.TryParseKnockbackPacket(
            new[] { LinkMath.KnockbackLinkTag },
            data,
            out float dx,
            out float dy,
            out string sender,
            out string? uuid,
            out string key);

        Assert.True(ok);
        Assert.Equal("Hornet", sender);
        Assert.Equal("abc-123", uuid);
        Assert.InRange(dx, 0.59f, 0.61f);
        Assert.InRange(dy, 0.79f, 0.81f);
        Assert.Contains("Hornet", key);
    }
}

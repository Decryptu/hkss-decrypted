using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace HKSS.Core;

public enum GiftEffectKind
{
    Geo,
    Heal,
    Soul,
    RancidEgg,
    Trap,
}

public sealed class ApGiftTrait
{
    [JsonProperty("trait")]
    public string Trait { get; set; } = string.Empty;

    [JsonProperty("quality")]
    public double Quality { get; set; } = 1.0;

    [JsonProperty("duration")]
    public double Duration { get; set; } = 1.0;
}

public sealed class ApGift
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("item_name")]
    public string ItemName { get; set; } = string.Empty;

    [JsonProperty("amount")]
    public int Amount { get; set; } = 1;

    [JsonProperty("item_value")]
    public long ItemValue { get; set; }

    [JsonProperty("traits")]
    public List<ApGiftTrait> Traits { get; set; } = new();

    [JsonProperty("sender_slot")]
    public int SenderSlot { get; set; }

    [JsonProperty("receiver_slot")]
    public int ReceiverSlot { get; set; }

    [JsonProperty("sender_team")]
    public int SenderTeam { get; set; }

    [JsonProperty("receiver_team")]
    public int ReceiverTeam { get; set; }

    [JsonProperty("is_refund")]
    public bool IsRefund { get; set; }
}

public sealed class ApGiftBoxMetadata
{
    [JsonProperty("is_open")]
    public bool IsOpen { get; set; }

    [JsonProperty("accepts_any_gift")]
    public bool AcceptsAnyGift { get; set; } = true;

    [JsonProperty("desired_traits")]
    public List<string> DesiredTraits { get; set; } = new();

    [JsonProperty("minimum_gift_data_version")]
    public int MinimumGiftDataVersion { get; set; } = 2;

    [JsonProperty("maximum_gift_data_version")]
    public int MaximumGiftDataVersion { get; set; } = 3;
}

/// <summary>
/// Archipelago Gifting API v2/v3 helpers for Hollow Knight.
/// </summary>
public static class GiftProtocol
{
    public static string GiftBoxesKey(int team) => $"GiftBoxes;{team}";
    public static string GiftBoxKey(int team, int slot) => $"GiftBox;{team};{slot}";

    public static ApGiftBoxMetadata CreateDefaultMetadata(bool isOpen) => new()
    {
        IsOpen = isOpen,
        AcceptsAnyGift = true,
        DesiredTraits = new List<string>
        {
            "Currency",
            "Geo",
            "Money",
            "Consumable",
            "Heal",
            "Health",
            "Soul",
            "Mana",
            "Egg",
            "Trap",
        },
        MinimumGiftDataVersion = 2,
        MaximumGiftDataVersion = 3,
    };

    public static (GiftEffectKind Kind, int Magnitude) ClassifyGift(ApGift gift)
    {
        if (gift == null) return (GiftEffectKind.Geo, 50);
        int amount = Math.Max(1, gift.Amount);

        bool HasTrait(params string[] names)
        {
            if (gift.Traits != null)
            {
                foreach (ApGiftTrait t in gift.Traits)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.Trait)) continue;
                    foreach (string n in names)
                    {
                        if (string.Equals(t.Trait, n, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            string item = gift.ItemName ?? string.Empty;
            foreach (string n in names)
            {
                if (item.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        if (HasTrait("Trap", "Damage", "Poison", "Ice", "Bomb", "Slowness"))
            return (GiftEffectKind.Trap, Math.Min(amount, 3));

        if (HasTrait("Egg", "RancidEgg", "Rancid Egg"))
            return (GiftEffectKind.RancidEgg, Math.Min(amount, 10));

        if (HasTrait("Heal", "Health", "Life", "Food", "Drink", "Fruit", "Vegetable", "Meat", "Candy"))
            return (GiftEffectKind.Heal, Math.Min(amount, 5));

        if (HasTrait("Soul", "Mana", "Magic", "Energy", "Silk"))
            return (GiftEffectKind.Soul, Math.Min(amount * 33, 99));

        if (HasTrait("Currency", "Geo", "Money", "Coin", "Gold", "Rosary", "Rupee"))
        {
            long raw = gift.ItemValue > 0 ? gift.ItemValue : amount * 50L;
            int geo = (int)Math.Max(10, Math.Min(5000, raw));
            return (GiftEffectKind.Geo, geo);
        }

        // Generic fallback: convert item_value or amount into Geo so no gift is wasted.
        long fallbackRaw = gift.ItemValue > 0 ? gift.ItemValue / 2 : amount * 50L;
        int fallbackGeo = (int)Math.Max(25, Math.Min(500, fallbackRaw));
        return (GiftEffectKind.Geo, fallbackGeo);
    }

    public static ApGift CreateGeoGift(int team, int senderSlot, int receiverSlot, int geoAmount) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ItemName = $"{geoAmount} Geo",
        Amount = 1,
        ItemValue = geoAmount,
        Traits = new List<ApGiftTrait>
        {
            new() { Trait = "Currency", Quality = 1.0, Duration = 1.0 },
            new() { Trait = "Money", Quality = 1.0, Duration = 1.0 },
            new() { Trait = "Geo", Quality = 1.0, Duration = 1.0 },
        },
        SenderTeam = team,
        SenderSlot = senderSlot,
        ReceiverTeam = team,
        ReceiverSlot = receiverSlot,
        IsRefund = false,
    };

    public static ApGift CreateRancidEggGift(int team, int senderSlot, int receiverSlot) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ItemName = "Rancid Egg",
        Amount = 1,
        ItemValue = 80,
        Traits = new List<ApGiftTrait>
        {
            new() { Trait = "Egg", Quality = 1.0, Duration = 1.0 },
            new() { Trait = "Consumable", Quality = 1.0, Duration = 1.0 },
            new() { Trait = "Food", Quality = 0.5, Duration = 1.0 },
        },
        SenderTeam = team,
        SenderSlot = senderSlot,
        ReceiverTeam = team,
        ReceiverSlot = receiverSlot,
        IsRefund = false,
    };
}

// Ported from ArchipelagoMW/Archipelago.HollowKnight, IC/{ArchipelagoItem,ArchipelagoTags,ArchipelagoUIDef,CostFactory,ItemFactory,RemotePlacement}.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.Internal;
using ItemChanger.Items;
using ItemChanger.Placements;
using ItemChanger.Tags;
using ItemChanger.UIDefs;
using Newtonsoft.Json;

namespace HKSS.HollowKnight;

public sealed class HkUIDef : MsgUIDef
{
    public static string GetSentItemName(AbstractItem item) => item.name switch
    {
        ItemNames.Grub => "A grub!",
        ItemNames.Grimmkin_Flame => "Grimmkin Flame",
        ItemNames.Rancid_Egg => "Rancid Egg",
        _ => item.UIDef?.GetPostviewName() ?? item.name,
    };

    public static HkUIDef CreateForReceivedItem(AbstractItem item, string sender)
    {
        UIDef? source = item.GetResolvedUIDef();
        var result = new HkUIDef(source);
        result.name = new BoxedString($"{source?.GetPostviewName() ?? item.name} from {sender}");
        return result;
    }

    public static HkUIDef CreateForSentItem(AbstractItem item, string recipient)
    {
        var result = new HkUIDef(item.UIDef);
        result.name = new BoxedString($"{recipient}'s {GetSentItemName(item)}");
        return result;
    }

    public HkUIDef() : base() { }

    public HkUIDef(UIDef? source) : base()
    {
        if (source is MsgUIDef msgDef)
        {
            shopDesc = msgDef.shopDesc?.Clone();
            sprite = msgDef.sprite?.Clone() ?? new EmptySprite();
        }
        else
        {
            shopDesc = new BoxedString(source?.GetShopDesc() ?? string.Empty);
            sprite = new EmptySprite();
        }
    }
}

public sealed class HkDummyItem : AbstractItem
{
    public string PreferredContainerType { get; set; } = Container.Unknown;

    public override string GetPreferredContainer() => PreferredContainerType;

    public HkDummyItem() { }

    public HkDummyItem(AbstractItem source)
    {
        name = source.name;
        UIDef = source.UIDef?.Clone();
        PreferredContainerType = source.GetPreferredContainer();
    }

    public override bool GiveEarly(string containerType) => containerType switch
    {
        Container.Unknown or Container.Shiny or Container.Chest => false,
        _ => true,
    };

    public override void GiveImmediate(GiveInfo info) { }
}

public sealed class HkRemoteItem : AbstractItem
{
    public HkRemoteItem() { }

    public HkRemoteItem(string itemName, string? recipientName = null, ItemFlags itemFlags = 0, bool isPharloom = false)
    {
        string desc = isPharloom
            ? "An artifact bound for Pharloom. It will be waiting when you cross the gate."
            : itemFlags.HasFlag(ItemFlags.Advancement)
                ? "This otherworldly artifact looks very important. Somebody probably really needs it."
                : itemFlags.HasFlag(ItemFlags.NeverExclude)
                    ? "This otherworldly artifact looks like it might be useful to someone."
                    : "A strange artifact from another world.";

        if (itemFlags.HasFlag(ItemFlags.Trap))
            desc += " Seems kinda suspicious though.";

        name = itemName;
        string display = isPharloom
            ? $"{itemName} (Pharloom)"
            : string.IsNullOrEmpty(recipientName) ? itemName : $"{recipientName}'s {itemName}";

        UIDef = new HkUIDef
        {
            name = new BoxedString(display),
            shopDesc = new BoxedString(desc),
            sprite = new EmptySprite(),
        };
    }

    public override void GiveImmediate(GiveInfo info) { }
}

public sealed class HkItemTag : Tag
{
    public long Location { get; set; }
    public int Player { get; set; }
    public ItemFlags Flags { get; set; }
    public bool IsItemForMe { get; set; }

    public void ReadItemInfo(ScoutedItemInfo itemInfo, bool isLocalHkItem)
    {
        Location = itemInfo.LocationId;
        Player = itemInfo.Player;
        Flags = itemInfo.Flags;
        IsItemForMe = isLocalHkItem;
    }

    public override void Load(object parent)
    {
        base.Load(parent);
        var item = (AbstractItem)parent;
        item.AfterGive += OnAfterGive;
        if (item.WasEverObtained() && Location > 0)
        {
            ApManager.Instance?.ReportCheckById(Location);
        }
    }

    public override void Unload(object parent)
    {
        ((AbstractItem)parent).AfterGive -= OnAfterGive;
        base.Unload(parent);
    }

    private void OnAfterGive(ReadOnlyGiveEventArgs args)
    {
        if (Location > 0)
        {
            ApManager.Instance?.ReportCheckById(Location);
        }
    }
}

public sealed class HkPlacementTag : Tag
{
    public static readonly Dictionary<long, AbstractPlacement> PlacementsByLocationId = new();

    public override void Load(object parent)
    {
        base.Load(parent);
        var pmt = (AbstractPlacement)parent;
        foreach (long locId in GetLocationIds(pmt))
        {
            PlacementsByLocationId[locId] = pmt;
        }
    }

    public override void Unload(object parent)
    {
        var pmt = (AbstractPlacement)parent;
        foreach (long locId in GetLocationIds(pmt))
        {
            PlacementsByLocationId.Remove(locId);
        }
        base.Unload(parent);
    }

    public static IEnumerable<long> GetLocationIds(AbstractPlacement pmt)
    {
        foreach (AbstractItem item in pmt.Items)
        {
            var tag = item.GetTag<HkItemTag>();
            if (tag != null && tag.Location > 0)
                yield return tag.Location;
        }
    }
}

public sealed class HkRemotePlacement : AbstractPlacement
{
    public const string SingletonName = "HKSS_Remote_Items";

    [JsonConstructor]
    private HkRemotePlacement(string Name) : base(SingletonName) { }

    public static HkRemotePlacement GetOrAddSingleton()
    {
        if (!Ref.Settings.Placements.TryGetValue(SingletonName, out AbstractPlacement pmt))
        {
            pmt = new HkRemotePlacement(SingletonName);
            pmt.AddTag<CompletionWeightTag>().Weight = 0;
            ItemChangerMod.AddPlacements(pmt.Yield());
        }
        return (HkRemotePlacement)pmt;
    }

    protected override void OnLoad() { }
    protected override void OnUnload() { }
}

internal sealed class HkItemFactory
{
    public AbstractItem CreateLocalHkItem(string rawItemName, ScoutedItemInfo itemInfo)
    {
        AbstractItem? item = Finder.GetItem(rawItemName);
        if (item == null)
        {
            HkssPlugin.Log.LogWarning($"[HK] Unknown ItemChanger item '{rawItemName}' (ID {itemInfo.ItemId}); using remote placeholder.");
            item = new HkRemoteItem(rawItemName, null, itemInfo.Flags);
        }

        AddTag(item, itemInfo, isLocalHkItem: true);
        return item;
    }

    public AbstractItem CreateRemoteOrCrossGameItem(
        AbstractPlacement targetPlacement,
        string? recipientName,
        string rawItemName,
        ScoutedItemInfo itemInfo,
        bool isOwnSilksongItem)
    {
        AbstractItem item;
        if (isOwnSilksongItem)
        {
            item = new HkRemoteItem(rawItemName, "Pharloom", itemInfo.Flags, isPharloom: true);
        }
        else if ((itemInfo.ItemGame == "Hollow Knight" || itemInfo.ItemGame == IdSpace.GameName)
            && IdSpace.IsHollowKnight(itemInfo.ItemId)
            && Finder.GetItem(rawItemName) is AbstractItem orig)
        {
            item = new HkDummyItem(orig)
            {
                UIDef = HkUIDef.CreateForSentItem(orig, recipientName ?? "Player"),
            };
            if (orig is SoulTotemItem totem)
                targetPlacement.GetOrAddTag<SoulTotemSubtypeTag>().Type = totem.soulTotemSubtype;
            else if (orig is GeoRockItem rock)
                targetPlacement.GetOrAddTag<GeoRockSubtypeTag>().Type = rock.geoRockSubtype;
        }
        else
        {
            item = new HkRemoteItem(rawItemName, recipientName, itemInfo.Flags);
        }

        AddTag(item, itemInfo, isLocalHkItem: false);
        return item;
    }

    private static void AddTag(AbstractItem item, ScoutedItemInfo itemInfo, bool isLocalHkItem)
    {
        item.AddTag<HkItemTag>().ReadItemInfo(itemInfo, isLocalHkItem);
    }
}

internal sealed class HkCostFactory
{
    private readonly Dictionary<string, Dictionary<string, int>> _locationCosts;

    public HkCostFactory(Dictionary<string, Dictionary<string, int>>? locationCosts)
    {
        _locationCosts = locationCosts ?? new Dictionary<string, Dictionary<string, int>>();
    }

    public void ApplyCost(AbstractPlacement pmt, AbstractItem item, string rawLocationName)
    {
        if (!_locationCosts.TryGetValue(rawLocationName, out Dictionary<string, int>? costs) || costs == null)
            return;

        var icCosts = new List<Cost>();
        foreach (var entry in costs)
        {
            Cost? proposed = entry.Key switch
            {
                "GEO" => Cost.NewGeoCost(entry.Value),
                "ESSENCE" => Cost.NewEssenceCost(entry.Value),
                "GRUBS" => Cost.NewGrubCost(entry.Value),
                "CHARMS" => new PDIntCost(entry.Value, nameof(PlayerData.charmsOwned),
                    $"Acquire {entry.Value} {(entry.Value == 1 ? "charm" : "charms")}"),
                "RANCIDEGGS" => new ItemChanger.Modules.CumulativeRancidEggCost(entry.Value),
                _ => null,
            };

            if (proposed == null)
            {
                HkssPlugin.Log.LogWarning($"[HK] Unknown currency type '{entry.Key}' at '{rawLocationName}'.");
                continue;
            }

            IEnumerable<Cost> inherent = pmt.GetPlacementAndLocationTags()
                .OfType<ImplicitCostTag>()
                .Where(t => t.Inherent)
                .Select(t => t.Cost);

            if (!inherent.Any(c => c.Includes(proposed)))
            {
                icCosts.Add(proposed);
            }
        }

        if (icCosts.Count == 0) return;

        Cost finalCost = icCosts.Count == 1 ? icCosts[0] : new MultiCost(icCosts);
        if (pmt is ISingleCostPlacement scp)
        {
            scp.Cost = scp.Cost == null ? finalCost : new MultiCost(scp.Cost, finalCost);
        }
        else
        {
            item.AddTag<CostTag>().Cost = finalCost;
        }
    }
}

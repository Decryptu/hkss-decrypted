// Ported from ArchipelagoMW/Archipelago.HollowKnight, ArchipelagoRandomizer.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.Modules;
using ItemChanger.Placements;
using ItemChanger.Tags;

namespace HKSS.HollowKnight;

/// <summary>
/// Builds the ItemChanger profile for a Hollow Knight x Silksong seed from slot_data["hollow_knight"]
/// and the scouted Hollow Knight locations in the Archipelago session.
/// </summary>
internal sealed class HkRandomizer
{
    private static readonly string[] ShopLocationNames =
    {
        LocationNames.Sly,
        LocationNames.Sly_Key,
        LocationNames.Iselda,
        LocationNames.Salubra,
        LocationNames.Leg_Eater,
        LocationNames.Grubfather,
        LocationNames.Seer,
    };

    private static readonly string[] ShopSuffixPrefixes =
    {
        LocationNames.Sly_Key,
        LocationNames.Sly,
        LocationNames.Iselda,
        LocationNames.Salubra,
        LocationNames.Leg_Eater,
        LocationNames.Egg_Shop,
        LocationNames.Seer,
        LocationNames.Grubfather,
    };

    private readonly HkSlotData _slotData;
    private readonly ArchipelagoSession _session;
    private readonly HkItemFactory _itemFactory = new();
    private readonly HkCostFactory _costFactory;
    private readonly Dictionary<string, AbstractPlacement> _placements = new(StringComparer.Ordinal);

    public HkRandomizer(HkSlotData slotData, ArchipelagoSession session)
    {
        _slotData = slotData;
        _session = session;
        _costFactory = new HkCostFactory(slotData.LocationCosts);
    }

    public int Randomize(long[] hkLocationIds)
    {
        ItemChangerMod.CreateSettingsProfile(overwrite: true);

        ApplyStartLocation();
        AddModules();
        AddHelperPlatforms();
        ApplyTransitionOverrides();
        ApplyCharmCosts();
        InitializeDefaultShops();

        int placedCount = 0;
        if (hkLocationIds.Length > 0)
        {
            Task<Dictionary<long, ScoutedItemInfo>> scoutTask = _session.Locations.ScoutLocationsAsync(hkLocationIds);
            if (!scoutTask.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Timed out scouting Hollow Knight locations from Archipelago.");
            }

            foreach (var kv in scoutTask.Result)
            {
                ScoutedItemInfo itemInfo = kv.Value;
                if (itemInfo == null) continue;
                string rawLocation = IdSpace.StripPrefix(itemInfo.LocationName ?? string.Empty);
                string rawItem = IdSpace.StripPrefix(itemInfo.ItemName ?? $"?Item {itemInfo.ItemId}");
                if (PlaceItem(rawLocation, rawItem, itemInfo))
                {
                    placedCount++;
                }
            }
        }

        ItemChangerMod.AddPlacements(_placements.Values);
        HkssPlugin.Log.LogInfo($"[HK] Randomized {placedCount} items across {_placements.Count} ItemChanger placements.");
        return placedCount;
    }

    private void ApplyStartLocation()
    {
        string? start = _slotData.Options.StartLocationName;
        if (string.IsNullOrWhiteSpace(start) || string.Equals(start, HkStartLocationNames.KingsPass, StringComparison.Ordinal))
        {
            return;
        }

        if (HkStartDef.Lookup.TryGetValue(start, out HkStartDef? def) && def != null)
        {
            ItemChangerMod.ChangeStartGame(def.ToItemChangerStartDef());
            HkssPlugin.Log.LogInfo($"[HK] Set alternate start location to '{start}'.");
        }
        else
        {
            HkssPlugin.Log.LogWarning($"[HK] Unsupported start location '{start}', starting in King's Pass.");
        }
    }

    private void AddHelperPlatforms()
    {
        HkHelperPlatformBuilder.AddConveniencePlatforms(_slotData);
        HkHelperPlatformBuilder.AddStartLocationRequiredPlatforms(_slotData.Options);
    }

    private void AddModules()
    {
        ItemChangerMod.Modules.Add<HkDupeHandlingModule>();
        ItemChangerMod.Modules.Add<HkRepositionShadeModule>();
        ItemChangerMod.Modules.Add<HkHintTracker>();
        ItemChangerMod.Modules.Add<HkBenchSyncModule>();
        ItemChangerMod.Modules.Add<HkGiftingModule>();
        ItemChangerMod.Modules.Add<CompletionPercentOverride>();

        var startEdits = ItemChangerMod.Modules.Add<HkStartLocationSceneEditsModule>();
        startEdits.StartLocationName = _slotData.Options.StartLocationName ?? HkStartLocationNames.KingsPass;
        startEdits.RandomizeNail = _slotData.Options.RandomizeNail;

        var goalModule = ItemChangerMod.Modules.Add<HkGoalModule>();
        goalModule.SelectedGoal = _slotData.Options.Goal;
        goalModule.GrubsRequired = _slotData.GrubsRequired ?? 46;

        bool deathLinkEnabled = _slotData.Options.DeathLink || (ApManager.Instance?.Client.DeathLinkEnabled ?? false);
        if (deathLinkEnabled)
        {
            var deathLink = ItemChangerMod.Modules.Add<HkDeathLinkModule>();
            deathLink.ShadeHandling = _slotData.Options.DeathLinkShade;
            deathLink.BreakFragileCharms = _slotData.Options.DeathLinkBreaksFragileCharms;
        }

        if (_slotData.Options.RandomizeNail)
            ItemChangerMod.Modules.Add<SplitNail>();

        if (_slotData.Options.RandomizeElevatorPass)
            ItemChangerMod.Modules.Add<ElevatorPass>();

        if (_slotData.Options.RandomizeFocus)
            ItemChangerMod.Modules.Add<FocusSkill>();

        if (_slotData.Options.RandomizeSwim)
            ItemChangerMod.Modules.Add<SwimSkill>();

        if (_slotData.Options.SplitMothwingCloak)
            ItemChangerMod.Modules.Add<SplitCloak>();

        if (_slotData.Options.SplitMantisClaw)
            ItemChangerMod.Modules.Add<SplitClaw>();

        if (_slotData.Options.SplitCrystalHeart)
            ItemChangerMod.Modules.Add<SplitSuperdash>();

        if (_slotData.Options.Slopeballs)
            ItemChangerMod.Modules.Add<ToggleableFireballUpgrade>();

        if (_slotData.Options.StartWithFullMaps)
            ItemChangerMod.Modules.Add<HkFullMapsModule>();

        if (_slotData.Options.EntranceRandoType != EntranceRandoType.None)
        {
            var targets = _slotData.EntrancePairs != null
                ? new HashSet<string>(_slotData.EntrancePairs.Values, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (targets.Contains($"{SceneNames.White_Palace_18}[top1]")
                || targets.Contains($"{SceneNames.White_Palace_17}[right1]")
                || targets.Contains($"{SceneNames.White_Palace_19}[top1]"))
            {
                ItemChangerMod.Modules.Add<ReversePathOfPainSaw>();
            }
            ItemChangerMod.Modules.Add<DisablePalaceMidWarp>();
            ItemChangerMod.Modules.Add<RemoveInfectedBlockades>();
        }
    }

    private void ApplyTransitionOverrides()
    {
        if (_slotData.EntrancePairs == null || _slotData.EntrancePairs.Count == 0) return;

        foreach (var pair in _slotData.EntrancePairs)
        {
            if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
            try
            {
                ItemChangerMod.AddTransitionOverride(ParseTransition(pair.Key), ParseTransition(pair.Value));
            }
            catch (Exception ex)
            {
                HkssPlugin.Log.LogWarning($"[HK] Failed to parse transition {pair.Key} -> {pair.Value}: {ex.Message}");
            }
        }
    }

    private void ApplyCharmCosts()
    {
        if (_slotData.NotchCosts == null || _slotData.NotchCosts.Count == 0) return;

        ItemChangerMod.Modules.Add<NotchCostUI>();
        ItemChangerMod.Modules.GetOrAdd<ZeroCostCharmEquip>();
        var pdEdits = ItemChangerMod.Modules.GetOrAdd<PlayerDataEditModule>();
        for (int i = 0; i < _slotData.NotchCosts.Count; i++)
        {
            pdEdits.AddPDEdit($"charmCost_{i + 1}", _slotData.NotchCosts[i]);
        }
    }

    private void InitializeDefaultShops()
    {
        HkSlotOptions opts = _slotData.Options;
        foreach (string name in ShopLocationNames)
        {
            AbstractLocation? loc = Finder.GetLocation(name);
            if (loc == null) continue;

            AbstractPlacement pmt = loc.Wrap();
            pmt.AddTag<HkPlacementTag>();
            _placements[name] = pmt;

            if (pmt is ShopPlacement shop)
            {
                shop.defaultShopItems = DefaultShopItems.IseldaMapPins
                    | DefaultShopItems.IseldaMapMarkers
                    | DefaultShopItems.LegEaterRepair;

                if (opts.AddUnshuffledLocations) continue;

                if (!opts.RandomizeCharms)
                {
                    shop.defaultShopItems |= DefaultShopItems.SlyCharms
                        | DefaultShopItems.SlyKeyCharms
                        | DefaultShopItems.IseldaCharms
                        | DefaultShopItems.SalubraCharms
                        | DefaultShopItems.LegEaterCharms;
                }
                if (!opts.RandomizeMaps)
                    shop.defaultShopItems |= DefaultShopItems.IseldaMaps | DefaultShopItems.IseldaQuill;
                if (!opts.RandomizeCharmNotches)
                    shop.defaultShopItems |= DefaultShopItems.SalubraNotches | DefaultShopItems.SalubraBlessing;
                if (!opts.RandomizeKeys)
                    shop.defaultShopItems |= DefaultShopItems.SlySimpleKey | DefaultShopItems.SlyLantern | DefaultShopItems.SlyKeyElegantKey;
                if (!opts.RandomizeMaskShards)
                    shop.defaultShopItems |= DefaultShopItems.SlyMaskShards;
                if (!opts.RandomizeVesselFragments)
                    shop.defaultShopItems |= DefaultShopItems.SlyVesselFragments;
                if (!opts.RandomizeRancidEggs)
                    shop.defaultShopItems |= DefaultShopItems.SlyRancidEgg;
            }
            else if (name == LocationNames.Grubfather)
            {
                var tag = pmt.AddTag<DestroyGrubRewardTag>();
                tag.destroyRewards = GrubfatherRewards.None;
                if (opts.AddUnshuffledLocations || opts.RandomizeMaskShards) tag.destroyRewards |= GrubfatherRewards.MaskShard;
                if (opts.AddUnshuffledLocations || opts.RandomizeCharms) tag.destroyRewards |= GrubfatherRewards.Grubsong | GrubfatherRewards.GrubberflysElegy;
                if (opts.AddUnshuffledLocations || opts.RandomizeRancidEggs) tag.destroyRewards |= GrubfatherRewards.RancidEgg;
                if (opts.AddUnshuffledLocations || opts.RandomizeRelics) tag.destroyRewards |= GrubfatherRewards.HallownestSeal | GrubfatherRewards.KingsIdol;
                if (opts.AddUnshuffledLocations || opts.RandomizePaleOre) tag.destroyRewards |= GrubfatherRewards.PaleOre;
            }
            else if (name == LocationNames.Seer)
            {
                var tag = pmt.AddTag<DestroySeerRewardTag>();
                tag.destroyRewards = SeerRewards.None;
                if (opts.AddUnshuffledLocations || opts.RandomizeRelics) tag.destroyRewards |= SeerRewards.HallownestSeal | SeerRewards.ArcaneEgg;
                if (opts.AddUnshuffledLocations || opts.RandomizePaleOre) tag.destroyRewards |= SeerRewards.PaleOre;
                if (opts.AddUnshuffledLocations || opts.RandomizeCharms) tag.destroyRewards |= SeerRewards.DreamWielder;
                if (opts.AddUnshuffledLocations || opts.RandomizeVesselFragments) tag.destroyRewards |= SeerRewards.VesselFragment;
                if (opts.AddUnshuffledLocations || opts.RandomizeSkills) tag.destroyRewards |= SeerRewards.DreamGate | SeerRewards.AwokenDreamNail;
                if (opts.AddUnshuffledLocations || opts.RandomizeMaskShards) tag.destroyRewards |= SeerRewards.MaskShard;
            }
        }
    }

    private bool PlaceItem(string rawLocation, string rawItemName, ScoutedItemInfo itemInfo)
    {
        string originalLocation = rawLocation;
        string location = StripShopSuffix(rawLocation);

        if (_slotData.Options.RandomizeLoreTablets)
        {
            if (location == LocationNames.Focus) location = LocationNames.Lore_Tablet_Kings_Pass_Focus;
            else if (location == LocationNames.World_Sense) location = LocationNames.Lore_Tablet_World_Sense;
        }

        AbstractLocation? loc = Finder.GetLocation(location);
        if (loc == null)
        {
            HkssPlugin.Log.LogDebug($"[HK] Skipping unknown ItemChanger location '{location}' (raw '{originalLocation}').");
            return false;
        }

        if (!_placements.TryGetValue(location, out AbstractPlacement? pmt))
        {
            pmt = loc.Wrap();
            pmt.AddTag<HkPlacementTag>();
            _placements[location] = pmt;
        }

        bool isMySlot = itemInfo.IsReceiverRelatedToActivePlayer;
        CrossGameItem? sharedItem = null;
        bool isOwnShared = isMySlot && CrossGameItems.TryGet(itemInfo.ItemId, out sharedItem);
        bool isLocalHk = isMySlot && IdSpace.IsHollowKnight(itemInfo.ItemId);
        bool isOwnSilksong = isMySlot && IdSpace.IsSilksong(itemInfo.ItemId);

        AbstractItem item;
        if (isOwnShared)
        {
            item = _itemFactory.CreateSharedHkItem(sharedItem!, itemInfo);
        }
        else if (isLocalHk)
        {
            item = _itemFactory.CreateLocalHkItem(rawItemName, itemInfo);
        }
        else
        {
            string? recipient = isOwnSilksong ? "Pharloom" : _session.Players.GetPlayerName(itemInfo.Player);
            item = _itemFactory.CreateRemoteOrCrossGameItem(pmt, recipient, rawItemName, itemInfo, isOwnSilksong);
        }

        pmt.Add(item);
        _costFactory.ApplyCost(pmt, item, originalLocation);
        return true;
    }

    private static string StripShopSuffix(string location)
    {
        foreach (string prefix in ShopSuffixPrefixes)
        {
            if (location.StartsWith(prefix, StringComparison.Ordinal))
                return prefix;
        }
        return location;
    }

    private static Transition ParseTransition(string transitionString)
    {
        int open = transitionString.IndexOf('[');
        int close = transitionString.IndexOf(']');
        if (open < 0 || close < 0 || close <= open)
            throw new ArgumentException($"Invalid transition format: {transitionString}");
        return new Transition(
            transitionString.Substring(0, open).Trim(),
            transitionString.Substring(open + 1, close - open - 1).Trim());
    }
}

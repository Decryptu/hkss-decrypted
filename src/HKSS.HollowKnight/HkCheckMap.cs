using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Internal;
using ItemChanger.Placements;
using ItemChanger.Tags;
using UnityEngine;
using Color = UnityEngine.Color;

namespace HKSS.HollowKnight;

internal enum HkMapVisibilityMode
{
    All = 0,
    OwnedMaps = 1,
    MappedRooms = 2,
    Off = 3,
}

internal enum HkMapCheckFilter
{
    Unchecked = 0,
    Reachable = 1,
    Unreachable = 2,
    Checked = 3,
    All = 4,
}

internal sealed class HkCheckEntry
{
    public long LocationId;
    public string RawLocationName = string.Empty;
    public string DisplayName = string.Empty;
    public string PlacementName = string.Empty;
    public string MapRoom = string.Empty;
    public HkPinCategory Category;
    public bool IsChecked;
    public HkReachability Reachability;
    public string? CostSummary;
    public bool IsHinted;
    public string? HintText;
}

internal sealed class HkMapPinRecord
{
    public string Key = string.Empty;
    public string MapRoom = string.Empty;
    public string Title = string.Empty;
    public HkPinCategory Category;
    public GameObject Area = null!;
    public GameObject PinObject = null!;
    public SpriteRenderer Renderer = null!;
    public SpriteRenderer OutlineRenderer = null!;
    public int BaseSortingOrder;
    public readonly List<HkCheckEntry> Checks = new();
}

/// <summary>
/// Full clean-room in-game check map for Hollow Knight matching Silksong parity:
/// category &amp; shop pins, horizontal room clusters, hint outlines, visibility &amp; check filters,
/// live worlds/hk reachability (F10), and automatic Wayward Compass.
/// </summary>
internal static class HkCheckMap
{
    private const float PinScale = 0.34f;
    private const float ClusterSpacing = 0.24f;
    private const float MaxClusterSpan = 0.78f;

    private static readonly Dictionary<string, string> InteriorToMapRoom = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Room_shop"] = "Town",
        ["Room_mapper"] = "Town",
        ["Room_Ouiji"] = "Town",
        ["Room_Sly_Storeroom"] = "Town",
        ["Room_Town_Stag_Station"] = "Town",
        ["Room_Bretta"] = "Town",
        ["Room_Jinn"] = "Town",
        ["Grimm_Main_Tent"] = "Town",
        ["Grimm_Divine"] = "Town",
        ["Room_Charm_Shop"] = "Crossroads_04",
        ["Room_Mender_House"] = "Crossroads_04",
        ["Room_ruinhouse"] = "Crossroads_04",
        ["Crossroads_ShamanTemple"] = "Crossroads_06",
        ["Room_temple"] = "Crossroads_02",
        ["Room_Final_Boss_Atrium"] = "Crossroads_02",
        ["Room_Mansion"] = "RestingGrounds_05",
        ["Dream_Nailcollection"] = "RestingGrounds_04",
        ["Room_Colosseum_01"] = "Deepnest_East_09",
        ["Room_Colosseum_02"] = "Deepnest_East_09",
        ["Room_Colosseum_Spectate"] = "Deepnest_East_09",
        ["GG_Lurker"] = "Deepnest_East_09",
        ["Room_Wyrm"] = "Deepnest_East_12",
        ["Room_nailsmith"] = "Ruins1_04",
        ["Room_Queen"] = "Fungus3_48",
        ["Room_Mask_Maker"] = "Deepnest_35",
        ["Room_Slug_Shrine"] = "Fungus1_26",
        ["Room_Fungus_Shaman"] = "Fungus3_26",
        ["Room_Nailmaster"] = "Cliffs_02",
        ["Room_Nailmaster_02"] = "Fungus1_15",
        ["Room_Nailmaster_03"] = "Deepnest_East_06",
        ["Room_GG_Shortcut"] = "Waterways_09",
        ["GG_Waterways"] = "Waterways_09",
        ["GG_Pipeway"] = "Waterways_08",
        ["Room_spider_small"] = "Deepnest_10",
        ["Deepnest_Spider_Town"] = "Deepnest_10",
        ["Ruins_House_01"] = "Ruins2_04",
        ["Ruins_House_02"] = "Ruins2_04",
        ["Ruins_House_03"] = "Ruins2_04",
        ["Ruins_Elevator"] = "Ruins2_04",
        ["Ruins_Bathhouse"] = "Ruins2_04",
        ["Ruins2_Watcher_Room"] = "Ruins2_03",
        ["Fungus3_archive"] = "Fungus3_47",
        ["Fungus3_archive_02"] = "Fungus3_47",
        // White Palace rooms anchor to Palace Grounds
        ["White_Palace_01"] = "Abyss_05",
        ["White_Palace_02"] = "Abyss_05",
        ["White_Palace_03_hub"] = "Abyss_05",
        ["White_Palace_04"] = "Abyss_05",
        ["White_Palace_05"] = "Abyss_05",
        ["White_Palace_06"] = "Abyss_05",
        ["White_Palace_07"] = "Abyss_05",
        ["White_Palace_08"] = "Abyss_05",
        ["White_Palace_09"] = "Abyss_05",
        ["White_Palace_11"] = "Abyss_05",
        ["White_Palace_12"] = "Abyss_05",
        ["White_Palace_13"] = "Abyss_05",
        ["White_Palace_14"] = "Abyss_05",
        ["White_Palace_15"] = "Abyss_05",
        ["White_Palace_16"] = "Abyss_05",
        ["White_Palace_17"] = "Abyss_05",
        ["White_Palace_18"] = "Abyss_05",
        ["White_Palace_19"] = "Abyss_05",
        ["White_Palace_20"] = "Abyss_05",
    };

    private static ConfigEntry<HkMapVisibilityMode>? _cfgVisibility;
    private static ConfigEntry<HkMapCheckFilter>? _cfgFilter;
    private static ConfigEntry<bool>? _cfgShowLogic;
    private static ConfigEntry<bool>? _cfgCategoryIcons;

    private static HkMapVisibilityMode _visibility = HkMapVisibilityMode.All;
    private static HkMapCheckFilter _filter = HkMapCheckFilter.Unchecked;
    private static bool _showLogic = true;
    private static bool _useCategoryIcons = true;

    private static readonly Dictionary<string, (GameObject Area, Transform Room, string MapOwnedBool)> RoomLookup = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HkMapPinRecord> PinsByKey = new(StringComparer.OrdinalIgnoreCase);

    private static GameMap? _indexedMap;
    private static GameObject? _pinRoot;
    private static HkMapPinRecord? _hoveredPin;
    private static HkLogicState? _lastLogicState;
    private static bool _dirty = true;
    private static bool _isWorldMapOpen;
    private static bool _isQuickMapOpen;
    private static int _totalUnchecked;
    private static int _totalReachable;

    internal static bool IsWorldMapOpen => _isWorldMapOpen && _indexedMap != null;
    internal static bool IsAnyMapOpen => (_isWorldMapOpen || _isQuickMapOpen) && _indexedMap != null;
    internal static HkLogicState? LastLogicState => _lastLogicState;
    internal static int TotalUnchecked => _totalUnchecked;
    internal static int TotalReachable => _totalReachable;

    internal static HkMapVisibilityMode VisibilityMode
    {
        get => _cfgVisibility?.Value ?? _visibility;
        set
        {
            _visibility = value;
            if (_cfgVisibility != null) _cfgVisibility.Value = value;
            MarkDirty();
            RefreshIfOpen();
        }
    }

    internal static HkMapCheckFilter CheckFilter
    {
        get => PlayerOptions.MapFilter.Value switch
        {
            CheckPinFilter.Unchecked => HkMapCheckFilter.Unchecked,
            CheckPinFilter.Reachable => HkMapCheckFilter.Reachable,
            CheckPinFilter.Blocked => HkMapCheckFilter.Unreachable,
            CheckPinFilter.Checked => HkMapCheckFilter.Checked,
            _ => _cfgFilter?.Value ?? _filter,
        };
        set
        {
            _filter = value;
            if (_cfgFilter != null) _cfgFilter.Value = value;
            MarkDirty();
            RefreshIfOpen();
        }
    }

    internal static bool ShowLogic
    {
        get => _cfgShowLogic?.Value ?? _showLogic;
        set
        {
            _showLogic = value;
            if (_cfgShowLogic != null) _cfgShowLogic.Value = value;
            MarkDirty();
            RefreshIfOpen();
        }
    }

    internal static bool UseCategoryIcons
    {
        get => _cfgCategoryIcons?.Value ?? _useCategoryIcons;
        set
        {
            _useCategoryIcons = value;
            if (_cfgCategoryIcons != null) _cfgCategoryIcons.Value = value;
            MarkDirty();
            RefreshIfOpen();
        }
    }

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        BindConfig();
        try
        {
            harmony.CreateClassProcessor(typeof(GameMapPatches)).Patch();
        }
        catch (Exception ex)
        {
            log.LogWarning($"[HK CheckMap] Failed to install GameMap patches: {ex.Message}");
        }

        AbstractItem.AfterGiveGlobal += _ => MarkDirty();
        HkHintTracker.OnArchipelagoHintUpdate += MarkDirty;
        ApManager.OnConnected += _ => MarkDirty();
        ApManager.OnItemNotice += (_, _) => MarkDirty();
    }

    private static void BindConfig()
    {
        ConfigFile? cfg = HkssPlugin.PluginConfig;
        if (cfg == null) return;

        _cfgVisibility = cfg.Bind("Map Markers", "Visibility Mode", HkMapVisibilityMode.All,
            "Controls which map check pins are shown: All, OwnedMaps, MappedRooms, or Off.");
        _cfgFilter = cfg.Bind("Map Markers", "Check Filter", HkMapCheckFilter.Unchecked,
            "Controls which checks are shown on the map: Unchecked, Reachable, Unreachable, Checked, or All.");
        _cfgShowLogic = cfg.Bind("Map Markers", "Show Map Marker Logic", true,
            "When enabled (toggled with F10), evaluates live Hollow Knight logic to highlight reachable vs blocked checks.");
        _cfgCategoryIcons = cfg.Bind("Map Markers", "Use Category Icons", true,
            "When enabled, displays category and shop icons instead of generic diamond pins.");

        _visibility = _cfgVisibility.Value;
        _filter = _cfgFilter.Value;
        _showLogic = _cfgShowLogic.Value;
        _useCategoryIcons = _cfgCategoryIcons.Value;
    }

    internal static void MarkDirty() => _dirty = true;

    internal static void RefreshIfOpen()
    {
        if (_indexedMap != null && IsAnyMapOpen)
        {
            RefreshPins(_indexedMap);
        }
    }

    internal static void SetHoveredPin(HkMapPinRecord? pin)
    {
        if (ReferenceEquals(_hoveredPin, pin)) return;
        HkMapPinRecord? prev = _hoveredPin;
        _hoveredPin = pin;
        ApplySorting(prev);
        ApplySorting(pin);
    }

    internal static HkMapPinRecord? FindHoveredPin(Camera cam, Vector2 screenPointer, float maxRadiusPx = 34f)
    {
        if (cam == null || !IsAnyMapOpen) return null;

        float bestDistSq = maxRadiusPx * maxRadiusPx;
        HkMapPinRecord? best = null;
        foreach (HkMapPinRecord pin in PinsByKey.Values)
        {
            if (pin.PinObject == null || !pin.PinObject.activeInHierarchy) continue;
            Vector3 sp = cam.WorldToScreenPoint(pin.PinObject.transform.position);
            if (sp.z <= 0f) continue;
            float dx = sp.x - screenPointer.x;
            float dy = sp.y - screenPointer.y;
            float d2 = dx * dx + dy * dy;
            if (d2 <= bestDistSq)
            {
                bestDistSq = d2;
                best = pin;
            }
        }
        return best;
    }

    private static void ApplySorting(HkMapPinRecord? pin)
    {
        if (pin?.Renderer == null) return;
        int order = pin.BaseSortingOrder + (ReferenceEquals(_hoveredPin, pin) ? 100 : 0);
        pin.Renderer.sortingOrder = order;
        if (pin.OutlineRenderer != null)
            pin.OutlineRenderer.sortingOrder = order - 1;
    }

    private static void EnsureIndexed(GameMap map)
    {
        if (_indexedMap == map && RoomLookup.Count > 0 && _pinRoot != null) return;

        _indexedMap = map;
        _hoveredPin = null;
        RoomLookup.Clear();
        ClearPins();

        if (_pinRoot != null)
            UnityEngine.Object.Destroy(_pinRoot);

        _pinRoot = new GameObject("HKSS_CheckMapRoot");
        _pinRoot.layer = map.gameObject.layer;
        _pinRoot.transform.SetParent(map.transform, worldPositionStays: false);
        _pinRoot.transform.localPosition = Vector3.zero;
        _pinRoot.transform.localScale = Vector3.one;

        (GameObject? Area, string OwnedBool)[] areas =
        {
            (map.areaAncientBasin, nameof(PlayerData.mapAbyss)),
            (map.areaCity, nameof(PlayerData.mapCity)),
            (map.areaCliffs, nameof(PlayerData.mapCliffs)),
            (map.areaCrossroads, nameof(PlayerData.mapCrossroads)),
            (map.areaCrystalPeak, nameof(PlayerData.mapMines)),
            (map.areaDeepnest, nameof(PlayerData.mapDeepnest)),
            (map.areaFogCanyon, nameof(PlayerData.mapFogCanyon)),
            (map.areaFungalWastes, nameof(PlayerData.mapFungalWastes)),
            (map.areaGreenpath, nameof(PlayerData.mapGreenpath)),
            (map.areaKingdomsEdge, nameof(PlayerData.mapOutskirts)),
            (map.areaQueensGardens, nameof(PlayerData.mapRoyalGardens)),
            (map.areaRestingGrounds, nameof(PlayerData.mapRestingGrounds)),
            (map.areaDirtmouth, string.Empty),
            (map.areaWaterways, nameof(PlayerData.mapWaterways)),
        };

        foreach (var (area, ownedBool) in areas)
        {
            if (area == null) continue;
            Transform t = area.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child == null || string.IsNullOrEmpty(child.name)) continue;
                if (child.name.StartsWith("HKSS_", StringComparison.Ordinal)) continue;
                if (!RoomLookup.ContainsKey(child.name))
                {
                    RoomLookup[child.name] = (area, child, ownedBool);
                }
            }
        }
    }

    private static void ClearPins()
    {
        foreach (HkMapPinRecord pin in PinsByKey.Values)
        {
            if (pin.PinObject != null)
                UnityEngine.Object.Destroy(pin.PinObject);
        }
        PinsByKey.Clear();
    }

    private static void SetPinsVisible(bool visible)
    {
        if (_pinRoot != null)
            _pinRoot.SetActive(visible);
    }

    private static void RefreshPins(GameMap map)
    {
        if (map == null || Ref.Settings?.Placements == null) return;
        EnsureIndexed(map);

        if (VisibilityMode == HkMapVisibilityMode.Off)
        {
            SetPinsVisible(false);
            return;
        }

        if (!_dirty && PinsByKey.Count > 0)
        {
            UpdateQuickMapAreaFilter();
            SetPinsVisible(IsAnyMapOpen);
            return;
        }
        _dirty = false;

        // 1. Evaluate live logic reachability
        HkLogicState? logic = ShowLogic || CheckFilter is HkMapCheckFilter.Reachable or HkMapCheckFilter.Unreachable
            ? EvaluateLiveLogic()
            : null;
        _lastLogicState = logic;

        // 2. Build hint lookup by LocationId
        var hintsByLocationId = BuildHintLookup();

        // 3. Collect all checks per map room
        var checksByRoom = new Dictionary<string, List<HkCheckEntry>>(StringComparer.OrdinalIgnoreCase);
        _totalUnchecked = 0;
        _totalReachable = 0;

        foreach (AbstractPlacement pmt in Ref.Settings.Placements.Values)
        {
            if (pmt == null || pmt.Items == null || pmt.Items.Count == 0) continue;
            if (pmt is HkRemotePlacement) continue;

            string? sceneName = (pmt as IPrimaryLocationPlacement)?.Location?.sceneName;
            if (string.IsNullOrEmpty(sceneName)) continue;

            string mapRoom = ResolveMapRoom(sceneName!);
            if (!RoomLookup.ContainsKey(mapRoom)) continue;

            for (int i = 0; i < pmt.Items.Count; i++)
            {
                AbstractItem item = pmt.Items[i];
                if (item == null) continue;

                item.GetTag<HkItemTag>(out HkItemTag? itemTag);
                long locId = itemTag?.Location ?? 0;

                string rawLocName = ResolveRawLocationName(pmt, item, i, locId);
                bool isChecked = item.WasEverObtained();
                HkReachability reach = logic != null
                    ? logic.EvaluateLocation(rawLocName)
                    : HkReachability.Unknown;

                if (!isChecked)
                {
                    _totalUnchecked++;
                    if (reach == HkReachability.Reachable)
                        _totalReachable++;
                }

                if (!PassesCheckFilter(isChecked, reach))
                    continue;

                bool isHinted = (itemTag?.Hinted == true) || (locId > 0 && hintsByLocationId.ContainsKey(locId));
                string? hintText = null;
                if (locId > 0 && hintsByLocationId.TryGetValue(locId, out string? h))
                    hintText = h;

                var entry = new HkCheckEntry
                {
                    LocationId = locId,
                    RawLocationName = rawLocName,
                    DisplayName = FormatCheckName(pmt.Name, rawLocName, i, pmt.Items.Count),
                    PlacementName = pmt.Name,
                    MapRoom = mapRoom,
                    Category = HkCheckMapIcons.Categorize(pmt.Name, rawLocName),
                    IsChecked = isChecked,
                    Reachability = reach,
                    CostSummary = FormatCostSummary(item, rawLocName),
                    IsHinted = isHinted && !isChecked,
                    HintText = hintText,
                };

                if (!checksByRoom.TryGetValue(mapRoom, out List<HkCheckEntry>? list))
                {
                    list = new List<HkCheckEntry>();
                    checksByRoom[mapRoom] = list;
                }
                list.Add(entry);
            }
        }

        // 4. Rebuild cluster pins per room
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roomKv in checksByRoom)
        {
            string mapRoom = roomKv.Key;
            if (!RoomLookup.TryGetValue(mapRoom, out var roomInfo)) continue;

            bool passesVisibility = PassesVisibilityMode(mapRoom, roomInfo.MapOwnedBool);
            if (!passesVisibility) continue;

            // Group checks into clusters:
            // - If UseCategoryIcons is ON, cluster by Shop placement name (for shops) or by HkPinCategory.
            // - If UseCategoryIcons is OFF, one generic pin per room.
            List<(string ClusterKey, string Title, HkPinCategory Category, List<HkCheckEntry> Items)> clusters =
                BuildRoomClusters(mapRoom, roomKv.Value);

            int count = clusters.Count;
            int cols = Math.Min(count, 4);
            int rows = (count + cols - 1) / cols;
            float spacingX = cols > 1 ? Math.Min(ClusterSpacing, MaxClusterSpan / (cols - 1)) : 0f;
            float spacingY = 0.24f;

            Vector3 basePos = roomInfo.Area.transform.localPosition + roomInfo.Room.localPosition;
            SpriteRenderer? roomSr = roomInfo.Room.GetComponent<SpriteRenderer>();
            int baseOrder = roomSr != null ? roomSr.sortingOrder + 25 : 60;
            int sortingLayerId = roomSr != null ? roomSr.sortingLayerID : 0;

            for (int idx = 0; idx < count; idx++)
            {
                var cluster = clusters[idx];
                activeKeys.Add(cluster.ClusterKey);

                int row = idx / cols;
                int col = idx % cols;
                int itemsInRow = (row == rows - 1) ? (count - row * cols) : cols;
                float ox = (col - (itemsInRow - 1) * 0.5f) * spacingX;
                float oy = ((rows - 1) * 0.5f - row) * spacingY;

                HkMapPinRecord pin = GetOrCreatePin(cluster.ClusterKey, mapRoom, roomInfo.Area, sortingLayerId, baseOrder + idx * 2);
                pin.Title = cluster.Title;
                pin.Category = cluster.Category;
                pin.Checks.Clear();
                pin.Checks.AddRange(cluster.Items);
                pin.PinObject.transform.localPosition = new Vector3(basePos.x + ox, basePos.y + oy, -0.6f);

                ApplyPinVisuals(pin);
            }
        }

        // Disable unused pins
        foreach (var kv in PinsByKey)
        {
            if (!activeKeys.Contains(kv.Key) && kv.Value.PinObject != null)
            {
                if (ReferenceEquals(_hoveredPin, kv.Value))
                    SetHoveredPin(null);
                kv.Value.PinObject.SetActive(false);
            }
        }

        UpdateQuickMapAreaFilter();
        SetPinsVisible(IsAnyMapOpen);
    }

    private static List<(string ClusterKey, string Title, HkPinCategory Category, List<HkCheckEntry> Items)> BuildRoomClusters(
        string mapRoom,
        List<HkCheckEntry> entries)
    {
        var result = new List<(string, string, HkPinCategory, List<HkCheckEntry>)>();
        if (!UseCategoryIcons)
        {
            result.Add(($"{mapRoom}|ALL", mapRoom.Replace('_', ' '), HkPinCategory.Generic, entries));
            return result;
        }

        var groups = entries.GroupBy(e =>
        {
            if (e.Category == HkPinCategory.Shop)
                return $"SHOP:{e.PlacementName}";
            return $"CAT:{(int)e.Category}";
        }).OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var list = g.ToList();
            HkPinCategory cat = list[0].Category;
            string title = cat == HkPinCategory.Shop
                ? list[0].PlacementName.Replace('_', ' ')
                : HkCheckMapIcons.GetCategoryLabel(cat);
            result.Add(($"{mapRoom}|{g.Key}", title, cat, list));
        }

        return result;
    }

    private static HkMapPinRecord GetOrCreatePin(string key, string mapRoom, GameObject area, int sortingLayerId, int sortingOrder)
    {
        if (PinsByKey.TryGetValue(key, out HkMapPinRecord? existing) && existing.PinObject != null)
        {
            existing.BaseSortingOrder = sortingOrder;
            ApplySorting(existing);
            return existing;
        }

        var pinGo = new GameObject($"HKSS_Pin_{key}");
        pinGo.layer = _pinRoot!.layer;
        pinGo.transform.SetParent(_pinRoot.transform, worldPositionStays: false);
        pinGo.transform.localScale = new Vector3(PinScale, PinScale, 1f);

        var sr = pinGo.AddComponent<SpriteRenderer>();
        sr.sortingLayerID = sortingLayerId;
        sr.sortingOrder = sortingOrder;

        var outlineGo = new GameObject("HintOutline");
        outlineGo.layer = pinGo.layer;
        outlineGo.transform.SetParent(pinGo.transform, worldPositionStays: false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localScale = Vector3.one;

        var outlineSr = outlineGo.AddComponent<SpriteRenderer>();
        outlineSr.sprite = HkCheckMapIcons.GetHintOutlineSprite();
        outlineSr.color = Color.white;
        outlineSr.sortingLayerID = sortingLayerId;
        outlineSr.sortingOrder = sortingOrder - 1;
        outlineGo.SetActive(false);

        var rec = new HkMapPinRecord
        {
            Key = key,
            MapRoom = mapRoom,
            Area = area,
            PinObject = pinGo,
            Renderer = sr,
            OutlineRenderer = outlineSr,
            BaseSortingOrder = sortingOrder,
        };
        PinsByKey[key] = rec;
        return rec;
    }

    private static void ApplyPinVisuals(HkMapPinRecord pin)
    {
        HkPinCategory spriteCat = UseCategoryIcons ? pin.Category : HkPinCategory.Generic;
        pin.Renderer.sprite = HkCheckMapIcons.GetSprite(spriteCat);

        bool allChecked = pin.Checks.All(c => c.IsChecked);
        bool anyHinted = pin.Checks.Any(c => c.IsHinted);
        bool allUnreachable = ShowLogic
            && !allChecked
            && pin.Checks.Where(c => !c.IsChecked).All(c => c.Reachability == HkReachability.Unreachable);

        if (allChecked)
        {
            pin.Renderer.color = new Color(0.55f, 0.85f, 0.62f, 0.78f);
        }
        else if (allUnreachable)
        {
            pin.Renderer.color = new Color(0.45f, 0.45f, 0.50f, 0.72f);
        }
        else
        {
            pin.Renderer.color = Color.white;
        }

        pin.OutlineRenderer.gameObject.SetActive(anyHinted);
        pin.PinObject.SetActive(true);
        ApplySorting(pin);
    }

    private static void UpdateQuickMapAreaFilter()
    {
        if (!_isQuickMapOpen) return;
        foreach (HkMapPinRecord pin in PinsByKey.Values)
        {
            if (pin.PinObject == null || !pin.PinObject.activeSelf) continue;
            if (pin.Area != null && !pin.Area.activeSelf)
                pin.PinObject.SetActive(false);
        }
    }

    private static bool PassesCheckFilter(bool isChecked, HkReachability reachability) => CheckFilter switch
    {
        HkMapCheckFilter.Unchecked => !isChecked,
        HkMapCheckFilter.Reachable => !isChecked && reachability == HkReachability.Reachable,
        HkMapCheckFilter.Unreachable => !isChecked && reachability == HkReachability.Unreachable,
        HkMapCheckFilter.Checked => isChecked,
        HkMapCheckFilter.All => true,
        _ => !isChecked,
    };

    private static bool PassesVisibilityMode(string mapRoom, string ownedBool)
    {
        PlayerData? pd = PlayerData.instance;
        return VisibilityMode switch
        {
            HkMapVisibilityMode.Off => false,
            HkMapVisibilityMode.All => true,
            HkMapVisibilityMode.OwnedMaps => string.IsNullOrEmpty(ownedBool) || (pd != null && (pd.mapAllRooms || pd.GetBool(ownedBool))),
            HkMapVisibilityMode.MappedRooms => pd != null && (pd.mapAllRooms || (pd.scenesMapped != null && pd.scenesMapped.Contains(mapRoom))),
            _ => true,
        };
    }

    private static string ResolveMapRoom(string sceneName)
    {
        if (RoomLookup.ContainsKey(sceneName)) return sceneName;
        if (InteriorToMapRoom.TryGetValue(sceneName, out string? mapped)) return mapped;
        return sceneName;
    }

    private static string ResolveRawLocationName(AbstractPlacement pmt, AbstractItem item, int index, long locId)
    {
        ApClient? client = ApManager.Instance?.Client;
        if (locId > 0 && client?.Session != null)
        {
            string? apName = client.Session.Locations.GetLocationNameFromId(locId);
            if (!string.IsNullOrWhiteSpace(apName))
                return IdSpace.StripPrefix(apName!);
        }

        if (HkCheckMapIcons.IsShopPlacement(pmt.Name))
            return $"{pmt.Name}_{index + 1}";

        return pmt.Name;
    }

    private static string FormatCheckName(string placementName, string rawLocationName, int index, int totalInPlacement)
    {
        if (HkCheckMapIcons.IsShopPlacement(placementName))
            return $"{placementName.Replace('_', ' ')} #{index + 1}";

        string clean = !string.IsNullOrWhiteSpace(rawLocationName) ? rawLocationName : placementName;
        return clean.Replace('_', ' ').Replace('-', ' ');
    }

    private static string? FormatCostSummary(AbstractItem item, string rawLocationName)
    {
        var parts = new List<string>();
        HkSlotData? slot = HkClientHost.CurrentSlotData;
        if (slot?.LocationCosts != null && slot.LocationCosts.TryGetValue(rawLocationName, out Dictionary<string, int>? costs) && costs != null)
        {
            foreach (var kv in costs)
            {
                string label = kv.Key.ToUpperInvariant() switch
                {
                    "GEO" => "Geo",
                    "GRUBS" => "Grubs",
                    "ESSENCE" => "Essence",
                    "CHARMS" => "Charms",
                    "RANCIDEGGS" => "Eggs",
                    _ => kv.Key,
                };
                parts.Add($"{kv.Value} {label}");
            }
        }
        else if (item.GetTag<CostTag>(out CostTag? costTag) && costTag?.Cost != null)
        {
            string desc = costTag.Cost.GetCostText();
            if (!string.IsNullOrWhiteSpace(desc))
                parts.Add(desc);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static Dictionary<long, string> BuildHintLookup()
    {
        var map = new Dictionary<long, string>();
        List<Hint>? hints = HkHintTracker.Hints;
        ApClient? client = ApManager.Instance?.Client;
        if (hints == null || client?.Session == null) return map;

        int mySlot = client.Slot;
        foreach (Hint h in hints)
        {
            if (h == null || h.Found || h.FindingPlayer != mySlot) continue;
            if (!IdSpace.IsHollowKnight(h.LocationId)) continue;

            string itemName = client.Session.Items.GetItemName(h.ItemId) ?? $"Item #{h.ItemId}";
            itemName = IdSpace.StripPrefix(itemName).Replace('_', ' ');
            string playerName = client.Session.Players.GetPlayerName(h.ReceivingPlayer) ?? $"P{h.ReceivingPlayer}";
            map[h.LocationId] = h.ReceivingPlayer == mySlot
                ? $"Hint: {itemName}"
                : $"Hint: {itemName} ({playerName})";
        }
        return map;
    }

    private static HkLogicState EvaluateLiveLogic()
    {
        var owned = new List<string>();
        ApClient? client = ApManager.Instance?.Client;
        var received = client?.Session?.Items?.AllItemsReceived;

        if (received != null && received.Count > 0)
        {
            foreach (ItemInfo item in received)
            {
                if (!IdSpace.IsHollowKnight(item.ItemId)) continue;
                string? name = client!.Session!.Items.GetItemName(item.ItemId);
                if (!string.IsNullOrWhiteSpace(name))
                    owned.Add(IdSpace.StripPrefix(name!));
            }
        }
        else
        {
            // Offline fallback: infer core progression directly from PlayerData
            CollectPlayerDataItemsFallback(owned);
        }

        HkSlotData? slot = HkClientHost.CurrentSlotData;
        var input = new HkLogicInput
        {
            OwnedItems = owned,
            Options = slot?.Options.ToLogicOptions(),
            NotchCosts = slot?.NotchCosts,
            LocationCosts = slot?.LocationCosts,
            EntrancePairs = slot?.EntrancePairs,
            StartLocationName = slot?.Options.StartLocationName,
            BaseNotches = 3,
            BaseMaskShards = 20,
        };

        return HkLogicEvaluator.Evaluate(input);
    }

    private static void CollectPlayerDataItemsFallback(List<string> owned)
    {
        PlayerData? pd = PlayerData.instance;
        if (pd == null) return;

        if (pd.hasDash) owned.Add("Mothwing_Cloak");
        if (pd.hasShadowDash) owned.Add("Shade_Cloak");
        if (pd.hasWalljump) owned.Add("Mantis_Claw");
        if (pd.hasSuperDash) owned.Add("Crystal_Heart");
        if (pd.hasDoubleJump) owned.Add("Monarch_Wings");
        if (pd.hasAcidArmour) owned.Add("Isma's_Tear");
        if (pd.hasLantern) owned.Add("Lumafly_Lantern");
        if (pd.hasTramPass) owned.Add("Tram_Pass");
        if (pd.hasCityKey) owned.Add("City_Crest");
        if (pd.hasSlykey) owned.Add("Shopkeeper's_Key");
        if (pd.hasWhiteKey) owned.Add("Elegant_Key");
        if (pd.hasLoveKey) owned.Add("Love_Key");
        if (pd.hasKingsBrand) owned.Add("King's_Brand");
        if (pd.hasDreamNail) owned.Add("Dream_Nail");
        if (pd.hasDreamGate) owned.Add("Dream_Gate");
        if (pd.dreamNailUpgraded) owned.Add("Awoken_Dream_Nail");
        if (pd.fireballLevel >= 1) owned.Add("Vengeful_Spirit");
        if (pd.fireballLevel >= 2) owned.Add("Shade_Soul");
        if (pd.quakeLevel >= 1) owned.Add("Desolate_Dive");
        if (pd.quakeLevel >= 2) owned.Add("Descending_Dark");
        if (pd.screamLevel >= 1) owned.Add("Howling_Wraiths");
        if (pd.screamLevel >= 2) owned.Add("Abyss_Shriek");
        if (pd.hasCyclone) owned.Add("Cyclone_Slash");
        if (pd.hasDashSlash) owned.Add("Dash_Slash");
        if (pd.hasUpwardSlash) owned.Add("Great_Slash");
        for (int i = 0; i < pd.simpleKeys; i++) owned.Add("Simple_Key");
        for (int i = 0; i < pd.grubsCollected; i++) owned.Add("Grub");
        for (int i = 0; i < pd.rancidEggs; i++) owned.Add("Rancid_Egg");
    }

    [HarmonyPatch(typeof(GameMap))]
    private static class GameMapPatches
    {
        [HarmonyPatch("PositionCompass")]
        [HarmonyPrefix]
        private static void PositionCompassPrefix(out bool __state)
        {
            PlayerData? pd = PlayerData.instance;
            __state = pd != null && pd.equippedCharm_2;
            if (pd != null)
                pd.equippedCharm_2 = true;
        }

        [HarmonyPatch("PositionCompass")]
        [HarmonyFinalizer]
        private static void PositionCompassFinalizer(bool __state)
        {
            PlayerData? pd = PlayerData.instance;
            if (pd != null)
                pd.equippedCharm_2 = __state;
        }

        [HarmonyPatch(nameof(GameMap.SetupMap))]
        [HarmonyPostfix]
        private static void SetupMapPostfix(GameMap __instance)
        {
            _dirty = true;
            RefreshPins(__instance);
        }

        [HarmonyPatch(nameof(GameMap.SetupMapMarkers))]
        [HarmonyPostfix]
        private static void SetupMapMarkersPostfix(GameMap __instance)
        {
            RefreshPins(__instance);
        }

        [HarmonyPatch(nameof(GameMap.WorldMap))]
        [HarmonyPostfix]
        private static void WorldMapPostfix(GameMap __instance)
        {
            _isWorldMapOpen = true;
            _isQuickMapOpen = false;
            _dirty = true;

            // In All visibility mode, widen pan bounds so pins in unowned map areas can be inspected too.
            if (VisibilityMode == HkMapVisibilityMode.All)
            {
                __instance.panMinX = Math.Min(__instance.panMinX, -24.16f);
                __instance.panMaxX = Math.Max(__instance.panMaxX, 17.3f);
                __instance.panMinY = Math.Min(__instance.panMinY, -12.6f);
                __instance.panMaxY = Math.Max(__instance.panMaxY, 15.7f);
            }

            RefreshPins(__instance);
        }

        [HarmonyPatch(nameof(GameMap.CloseQuickMap))]
        [HarmonyPostfix]
        private static void CloseQuickMapPostfix()
        {
            _isWorldMapOpen = false;
            _isQuickMapOpen = false;
            SetHoveredPin(null);
            SetPinsVisible(false);
        }

        [HarmonyPatch("PositionCompass")]
        [HarmonyPostfix]
        private static void QuickMapDetectPostfix(GameMap __instance)
        {
            // QuickMap* methods set displayNextArea = true before calling PositionCompass.
            if (__instance.displayNextArea && !_isWorldMapOpen)
            {
                _isQuickMapOpen = true;
                RefreshPins(__instance);
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using HKSS.HollowKnight.ModApi;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Internal;
using Modding;
using Newtonsoft.Json;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Hosts ItemChanger and the Hollow Knight Archipelago client inside the BepInEx plugin.
/// Persists ItemChanger state exclusively inside the isolated run folder (<c>saves/hollow_knight/itemchanger.json</c>).
/// </summary>
internal sealed class HkClientHost : MonoBehaviour
{
    private const float ConnectWaitBudgetSeconds = 12f;

    private sealed class HkIcSerializationBinder : Newtonsoft.Json.Serialization.DefaultSerializationBinder
    {
        private static readonly string IcAssemblyName = typeof(ItemChangerMod).Assembly.GetName().Name;

        public override Type BindToType(string? assemblyName, string typeName)
        {
            if (assemblyName == "ItemChanger") assemblyName = IcAssemblyName;
            return base.BindToType(assemblyName, typeName);
        }
    }

    private static readonly JsonSerializerSettings IcJsonSettings = new()
    {
        DefaultValueHandling = DefaultValueHandling.Include,
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        SerializationBinder = new HkIcSerializationBinder(),
    };

    private static readonly GiveInfo RemoteGiveInfo = new()
    {
        FlingType = FlingType.DirectDeposit,
        Callback = null,
        Container = Container.Unknown,
        MessageType = MessageType.Corner,
    };

    private static readonly GiveInfo SilentGiveInfo = new()
    {
        FlingType = FlingType.DirectDeposit,
        Callback = null,
        Container = Container.Unknown,
        MessageType = MessageType.None,
    };

    private static ItemChangerMod? _icMod;
    private static GameObject? _flowerPickupPrefab;
    private static bool _preloaded;
    private static bool _icActive;
    private static int _pendingStartingGeo;

    internal static HkSlotData? CurrentSlotData { get; private set; }

    internal static GameObject? CreateFlowerPickup() =>
        _flowerPickupPrefab != null ? UnityEngine.Object.Instantiate(_flowerPickupPrefab) : null;

    private bool _preloadStarted;
    private bool _connectWaitDone;
    private float _connectWaitStart = -1f;
    private string _icSavePath = string.Empty;
    private string _icSeedPath = string.Empty;
    private string _slotDataPath = string.Empty;
    private string _loadedSeedKey = string.Empty;

    private static bool _assemblyResolverInstalled;

    internal static void InstallEarly(Harmony harmony, ManualLogSource log)
    {
        if (!_assemblyResolverInstalled)
        {
            _assemblyResolverInstalled = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveItemChangerAssembly;
        }
        Mod.Logger = log;
        ModHooks.Install(harmony, log);
        HkQolPatches.Install(harmony, log);
        HkExtraChecks.Install(harmony, log);
        _icMod = new ItemChangerMod();
    }

    private static System.Reflection.Assembly? ResolveItemChangerAssembly(object sender, ResolveEventArgs args)
    {
        string requested = new System.Reflection.AssemblyName(args.Name).Name;
        if (string.Equals(requested, "ItemChanger", StringComparison.OrdinalIgnoreCase))
            return typeof(ItemChangerMod).Assembly;
        return null;
    }

    private void Awake()
    {
        _icSavePath = Path.Combine(HkssPlugin.Run.SaveDir, "itemchanger.json");
        _icSeedPath = Path.Combine(HkssPlugin.Run.SaveDir, "itemchanger.seed");
        _slotDataPath = Path.Combine(HkssPlugin.Run.SaveDir, "slotdata.json");
        AutoLoad.HoldAutoLoad = true;
        ApManager.DeferAutoConnect = true;
        ApManager.FullHostInstalled = true;
        ApManager.DeferProcessedPersistence = true;
        ApManager.OnConnected += OnApConnected;
        ApManager.LocalItemGrantHandler = TryGrantViaItemChanger;
        ModHooks.SavegameSaveHook += OnSaveGame;
        Events.OnEnterGame += OnEnterGame;
        Events.OnSettingsLoadError += OnSettingsLoadError;
        if (gameObject.GetComponent<HkRecentItemsDisplay>() == null)
            gameObject.AddComponent<HkRecentItemsDisplay>();
        if (gameObject.GetComponent<HkCheckMapOverlay>() == null)
            gameObject.AddComponent<HkCheckMapOverlay>();
        if (gameObject.GetComponent<HkLinksManager>() == null)
            gameObject.AddComponent<HkLinksManager>();
        HkGiftingModule.BindConfig(HkssPlugin.PluginConfig);
    }

    private void OnDestroy()
    {
        ApManager.OnConnected -= OnApConnected;
        if (ApManager.LocalItemGrantHandler == TryGrantViaItemChanger)
            ApManager.LocalItemGrantHandler = null;
        ModHooks.SavegameSaveHook -= OnSaveGame;
        Events.OnEnterGame -= OnEnterGame;
        Events.OnSettingsLoadError -= OnSettingsLoadError;
    }

    private static void OnSettingsLoadError(Exception ex)
    {
        HkssPlugin.Log.LogError($"[HK] ItemChanger failed to load placements: {ex.Message}");
        ApManager.Instance?.PushToast(
            $"Randomizer failed to load placements ({ex.Message}). Export a bug report from the launcher.",
            ToastKind.Error);
    }

    private void Update()
    {
        if (!_preloadStarted && GameManager._instance != null && GameManager._instance.IsMenuScene())
        {
            _preloadStarted = true;
            StartCoroutine(RunPreloadAndRestore());
        }

        if (_preloaded && !_connectWaitDone && AutoLoad.HoldAutoLoad)
        {
            ApManager? ap = ApManager.Instance;
            bool shouldWait = !_icActive
                && ap != null
                && ap.IsAutoConnectPending;

            if (!shouldWait)
            {
                ReleaseAutoLoad();
            }
            else
            {
                if (_connectWaitStart < 0f) _connectWaitStart = Time.unscaledTime;
                if (Time.unscaledTime - _connectWaitStart > ConnectWaitBudgetSeconds)
                {
                    HkssPlugin.Log.LogWarning("[HK] Continuing boot without waiting further for Archipelago connection.");
                    ReleaseAutoLoad();
                }
            }
        }

        if (_pendingRemoteSync && _icActive && ItemChangerMod.IsSettingsLoaded && IsGameplayReady())
        {
            ApClient? client = ApManager.Instance?.Client;
            if (client != null && client.IsConnected)
            {
                SyncRemoteCheckedLocations(client);
            }
        }

        if (_pendingStartingGeo > 0 && HeroController.instance != null && GameManager._instance != null && GameManager._instance.IsGameplayScene())
        {
            int geo = _pendingStartingGeo;
            _pendingStartingGeo = 0;
            HeroController.instance.AddGeo(geo);
            HkssPlugin.Log.LogInfo($"[HK] Granted starting geo: {geo}");
        }
    }

    private IEnumerator RunPreloadAndRestore()
    {
        if (_icMod == null)
        {
            _preloaded = true;
            ReleaseAutoLoad();
            yield break;
        }

        // Reduce non-essential preloads (geo rocks / soul totems) only if desired; Core + Grub + Mimic are fast.
        List<(string, string)> preloads = _icMod.GetPreloadNames();
        preloads.Add((SceneNames.Fungus3_49, "gg_white_flower"));
        Dictionary<string, Dictionary<string, GameObject>>? loaded = null;
        yield return HKSS.HollowKnight.ModApi.ScenePreloader.PreloadAsync(preloads, HkssPlugin.Log, dict => loaded = dict);

        if (loaded != null
            && loaded.TryGetValue(SceneNames.Fungus3_49, out Dictionary<string, GameObject>? flowerScene)
            && flowerScene.TryGetValue("gg_white_flower", out GameObject? flowerPrefab))
        {
            _flowerPickupPrefab = flowerPrefab;
        }

        try
        {
            _icMod.Initialize(loaded ?? new Dictionary<string, Dictionary<string, GameObject>>());
            HkssPlugin.Log.LogInfo("[HK] ItemChanger initialized.");
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] ItemChanger.Initialize failed: {ex}");
        }

        TryLoadSavedItemChangerProfile();
        _preloaded = true;
        ApManager.DeferAutoConnect = false;
    }

    private void ReleaseAutoLoad()
    {
        _connectWaitDone = true;
        AutoLoad.HoldAutoLoad = false;
    }

    private void OnApConnected(ApClient client)
    {
        if (!_preloaded || _icMod == null)
        {
            StartCoroutine(DeferredOnApConnected(client));
            return;
        }
        ApplyApSessionToItemChanger(client);
    }

    private IEnumerator DeferredOnApConnected(ApClient client)
    {
        while (!_preloaded) yield return null;
        ApplyApSessionToItemChanger(client);
    }

    private void ApplyApSessionToItemChanger(ApClient client)
    {
        try
        {
            string seed = client.Session?.RoomState?.Seed ?? string.Empty;
            string currentSeedKey = string.IsNullOrEmpty(seed)
                ? string.Empty
                : $"{seed}|{client.ConnectedSlot}";

            if (_icActive && !string.IsNullOrEmpty(currentSeedKey))
            {
                if (!string.IsNullOrEmpty(_loadedSeedKey)
                    && !string.Equals(_loadedSeedKey, currentSeedKey, StringComparison.Ordinal))
                {
                    HkssPlugin.Log.LogWarning("[HK] Archipelago room seed changed; rebuilding ItemChanger placements for the new room.");
                    if (_icMod != null)
                        ((ILocalSettings<SaveSettings>)_icMod).OnLoadLocal(default);
                    _icActive = false;
                    ThinSlice.SuppressCheckHooks = false;
                    _loadedSeedKey = string.Empty;
                }
                else if (string.IsNullOrEmpty(_loadedSeedKey))
                {
                    _loadedSeedKey = currentSeedKey;
                    TryWriteSeedMarker(currentSeedKey);
                }
            }

            HkSlotData? connectedSlot = ExtractHkSlotData(client.SlotData);
            if (connectedSlot != null)
            {
                CurrentSlotData = connectedSlot;
                TryWriteSlotDataCache(connectedSlot);
                HkCheckMap.MarkDirty();
                // The links read their seed options from this slot data, so reconfigure once it is set.
                HkLinksManager.Instance?.Reconfigure();
            }

            if (!_icActive)
            {
                HkSlotData? hkSlot = connectedSlot;
                if (hkSlot != null && client.Session != null)
                {
                    long[] hkLocs = client.GetOwnLocationIds();
                    var randomizer = new HkRandomizer(hkSlot, client.Session);
                    randomizer.Randomize(hkLocs);
                    _icActive = true;
                    ApManager.FullHostInstalled = true;
                    ThinSlice.SuppressCheckHooks = true;
                    _loadedSeedKey = currentSeedKey;

                    if (hkSlot.Options.StartingGeo > 0)
                        _pendingStartingGeo = hkSlot.Options.StartingGeo;

                    if (GameManager._instance != null && GameManager._instance.IsGameplayScene())
                    {
                        ItemChangerMod.EnsureSettingsLoaded();
                    }
                    SaveItemChangerProfile();
                }
                else
                {
                    ApManager.FullHostInstalled = false;
                    HkssPlugin.Log.LogInfo("[HK] No slot_data['hollow_knight'] present; using thin-slice fallback.");
                }
            }

            if (_icActive)
            {
                SyncRemoteCheckedLocations(client);
            }
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Failed to configure ItemChanger for AP session: {ex}");
        }
        finally
        {
            ReleaseAutoLoad();
        }
    }

    private static void OnEnterGame()
    {
        if (_icActive && ItemChangerMod.IsSettingsLoaded)
        {
            ItemChangerMod.Modules.GetOrAdd<HkBenchSyncModule>();
            ItemChangerMod.Modules.GetOrAdd<HkGiftingModule>();
            ReconcileObtainedCustomSkills();
        }

        ApClient? client = ApManager.Instance?.Client;
        if (client == null || !client.IsConnected || !_icActive) return;

        // Batch-sync any locally obtained HK checks (without per-item toast spam) and pull remote checks.
        var localObtained = new List<long>();
        foreach (var kv in HkPlacementTag.PlacementsByLocationId)
        {
            if (kv.Value == null) continue;
            foreach (AbstractItem item in kv.Value.Items)
            {
                if (item.WasEverObtained()
                    && item.GetTag<HkItemTag>(out HkItemTag tag)
                    && tag != null
                    && tag.Location > 0)
                {
                    localObtained.Add(tag.Location);
                }
            }
        }
        if (localObtained.Count > 0)
            client.SyncCheckedLocations(localObtained);

        SyncRemoteCheckedLocations(client);
    }

    private static void ReconcileObtainedCustomSkills()
    {
        if (PlayerData.instance == null || Ref.Settings?.Placements == null) return;

        int repaired = 0;
        foreach (AbstractPlacement pmt in Ref.Settings.Placements.Values)
        {
            if (pmt == null) continue;
            foreach (AbstractItem item in pmt.Items)
            {
                if (item is ItemChanger.Items.CustomSkillItem skill
                    && skill.WasEverObtained()
                    && !string.IsNullOrEmpty(skill.boolName)
                    && !PlayerData.instance.GetBool(skill.boolName))
                {
                    PlayerData.instance.SetBool(skill.boolName, true);
                    repaired++;
                    HkssPlugin.Log.LogInfo($"[HK] Restored missing custom skill bool '{skill.boolName}' from obtained item '{skill.name}'.");
                }
            }
        }

        if (repaired > 0)
        {
            HkssPlugin.Log.LogInfo($"[HK] Repaired {repaired} custom skill state(s) on save load.");
        }
    }

    private static bool _pendingRemoteSync;

    private static bool IsGameplayReady()
    {
        GameManager? gm = GameManager._instance;
        return HeroController.SilentInstance != null
            && gm != null
            && gm.IsGameplayScene()
            && !gm.IsInSceneTransition;
    }

    private static void SyncRemoteCheckedLocations(ApClient client)
    {
        if (!ItemChangerMod.IsSettingsLoaded || !IsGameplayReady())
        {
            _pendingRemoteSync = true;
            return;
        }
        _pendingRemoteSync = false;
        foreach (long locId in client.GetOwnCheckedLocationIds())
        {
            MarkLocationAsChecked(locId, silent: true);
        }
    }

    private static void MarkLocationAsChecked(long locationId, bool silent)
    {
        if (!HkPlacementTag.PlacementsByLocationId.TryGetValue(locationId, out AbstractPlacement? pmt) || pmt == null)
            return;

        bool newlyObtained = false;
        bool anyUnobtained = false;

        foreach (AbstractItem item in pmt.Items)
        {
            if (!item.GetTag<HkItemTag>(out HkItemTag tag) || tag == null)
            {
                anyUnobtained = true;
                continue;
            }
            if (item.WasEverObtained()) continue;
            if (tag.Location != locationId)
            {
                anyUnobtained = true;
                continue;
            }

            newlyObtained = true;
            pmt.AddVisitFlag(VisitState.ObtainedAnyItem);
            item.Give(pmt, (silent ? SilentGiveInfo : RemoteGiveInfo).Clone());
        }

        if (newlyObtained && !anyUnobtained)
        {
            pmt.AddVisitFlag(VisitState.Opened | VisitState.Dropped | VisitState.Accepted | VisitState.ObtainedAnyItem);
        }
    }

    private bool TryGrantViaItemChanger(ApEvent evt, RoutedItem routed)
    {
        if (!_icActive || !ItemChangerMod.IsSettingsLoaded) return false;

        ApClient? client = ApManager.Instance?.Client;
        bool fromOwnHkLocation = client != null
            && evt.SenderSlot == client.Slot
            && IdSpace.IsHollowKnight(evt.LocationId);

        if (fromOwnHkLocation)
        {
            // If we already picked this item up locally in HK, avoid double-granting.
            if (HkPlacementTag.PlacementsByLocationId.TryGetValue(evt.LocationId, out AbstractPlacement? pmt))
            {
                foreach (AbstractItem existing in pmt.Items)
                {
                    if (existing.GetTag<HkItemTag>(out HkItemTag tag) && tag != null && tag.Location == evt.LocationId)
                    {
                        if (existing.WasEverObtained())
                        {
                            HkssPlugin.Log.LogDebug($"[HK] Skipping echo of already-obtained local check {evt.LocationId} ({routed.RawName}).");
                            return true;
                        }
                        MarkLocationAsChecked(evt.LocationId, silent: !evt.IsLiveReceipt);
                        return true;
                    }
                }
            }
        }

        bool isShared = CrossGameItems.TryGet(routed.CombinedItemId, out CrossGameItem? sharedItem);
        string icItemName = isShared ? sharedItem!.HollowKnightItem : IdSpace.StripPrefix(routed.DisplayName);
        AbstractItem? item = Finder.GetItem(icItemName);
        if (item == null)
        {
            HkssPlugin.Log.LogWarning($"[HK] Finder.GetItem('{icItemName}') returned null; falling back to thin-slice.");
            ThinSlice.GrantItem(routed);
            return true;
        }

        if (item.UIDef != null)
        {
            if (isShared)
            {
                item.UIDef = HkUIDef.CreateForSharedItem(item, sharedItem!.Name, evt.SenderName);
            }
            else if (!string.IsNullOrEmpty(evt.SenderName))
            {
                item.UIDef = HkUIDef.CreateForReceivedItem(item, evt.SenderName);
            }
        }

        HkRemotePlacement remotePmt = HkRemotePlacement.GetOrAddSingleton();
        item.Load();
        remotePmt.Add(item);
        GiveInfo giveInfo = (evt.IsLiveReceipt ? RemoteGiveInfo : SilentGiveInfo).Clone();
        item.Give(remotePmt, giveInfo);
        return true;
    }

    private void OnSaveGame(int saveSlot)
    {
        ApManager.Instance?.FlushProcessedItemsToDisk();
        if (_icActive)
        {
            SaveItemChangerProfile();
        }
    }

    private void SaveItemChangerProfile()
    {
        if (_icMod == null || string.IsNullOrEmpty(_icSavePath)) return;
        try
        {
            SaveSettings save = ((ILocalSettings<SaveSettings>)_icMod).OnSaveLocal();
            if (save.value == null) return;

            string dir = Path.GetDirectoryName(_icSavePath)!;
            Directory.CreateDirectory(dir);
            string tmp = _icSavePath + ".tmp";
            string json = JsonConvert.SerializeObject(save, IcJsonSettings);
            File.WriteAllText(tmp, json);
            if (File.Exists(_icSavePath)) File.Replace(tmp, _icSavePath, null);
            else File.Move(tmp, _icSavePath);
            if (!string.IsNullOrEmpty(_loadedSeedKey))
                TryWriteSeedMarker(_loadedSeedKey);
            HkssPlugin.Log.LogDebug($"[HK] Saved ItemChanger state to {_icSavePath}");
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Failed to save ItemChanger state: {ex}");
        }
    }

    private void TryWriteSeedMarker(string seedKey)
    {
        if (string.IsNullOrEmpty(_icSeedPath) || string.IsNullOrEmpty(seedKey)) return;
        try
        {
            string dir = Path.GetDirectoryName(_icSeedPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_icSeedPath, seedKey);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK] Failed to write ItemChanger seed marker: {ex.Message}");
        }
    }

    private void TryWriteSlotDataCache(HkSlotData slotData)
    {
        if (string.IsNullOrEmpty(_slotDataPath)) return;
        try
        {
            string dir = Path.GetDirectoryName(_slotDataPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_slotDataPath, JsonConvert.SerializeObject(slotData));
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[HK] Failed to write slotdata cache: {ex.Message}");
        }
    }

    private void TryLoadSavedItemChangerProfile()
    {
        if (!string.IsNullOrEmpty(_slotDataPath) && File.Exists(_slotDataPath))
        {
            try
            {
                CurrentSlotData = JsonConvert.DeserializeObject<HkSlotData>(File.ReadAllText(_slotDataPath));
            }
            catch (Exception ex)
            {
                HkssPlugin.Log.LogWarning($"[HK] Failed to load slotdata cache: {ex.Message}");
            }
        }

        if (_icMod == null || string.IsNullOrEmpty(_icSavePath) || !File.Exists(_icSavePath)) return;
        try
        {
            string json = File.ReadAllText(_icSavePath);
            SaveSettings loaded = JsonConvert.DeserializeObject<SaveSettings>(json, IcJsonSettings);
            if (loaded.value != null)
            {
                ((ILocalSettings<SaveSettings>)_icMod).OnLoadLocal(loaded);
                _icActive = true;
                ThinSlice.SuppressCheckHooks = true;
                if (!string.IsNullOrEmpty(_icSeedPath) && File.Exists(_icSeedPath))
                    _loadedSeedKey = File.ReadAllText(_icSeedPath).Trim();
                HkssPlugin.Log.LogInfo($"[HK] Restored ItemChanger state from {_icSavePath} ({loaded.value.Placements.Count} placements).");
            }
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Failed to load ItemChanger state from {_icSavePath}: {ex}");
        }
    }

    private static HkSlotData? ExtractHkSlotData(Dictionary<string, object>? slotData)
    {
        if (slotData == null) return null;
        if (!slotData.TryGetValue("hollow_knight", out object? raw) || raw == null) return null;
        try
        {
            string json = JsonConvert.SerializeObject(raw);
            return JsonConvert.DeserializeObject<HkSlotData>(json);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Failed to parse slot_data['hollow_knight']: {ex}");
            return null;
        }
    }
}

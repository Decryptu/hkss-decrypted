using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Runtime detection and check reporting for Hollow Knight extra check types:
/// levers, breakable walls (including quake floors), and cuttable grass.
/// Objects keep their normal vanilla behavior and also send their Archipelago check.
/// </summary>
internal static class HkExtraChecks
{
    private static ManualLogSource? _log;
    private static readonly HashSet<long> ActiveIds = new();
    private static readonly List<ExtraCheckEntry> ActiveEntries = new();
    private static bool _activeSynced;

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        _log = log;
        ApManager.OnConnected += _ => RefreshActiveChecks();
        ApManager.OnDisconnected += ClearActiveChecks;

        try
        {
            harmony.CreateClassProcessor(typeof(Patches)).Patch();
        }
        catch (Exception ex)
        {
            log.LogWarning($"[HK ExtraChecks] Failed to install hooks: {ex.Message}");
        }
    }

    internal static IReadOnlyList<ExtraCheckEntry> GetActiveChecks()
    {
        if (!_activeSynced)
            RefreshActiveChecks();
        return ActiveEntries;
    }

    internal static bool IsActive(long locationId)
    {
        if (!_activeSynced)
            RefreshActiveChecks();
        return ActiveIds.Contains(locationId);
    }

    internal static bool IsChecked(long locationId)
    {
        var checkedLocations = ApManager.Instance?.Client?.Session?.Locations?.AllLocationsChecked;
        if (checkedLocations != null && checkedLocations.Contains(locationId))
            return true;

        IReadOnlyList<long> saved = HkssPlugin.State.GetCheckedLocations(GameId.HollowKnight);
        for (int i = 0; i < saved.Count; i++)
        {
            if (saved[i] == locationId)
                return true;
        }
        return false;
    }

    private static void ClearActiveChecks()
    {
        ActiveIds.Clear();
        ActiveEntries.Clear();
        _activeSynced = false;
    }

    private static void RefreshActiveChecks()
    {
        ActiveIds.Clear();
        ActiveEntries.Clear();

        ApClient? client = ApManager.Instance?.Client;
        var session = client?.Session;
        if (session?.Locations?.AllLocations == null)
        {
            _activeSynced = false;
            return;
        }

        foreach (long id in session.Locations.AllLocations)
        {
            if (ExtraChecksCatalog.TryGetById(id, out ExtraCheckEntry? entry)
                && entry != null
                && entry.Game == GameId.HollowKnight)
            {
                if (ActiveIds.Add(id))
                    ActiveEntries.Add(entry);
            }
        }
        _activeSynced = true;
        if (ActiveEntries.Count > 0)
        {
            _log?.LogInfo($"[HK ExtraChecks] Tracking {ActiveEntries.Count} extra checks in this seed.");
            HkCheckMap.MarkDirty();
        }
    }

    private static void TrySendCheck(ExtraCheckEntry? entry)
    {
        if (entry == null) return;
        if (!IsActive(entry.Id)) return;
        if (IsChecked(entry.Id)) return;

        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        ap.ReportCheckById(entry.Id);
        HkCheckMap.MarkDirty();
        _log?.LogInfo($"[HK ExtraChecks] Sent {entry.Name} ({entry.Id}) in {entry.Scene}.");
    }

    private static void HandlePersistentBool(string sceneName, string objectName, float x, float y, bool hasPosition)
    {
        if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(objectName)) return;
        if (!_activeSynced) RefreshActiveChecks();
        if (ActiveIds.Count == 0) return;

        ExtraCheckEntry? match = null;
        if (hasPosition)
        {
            match = ExtraChecksCatalog.FindObjectCheck(GameId.HollowKnight, sceneName, ExtraCheckKind.Lever, objectName, x, y)
                ?? ExtraChecksCatalog.FindObjectCheck(GameId.HollowKnight, sceneName, ExtraCheckKind.BreakableWall, objectName, x, y);
        }
        else
        {
            // SceneData.SaveMyState does not carry world coordinates; match uniquely by scene + object name.
            foreach (ExtraCheckEntry c in ExtraChecksCatalog.GetForScene(GameId.HollowKnight, sceneName))
            {
                if ((c.Kind == ExtraCheckKind.Lever || c.Kind == ExtraCheckKind.BreakableWall)
                    && string.Equals(c.ObjectName, objectName, StringComparison.OrdinalIgnoreCase)
                    && IsActive(c.Id)
                    && !IsChecked(c.Id))
                {
                    match = c;
                    break;
                }
            }
        }
        TrySendCheck(match);
    }

    private static void HandleGrassCut(string? sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        if (!_activeSynced) RefreshActiveChecks();
        if (ActiveIds.Count == 0) return;

        TrySendCheck(ExtraChecksCatalog.FindGrassCheck(GameId.HollowKnight, sceneName!));
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(PersistentBoolItem), nameof(PersistentBoolItem.SaveState))]
        [HarmonyPostfix]
        private static void OnPersistentBoolSave(PersistentBoolItem __instance)
        {
            try
            {
                if (__instance == null || __instance.persistentBoolData == null || !__instance.persistentBoolData.activated)
                    return;
                string scene = !string.IsNullOrEmpty(__instance.persistentBoolData.sceneName)
                    ? __instance.persistentBoolData.sceneName
                    : __instance.gameObject.scene.name;
                string objName = !string.IsNullOrEmpty(__instance.persistentBoolData.id)
                    ? __instance.persistentBoolData.id
                    : __instance.gameObject.name;
                Vector3 pos = __instance.transform.position;
                HandlePersistentBool(scene, objName, pos.x, pos.y, hasPosition: true);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] PersistentBoolItem hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(SceneData), nameof(SceneData.SaveMyState), typeof(PersistentBoolData))]
        [HarmonyPostfix]
        private static void OnSceneDataSaveBool(PersistentBoolData persistentBoolData)
        {
            try
            {
                if (persistentBoolData == null || !persistentBoolData.activated)
                    return;
                HandlePersistentBool(persistentBoolData.sceneName, persistentBoolData.id, 0f, 0f, hasPosition: false);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] SceneData hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(BridgeLever), "OpenBridge")]
        [HarmonyPrefix]
        private static void OnBridgeLeverOpen(BridgeLever __instance)
        {
            try
            {
                if (__instance == null) return;
                Vector3 pos = __instance.transform.position;
                HandlePersistentBool(__instance.gameObject.scene.name, __instance.gameObject.name, pos.x, pos.y, hasPosition: true);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] BridgeLever hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(GrassCut), "OnTriggerEnter2D")]
        [HarmonyPostfix]
        private static void OnGrassCutTrigger(GrassCut __instance, Collider2D collision)
        {
            try
            {
                if (__instance == null || collision == null || !GrassCut.ShouldCut(collision))
                    return;
                HandleGrassCut(__instance.gameObject.scene.name);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] GrassCut hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Grass), nameof(Grass.Hit))]
        [HarmonyPostfix]
        private static void OnGrassHit(Grass __instance, HitInstance damageInstance)
        {
            try
            {
                if (__instance == null || damageInstance.DamageDealt <= 0)
                    return;
                HandleGrassCut(__instance.gameObject.scene.name);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] Grass hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(TownGrass), "OnTriggerEnter2D")]
        [HarmonyPostfix]
        private static void OnTownGrassTrigger(TownGrass __instance, Collider2D collision)
        {
            try
            {
                if (__instance == null || collision == null || !GrassCut.ShouldCut(collision))
                    return;
                HandleGrassCut(__instance.gameObject.scene.name);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[HK ExtraChecks] TownGrass hook error: {ex.Message}");
            }
        }
    }
}

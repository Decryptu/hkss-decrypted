using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using SilksongRandomizer;
using SilksongRandomizer.Patches;
using UnityEngine;

namespace HKSS.Silksong;

/// <summary>
/// Runtime detection, check reporting, and map marker positions for Silksong extra check types:
/// levers, breakable walls, and cuttable grass.
/// Objects keep their normal vanilla behavior and also send their Archipelago check.
/// </summary>
internal static class SsExtraChecks
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
            log.LogWarning($"[SS ExtraChecks] Failed to install hooks: {ex.Message}");
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

    internal static bool IsChecked(ExtraCheckEntry entry)
    {
        var checkedLocations = ApManager.Instance?.Client?.Session?.Locations?.AllLocationsChecked;
        if (checkedLocations != null && checkedLocations.Contains(entry.Id))
            return true;

        SaveState? state = SaveState.Instance;
        if (state != null && state.IsLocationChecked(entry.Name))
            return true;

        IReadOnlyList<long> saved = HkssPlugin.State.GetCheckedLocations(GameId.Silksong);
        for (int i = 0; i < saved.Count; i++)
        {
            if (saved[i] == entry.Id)
                return true;
        }
        return false;
    }

    internal static IEnumerable<MapCheckPosition> Positions(SaveState? state, bool includeChecked)
    {
        IReadOnlyList<ExtraCheckEntry> active = GetActiveChecks();
        for (int i = 0; i < active.Count; i++)
        {
            ExtraCheckEntry entry = active[i];
            if (!includeChecked && IsChecked(entry))
                continue;
            if (state != null && !state.IsLocationInSeed(entry.Name))
                continue;

            bool isGrass = entry.Kind == ExtraCheckKind.Grass;
            yield return new MapCheckPosition(
                entry.Name,
                entry.Scene,
                isGrass ? 0.5f : entry.X,
                isGrass ? 0.5f : entry.Y,
                isGrass ? 1f : Math.Max(1f, entry.SceneWidth),
                isGrass ? 1f : Math.Max(1f, entry.SceneHeight),
                isGrass ? MapMarkerPositionConfidence.RoomCenter : MapMarkerPositionConfidence.ExactUpstream
            );
        }
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
                && entry.Game == GameId.Silksong)
            {
                if (ActiveIds.Add(id))
                    ActiveEntries.Add(entry);
            }
        }
        _activeSynced = true;
        if (ActiveEntries.Count > 0)
        {
            _log?.LogInfo($"[SS ExtraChecks] Tracking {ActiveEntries.Count} extra checks in this seed.");
            CheckMapMarkerManager.RefreshCurrentMap();
        }
    }

    private static void TrySendCheck(ExtraCheckEntry? entry)
    {
        if (entry == null) return;
        if (!IsActive(entry.Id)) return;
        if (IsChecked(entry)) return;

        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        ap.ReportCheckById(entry.Id);
        SaveState.Instance?.CheckLocation(entry.Name);
        CheckMapMarkerManager.RefreshCurrentMap();
        _log?.LogInfo($"[SS ExtraChecks] Sent {entry.Name} ({entry.Id}) in {entry.Scene}.");
    }

    private static void HandleObjectTrigger(Component component, ExtraCheckKind kind)
    {
        if (component == null) return;
        if (!_activeSynced) RefreshActiveChecks();
        if (ActiveIds.Count == 0) return;

        GameObject go = component.gameObject;
        Vector3 pos = go.transform.position;
        ExtraCheckEntry? match = ExtraChecksCatalog.FindObjectCheck(
            GameId.Silksong,
            go.scene.name,
            kind,
            go.name,
            pos.x,
            pos.y);
        TrySendCheck(match);
    }

    private static void HandleGrassCut(Component component)
    {
        if (component == null) return;
        if (!_activeSynced) RefreshActiveChecks();
        if (ActiveIds.Count == 0) return;

        TrySendCheck(ExtraChecksCatalog.FindGrassCheck(GameId.Silksong, component.gameObject.scene.name));
    }

    private static class Patches
    {
        [HarmonyPatch(typeof(Lever), nameof(Lever.SetActivated), typeof(bool))]
        [HarmonyPostfix]
        private static void OnLeverSetActivated(Lever __instance, bool fromStart)
        {
            try
            {
                if (__instance == null || fromStart) return;
                HandleObjectTrigger(__instance, ExtraCheckKind.Lever);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] Lever hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Lever_tk2d), nameof(Lever_tk2d.Hit))]
        [HarmonyPostfix]
        private static void OnLeverTk2dHit(Lever_tk2d __instance, IHitResponder.HitResponse __result)
        {
            try
            {
                if (__instance is null || !__instance || __result.response == IHitResponder.Response.None) return;
                HandleObjectTrigger(__instance, ExtraCheckKind.Lever);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] Lever_tk2d hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(BridgeLever), "OpenBridge")]
        [HarmonyPrefix]
        private static void OnBridgeLeverOpen(BridgeLever __instance)
        {
            try
            {
                if (__instance == null) return;
                HandleObjectTrigger(__instance, ExtraCheckKind.Lever);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] BridgeLever hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Breakable), nameof(Breakable.Break))]
        [HarmonyPostfix]
        private static void OnBreakableBreak(Breakable __instance)
        {
            try
            {
                if (__instance == null) return;
                HandleObjectTrigger(__instance, ExtraCheckKind.BreakableWall);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] Breakable hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(GrassCut), "DoCut")]
        [HarmonyPostfix]
        private static void OnGrassCut(GrassCut __instance)
        {
            try
            {
                if (__instance == null) return;
                HandleGrassCut(__instance);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] GrassCut hook error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Grass), nameof(Grass.Hit))]
        [HarmonyPostfix]
        private static void OnGrassHit(Grass __instance, IHitResponder.HitResponse __result)
        {
            try
            {
                if (__instance is null || !__instance || __result.response == IHitResponder.Response.None) return;
                HandleGrassCut(__instance);
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[SS ExtraChecks] Grass hook error: {ex.Message}");
            }
        }
    }
}

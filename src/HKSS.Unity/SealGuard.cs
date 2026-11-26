using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Enforces cross-game seals: a doorway into a sealed area sends the player back through the gate
/// they used until the seal's key (an item from the other game) has been received.
/// Warps, stags, trams and bellways are not stopped; the randomizer's logic never expects them
/// to reach a sealed area without its key.
/// </summary>
internal static class SealGuard
{
    private const float ToastCooldownSeconds = 1.5f;

    private static object? _slotData;
    private static CrossGameSeals _seals = CrossGameSeals.None;
    private static readonly HashSet<long> KnownKeys = new();
    private static float _lastToast = -100f;

    internal static void Install(Harmony harmony)
    {
#if HOLLOW_KNIGHT
        ItemChanger.Events.TransitionBlocker = (source, target) => ShouldBlock(source.SceneName, target.SceneName);
#elif SILKSONG
        harmony.CreateClassProcessor(typeof(RecordSourceGate)).Patch();
        harmony.CreateClassProcessor(typeof(BounceSealedTransition)).Patch();
#endif
    }

    /// <summary>Seals for the connected slot. Seals are known once the slot data has been read.</summary>
    internal static CrossGameSeals Current
    {
        get
        {
            Dictionary<string, object>? slotData = ApManager.Instance?.Client?.SlotData;
            if (slotData != null && !ReferenceEquals(slotData, _slotData))
            {
                _slotData = slotData;
                _seals = CrossGameSeals.Parse(slotData);
                KnownKeys.Clear();
            }
            return _seals;
        }
    }

    internal static bool HasKey(long keyId)
    {
        if (KnownKeys.Contains(keyId)) return true;
        try
        {
            if (HkssPlugin.State.GetSealKeys().Contains(keyId))
            {
                KnownKeys.Add(keyId);
                return true;
            }
            var received = ApManager.Instance?.Client?.Session?.Items?.AllItemsReceived;
            if (received != null && received.Any(item => item.ItemId == keyId))
            {
                KnownKeys.Add(keyId);
                HkssPlugin.State.RecordSealKeys(new[] { keyId });
                return true;
            }
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"Could not check seal key {keyId}: {e.Message}");
        }
        return false;
    }

    private static float _missingKeysCheckedAt = -100f;
    private static readonly HashSet<long> MissingKeys = new();

    /// <summary>
    /// Key status for display. Missing keys are rechecked at most once a second, because the
    /// check reads <c>state.json</c> and scans the received items.
    /// </summary>
    internal static bool HasKeyForDisplay(long keyId)
    {
        if (KnownKeys.Contains(keyId)) return true;
        if (Time.unscaledTime - _missingKeysCheckedAt > 1f)
        {
            _missingKeysCheckedAt = Time.unscaledTime;
            MissingKeys.Clear();
        }
        if (MissingKeys.Contains(keyId)) return false;
        if (HasKey(keyId)) return true;
        MissingKeys.Add(keyId);
        return false;
    }

    /// <summary>Scenes in this game that belong to seals whose key has not been received.</summary>
    internal static HashSet<string> ClosedScenes()
    {
        var scenes = new HashSet<string>(StringComparer.Ordinal);
        foreach (CrossGameSeal seal in Current.For(HkssPlugin.ThisGame))
        {
            if (!HasKeyForDisplay(seal.KeyId)) scenes.UnionWith(seal.Scenes);
        }
        return scenes;
    }

    internal static bool ShouldBlock(string? fromScene, string? toScene)
    {
        CrossGameSeals seals = Current;
        if (seals.All.Count == 0) return false;
        CrossGameSeal? seal = seals.Blocking(HkssPlugin.ThisGame, fromScene, toScene, HasKey);
        if (seal == null) return false;

        HkssPlugin.Log.LogInfo($"Sealed: {fromScene} -> {toScene} ({seal.Describe()})");
        if (Time.unscaledTime - _lastToast > ToastCooldownSeconds)
        {
            _lastToast = Time.unscaledTime;
            ApManager.Instance?.PushToast($"{seal.Area} is sealed. Find {seal.KeyDisplayName} in {seal.KeyWorld}.", ToastKind.Error);
        }
        return true;
    }

#if SILKSONG
    private static string? _sourceScene;
    private static string? _sourceGate;

    [HarmonyPatch(typeof(TransitionPoint), "DoSceneTransition")]
    private static class RecordSourceGate
    {
        private static void Prefix(TransitionPoint __instance)
        {
            _sourceScene = __instance.gameObject.scene.name;
            _sourceGate = __instance.gameObject.name.Split(null)[0];
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.BeginSceneTransition))]
    private static class BounceSealedTransition
    {
        // Runs before the entrance randomizer confirms a departure, which sees the bounce as a
        // cancelled exit and records nothing.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameManager __instance, GameManager.SceneLoadInfo info)
        {
            string? scene = _sourceScene;
            string? gate = _sourceGate;
            _sourceScene = _sourceGate = null;
            if (info == null || scene == null || string.IsNullOrEmpty(gate)) return;
            if (__instance.GetSceneNameString() != scene) return;
            if (!ShouldBlock(scene, info.SceneName)) return;
            info.SceneName = scene;
            info.EntryGateName = gate;
        }
    }
#endif
}

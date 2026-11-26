using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Enforces cross-game seals: doorways into a sealed area send the player back through the gate
/// they used, and fast-travel warps (Benchwarp, Stags, Trams, Dream Gate, Bellways, Ventricas,
/// and Beastling Call) refuse entry until the seal's key (an item from the other game) has been
/// received.
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
        ItemChanger.Events.AddFsmEdit(new ItemChanger.FsmID("Knight", "Dream Nail"), EditDreamNailForSeals);
        harmony.CreateClassProcessor(typeof(BlockSealedHkWarpTransitions)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedStagTravel)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedDreamGate)).Patch();
#elif SILKSONG
        harmony.CreateClassProcessor(typeof(RecordSourceGate)).Patch();
        harmony.CreateClassProcessor(typeof(BounceSealedTransition)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedBellwayButton)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedVentricaButton)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedFastTravelAction)).Patch();
        harmony.CreateClassProcessor(typeof(BlockSealedFastTravelCutscene)).Patch();
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

    /// <summary>Area IDs in this game that belong to seals whose key has not been received.</summary>
    internal static HashSet<string> ClosedAreaIds() =>
        Current.ClosedAreaIds(HkssPlugin.ThisGame, HasKeyForDisplay);

    /// <summary>Returns the closed seal containing <paramref name="scene"/> in this game, or null.</summary>
    internal static CrossGameSeal? ClosedForScene(string? scene) =>
        Current.ClosedForScene(HkssPlugin.ThisGame, scene, HasKeyForDisplay);

    /// <summary>Display-only check for whether travelling from <paramref name="fromScene"/> to <paramref name="toScene"/> is sealed.</summary>
    internal static bool IsBlockedForDisplay(string? fromScene, string? toScene)
    {
        CrossGameSeals seals = Current;
        return seals.All.Count > 0 && seals.Blocking(HkssPlugin.ThisGame, fromScene, toScene, HasKeyForDisplay) != null;
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

#if HOLLOW_KNIGHT
    private const string StagCutsceneScene = "Cinematic_Stag_travel";
    private static string? _stagDepartureScene;

    private static void EditDreamNailForSeals(PlayMakerFSM fsm)
    {
        if (fsm == null) return;
        HutongGames.PlayMaker.FsmState? canWarp = fsm.FsmStates.FirstOrDefault(s => s.Name == "Can Warp?");
        if (canWarp == null) return;
        string? failEvent = canWarp.Transitions
            .Select(t => t.EventName)
            .FirstOrDefault(name =>
                string.Equals(name, "FAIL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "CANCEL", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(failEvent)) return;

        var actions = canWarp.Actions?.ToList() ?? new List<HutongGames.PlayMaker.FsmStateAction>();
        actions.Insert(0, new ItemChanger.FsmStateActions.Lambda(() =>
        {
            if (ShouldBlock(GameManager._instance?.sceneName, PlayerData.instance?.dreamGateScene))
                fsm.SendEvent(failEvent);
        }));
        canWarp.Actions = actions.ToArray();
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.BeginSceneTransition))]
    private static class BlockSealedHkWarpTransitions
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameManager __instance, GameManager.SceneLoadInfo info)
        {
            if (__instance == null || info == null) return;
            string currentScene = __instance.sceneName ?? string.Empty;

            if (string.Equals(info.SceneName, StagCutsceneScene, StringComparison.Ordinal))
            {
                _stagDepartureScene = currentScene;
                string? nextScene = __instance.playerData?.nextScene;
                if (ShouldBlock(currentScene, nextScene))
                {
                    if (__instance.playerData != null)
                        __instance.playerData.nextScene = currentScene;
                    info.SceneName = currentScene;
                    info.EntryGateName = "door_stagExit";
                }
                return;
            }

            if ((string.Equals(currentScene, "Room_Tram", StringComparison.Ordinal)
                || string.Equals(currentScene, "Room_Tram_RG", StringComparison.Ordinal))
                && ShouldBlock(currentScene, info.SceneName))
            {
                info.SceneName = currentScene;
                info.EntryGateName = "door_tram";
            }
        }
    }

    [HarmonyPatch(typeof(StagTravel), "Start")]
    private static class BlockSealedStagTravel
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            PlayerData? pd = PlayerData.instance;
            if (pd == null) return;
            if (ShouldBlock(_stagDepartureScene, pd.nextScene))
            {
                pd.nextScene = !string.IsNullOrEmpty(_stagDepartureScene)
                    ? _stagDepartureScene!
                    : "Room_Town_Stag_Station";
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.WarpToDreamGate))]
    private static class BlockSealedDreamGate
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameManager __instance)
        {
            if (__instance == null) return true;
            if (!ShouldBlock(__instance.sceneName, __instance.playerData?.dreamGateScene))
                return true;

            HeroController.instance?.RegainControl();
            GameCameras.instance?.cameraFadeFSM?.SendEvent("FADE SCENE IN");
            return false;
        }
    }
#elif SILKSONG
    private static string? _sourceScene;
    private static string? _sourceGate;
    private static string? _fastTravelDepartureScene;

    private static readonly FieldInfo? BellwayTargetField =
        AccessTools.Field(typeof(FastTravelMapButtonBase<GlobalEnums.FastTravelLocations>), "targetLocation");
    private static readonly FieldInfo? VentricaTargetField =
        AccessTools.Field(typeof(FastTravelMapButtonBase<GlobalEnums.TubeTravelLocations>), "targetLocation");

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
            string currentScene = __instance.GetSceneNameString();
            if (!string.IsNullOrEmpty(currentScene) && Current.ClosedForScene(GameId.Silksong, currentScene, HasKey) == null)
                _fastTravelDepartureScene = currentScene;

            string? scene = _sourceScene;
            string? gate = _sourceGate;
            _sourceScene = _sourceGate = null;
            if (info == null || scene == null || string.IsNullOrEmpty(gate)) return;
            if (currentScene != scene) return;
            if (!ShouldBlock(scene, info.SceneName)) return;
            info.SceneName = scene;
            info.EntryGateName = gate;
        }
    }

    [HarmonyPatch(typeof(FastTravelMapButtonBase<GlobalEnums.FastTravelLocations>), nameof(FastTravelMapButtonBase<GlobalEnums.FastTravelLocations>.Submit))]
    private static class BlockSealedBellwayButton
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(FastTravelMapButtonBase<GlobalEnums.FastTravelLocations> __instance)
        {
            if (BellwayTargetField == null || __instance == null) return true;
            var location = (GlobalEnums.FastTravelLocations)BellwayTargetField.GetValue(__instance);
            string? targetScene = FastTravelScenes.GetSceneName(location);
            string? currentScene = GameManager._instance?.GetSceneNameString();
            return !ShouldBlock(currentScene, targetScene);
        }
    }

    [HarmonyPatch(typeof(FastTravelMapButtonBase<GlobalEnums.TubeTravelLocations>), nameof(FastTravelMapButtonBase<GlobalEnums.TubeTravelLocations>.Submit))]
    private static class BlockSealedVentricaButton
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(FastTravelMapButtonBase<GlobalEnums.TubeTravelLocations> __instance)
        {
            if (VentricaTargetField == null || __instance == null) return true;
            var location = (GlobalEnums.TubeTravelLocations)VentricaTargetField.GetValue(__instance);
            string? targetScene = FastTravelScenes.GetSceneName(location);
            string? currentScene = GameManager._instance?.GetSceneNameString();
            return !ShouldBlock(currentScene, targetScene);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.GetFastTravelScene), nameof(HutongGames.PlayMaker.Actions.GetFastTravelScene.OnEnter))]
    private static class BlockSealedFastTravelAction
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HutongGames.PlayMaker.Actions.GetFastTravelScene __instance)
        {
            if (__instance?.Location == null || __instance.Location.IsNone) return true;
            var location = (GlobalEnums.FastTravelLocations)(object)__instance.Location.Value;
            string? targetScene = FastTravelScenes.GetSceneName(location);
            string? currentScene = GameManager._instance?.GetSceneNameString();
            if (!ShouldBlock(currentScene, targetScene)) return true;

            if (__instance.InvalidEvent != null)
            {
                if (__instance.StoreSceneName != null)
                    __instance.StoreSceneName.Value = string.Empty;
                __instance.Fsm.Event(__instance.InvalidEvent);
                __instance.Finish();
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(FastTravelCutscene), "DoTransition")]
    private static class BlockSealedFastTravelCutscene
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            PlayerData? pd = GameManager._instance?.playerData;
            if (pd == null) return;
            if (ShouldBlock(_fastTravelDepartureScene, pd.nextScene))
            {
                pd.nextScene = "Bellway_01";
            }
        }
    }
#endif
}

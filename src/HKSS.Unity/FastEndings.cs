using System;
using System.Collections;
using GlobalEnums;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Speeds up post-boss ending cinematics, credits, and completion screens in both games using
/// each game's own native skip pipeline so goal reporting, completion stats, save writes, and
/// Silksong's Act 2 -&gt; Act 3 transition all run intact.
/// </summary>
internal sealed class FastEndings : MonoBehaviour
{
    private const float SettleDelaySeconds = 0.35f;
    private const float RetryIntervalSeconds = 0.25f;
    private const float VideoTriggerWaitCapSeconds = 4f;

    internal static FastEndingsMode Mode { get; set; } = FastEndingsMode.Credits;

    private string _activeScene = string.Empty;
    private float _sceneEnteredAt = -1f;
    private float _nextSkipAt = -1f;
    private bool _loggedAutoSkip;

    internal static void Install(Harmony harmony)
    {
        Mode = FastEndingsPolicy.Parse(Environment.GetEnvironmentVariable(FastEndingsPolicy.EnvVariable));
        HkssPlugin.Log.LogInfo($"[endings] Fast endings mode: {FastEndingsPolicy.ToKey(Mode)}");

        TryPatch(harmony, typeof(UnlockSkipModePatch), "UnlockSkipModePatch");
        TryPatch(harmony, typeof(UnlockMakeSkippablePatch), "UnlockMakeSkippablePatch");
#if SILKSONG
        TryPatch(harmony, typeof(UnlockSkippableSequencePatch), "UnlockSkippableSequencePatch");
#endif
    }

    private static void TryPatch(Harmony harmony, Type patchType, string name)
    {
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogWarning($"[endings] Failed to install {name}: {ex.Message}");
        }
    }

    private void OnEnable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;

    private void OnDisable() => UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
    {
        _activeScene = to.name ?? string.Empty;
        _sceneEnteredAt = Time.unscaledTime;
        _nextSkipAt = _sceneEnteredAt + SettleDelaySeconds;
        _loggedAutoSkip = false;
    }

    private void Update()
    {
        if (!FastEndingsPolicy.ShouldAutoSkip(Mode, _activeScene)) return;
        if (Time.unscaledTime < _nextSkipAt) return;

        GameManager? gm = GameManager._instance;
        if (gm == null || gm.IsInSceneTransition) return;

        if (gm.inputHandler != null && gm.inputHandler.skippingCutscene)
        {
            _nextSkipAt = Time.unscaledTime + 0.15f;
            return;
        }

        // In ending video scenes, wait until CinematicPlayer has actually started playback so
        // its Skip() coroutine stops the video and runs FinishVideo() cleanly.
        CinematicPlayer? player = FindFirstObjectByType<CinematicPlayer>();
        if (player != null)
        {
            bool videoTriggered = Traverse.Create(player).Field("videoTriggered").GetValue<bool>();
            if (!videoTriggered && Time.unscaledTime - _sceneEnteredAt < VideoTriggerWaitCapSeconds)
            {
                _nextSkipAt = Time.unscaledTime + 0.1f;
                return;
            }
        }

#if SILKSONG
        // Silksong's End_Game_Completion uses EndingSequence -> ChainSequence whose child steps
        // may start locked; unlock the active step before calling GameManager.SkipCutscene().
        EndingSequence? endingSeq = FindFirstObjectByType<EndingSequence>();
        if (endingSeq != null)
        {
            var chain = Traverse.Create(endingSeq).Field("chainSequence").GetValue<ChainSequence>();
            if (chain != null && chain.IsPlaying)
            {
                Traverse.Create(chain).Property("CurrentSequence").GetValue<SkippableSequence>()?.AllowSkip();
            }
        }
#endif

        IList? skippables = Traverse.Create(gm).Field("skippables").GetValue<IList>();
        if (skippables == null || skippables.Count == 0)
        {
            _nextSkipAt = Time.unscaledTime + 0.15f;
            return;
        }

        if (!_loggedAutoSkip)
        {
            _loggedAutoSkip = true;
            HkssPlugin.Log.LogInfo($"[endings] Auto-skipping '{_activeScene}' (mode={FastEndingsPolicy.ToKey(Mode)}).");
        }

        gm.SkipCutscene();
        _nextSkipAt = Time.unscaledTime + RetryIntervalSeconds;
    }

    [HarmonyPatch(typeof(InputHandler), nameof(InputHandler.SetSkipMode))]
    private static class UnlockSkipModePatch
    {
        private static void Prefix(ref SkipPromptMode newMode)
        {
            if (newMode != SkipPromptMode.SKIP_PROMPT && newMode != SkipPromptMode.NOT_SKIPPABLE) return;
            string? scene = GameManager._instance?.sceneName;
            if (!FastEndingsPolicy.ShouldUnlockManualSkip(Mode, scene)) return;
            newMode = SkipPromptMode.SKIP_INSTANT;
        }
    }

    [HarmonyPatch(typeof(MakeSkippable), "Start")]
    private static class UnlockMakeSkippablePatch
    {
        private static void Prefix(ref float ___unlockAfterSec)
        {
            string? scene = GameManager._instance?.sceneName;
            if (!FastEndingsPolicy.ShouldUnlockManualSkip(Mode, scene)) return;
            ___unlockAfterSec = 0f;
        }
    }

#if SILKSONG
    [HarmonyPatch(typeof(SkippableSequence), nameof(SkippableSequence.CanSkip), MethodType.Getter)]
    private static class UnlockSkippableSequencePatch
    {
        private static void Prefix(SkippableSequence __instance)
        {
            if (__instance == null) return;
            string? scene = GameManager._instance?.sceneName;
            if (!FastEndingsPolicy.ShouldUnlockManualSkip(Mode, scene)) return;
            __instance.AllowSkip();
        }
    }
#endif
}

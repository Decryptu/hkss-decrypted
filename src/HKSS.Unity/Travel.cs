using System.Collections;
using GlobalEnums;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>Saves, hands over to the launcher's travel screen and closes this game.</summary>
internal sealed class Travel : MonoBehaviour
{
    private const float FadeTime = 0.7f;
    private const float CoverTimeout = 3f;

    private bool _travelling;

#if DEBUG
    private float _autoTravelAt = -1f;
    private float _nextReport;
#endif

    internal static bool CanTravel()
    {
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        return AutoLoad.IsReady && gm != null && hero != null && hero.CanInput()
            && !gm.IsInSceneTransition && gm.IsGameplayScene() && Time.timeScale > 0f;
    }

    internal bool Begin()
    {
        if (_travelling || !CanTravel()) return false;
        _travelling = true;
        StartCoroutine(Run());
        return true;
    }

    private IEnumerator Run()
    {
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        hero.RelinquishControl();
        gm.cameraCtrl.FadeOut(CameraFadeType.LEVEL_TRANSITION);

        for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
        {
            AudioListener.volume = 1f - t / FadeTime;
            yield return null;
        }
        AudioListener.volume = 0f;

        bool? saved = null;
        gm.SaveGame(ok => saved = ok);
        while (saved == null) yield return null;

        if (saved != true)
        {
            // Never leave with unsaved progress: give the player the game back.
            HkssPlugin.Log.LogError("Saving failed, so travel was cancelled.");
            AudioListener.volume = 1f;
            gm.cameraCtrl.FadeSceneIn();
            hero.RegainControl();
            _travelling = false;
            yield break;
        }

        HkssPlugin.Signals.Send(Signal.Travel, HkssPlugin.ThisGame.Other());
        for (float t = 0f; t < CoverTimeout && !HkssPlugin.Signals.CoverIsUp(); t += Time.unscaledDeltaTime)
            yield return null;
        HkssPlugin.Log.LogInfo("Travelling: save written, closing.");
        Application.Quit();
    }

#if DEBUG
    // Development only: F8 travels, and HKSS_DEV_AUTOTRAVEL=<seconds> travels without input.
    private void Update()
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame) Begin();

        if (!AutoLoad.IsReady || _travelling) return;
        if (_autoTravelAt < 0f)
        {
            string? delay = System.Environment.GetEnvironmentVariable("HKSS_DEV_AUTOTRAVEL");
            _autoTravelAt = float.TryParse(delay, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float seconds)
                ? Time.unscaledTime + seconds
                : float.MaxValue;
        }
        if (Time.unscaledTime < _autoTravelAt || Begin() || Time.unscaledTime < _nextReport) return;
        _nextReport = Time.unscaledTime + 5f;
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        HkssPlugin.Log.LogInfo($"Waiting to travel: scene {gm.sceneName}, transition {gm.IsInSceneTransition}, "
            + $"accepting {hero.acceptingInput}, relinquished {hero.controlReqlinquished}, "
            + $"state {hero.hero_state}, timeScale {Time.timeScale}");
    }
#endif
}

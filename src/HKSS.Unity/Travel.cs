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

    internal static Travel? Instance { get; private set; }
    internal bool IsTravelling => _travelling;

#if DEBUG
    private float _autoTravelAt = -1f;
    private float _nextReport;
#endif

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    internal static bool CanTravel() => TravelGuard.CanLeave(out _);

    internal bool Begin(bool fromGate = false)
    {
        if (_travelling) return false;
        if (!TravelGuard.CanLeave(out string reason))
        {
            HkssPlugin.Log.LogInfo($"Travel refused: {reason}.");
            return false;
        }
        _travelling = true;
        StartCoroutine(Run(fromGate));
        return true;
    }

    private IEnumerator Run(bool fromGate)
    {
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        PlayerData pd = PlayerData.instance;
        hero.RelinquishControl();
        gm.cameraCtrl.FadeOut(CameraFadeType.LEVEL_TRANSITION);

        for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
        {
            AudioListener.volume = 1f - t / FadeTime;
            yield return null;
        }
        AudioListener.volume = 0f;

        string prevRespawnScene = pd != null ? pd.respawnScene : string.Empty;
        string prevRespawnMarker = pd != null ? pd.respawnMarkerName : string.Empty;
        int prevRespawnType = pd != null ? pd.respawnType : 0;
        MapZone prevMapZone = pd != null ? pd.mapZone : MapZone.NONE;
        bool prevAtBench = pd != null && pd.atBench;
#if SILKSONG
        string prevTempScene = pd != null ? pd.tempRespawnScene : string.Empty;
        string prevTempMarker = pd != null ? pd.tempRespawnMarker : string.Empty;
        int prevTempType = pd != null ? pd.tempRespawnType : 0;
#endif

        if (fromGate)
        {
            TownGate.SetGateAsRespawnPoint();
        }

        RunStatsTracker.Flush();
        bool? saved = null;
        gm.SaveGame(ok => saved = ok);
        while (saved == null) yield return null;

        if (saved != true)
        {
            if (fromGate && pd != null)
            {
                pd.respawnScene = prevRespawnScene;
                pd.respawnMarkerName = prevRespawnMarker;
                pd.respawnType = prevRespawnType;
                pd.mapZone = prevMapZone;
                pd.atBench = prevAtBench;
#if SILKSONG
                pd.tempRespawnScene = prevTempScene;
                pd.tempRespawnMarker = prevTempMarker;
                pd.tempRespawnType = prevTempType;
#endif
            }
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
        if (System.Environment.GetEnvironmentVariable("HKSS_DEV_AUTOTRAVEL") != null
            && !ApOverlay.IsPanelOpen && keyboard != null && keyboard.f8Key.wasPressedThisFrame) Begin();

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

using System.Collections;
using GlobalEnums;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HKSS.Unity;

/// <summary>
/// Warps the player back to the current game's town gate (Dirtmouth in Hollow Knight,
/// Bone Bottom in Silksong) on F4, respecting all travel guards and the first-trip rule.
/// </summary>
internal sealed class TownWarp : MonoBehaviour
{
    private bool _warping;

    internal static TownWarp? Instance { get; private set; }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (ApOverlay.IsPanelOpen) return;
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
        {
            TryWarpToTown();
        }
    }

    internal bool TryWarpToTown()
    {
        if (_warping) return false;

        if (!TravelGuard.CanLeave(out string reason))
        {
            HkssPlugin.Log.LogInfo($"Town warp refused: {reason}.");
            return false;
        }

#if SILKSONG
        if (!HkssPlugin.State.Load().BonetownGateOpen)
        {
            HkssPlugin.Log.LogInfo("Town warp refused: reach Bone Bottom first to open its gate.");
            return false;
        }
#endif

        _warping = true;
        StartCoroutine(WarpRoutine());
        return true;
    }

    private IEnumerator WarpRoutine()
    {
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        PlayerData pd = PlayerData.instance;

        if (gm == null || hero == null || pd == null)
        {
            _warping = false;
            yield break;
        }

        HkssPlugin.Log.LogInfo($"Warping to {TownGate.TownScene} gate.");
        TownGate.SetGateAsRespawnPoint();

#if HOLLOW_KNIGHT
        hero.RelinquishControl();
        hero.AffectedByGravity(false);
        hero.transitionState = HeroTransitionState.EXITING_SCENE;

        if (hero.cState.onConveyor || hero.cState.onConveyorV || hero.cState.inConveyorZone)
        {
            hero.GetComponent<ConveyorMovementHero>()?.StopConveyorMove();
            hero.cState.inConveyorZone = false;
            hero.cState.onConveyor = false;
            hero.cState.onConveyorV = false;
        }

        hero.cState.nearBench = false;
        hero.cState.superDashing = false;
        hero.cState.spellQuake = false;
        pd.atBench = false;

        gm.cameraCtrl.FadeOut(CameraFadeType.LEVEL_TRANSITION);
        yield return new WaitForSecondsRealtime(0.4f);

        gm.ReadyForRespawn(isFirstLevelForPlayer: false);
        while (gm.IsInSceneTransition) yield return null;

        Time.timeScale = 1f;
        gm.FadeSceneIn();
#elif SILKSONG
        DeliveryQuestItem.BreakAll();
        pd.travelling = false;
        pd.atBench = false;

        gm.RespawningHero = true;
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo
        {
            SceneName = TownGate.TownScene,
            EntryGateName = TownGate.RespawnMarkerName,
        });
        while (gm.IsInSceneTransition) yield return null;
#endif

        _warping = false;
    }
}

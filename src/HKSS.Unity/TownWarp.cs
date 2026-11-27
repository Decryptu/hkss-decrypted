using System.Collections;
using GlobalEnums;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Warps the player back to the current game's town gate (Dirtmouth in Hollow Knight,
/// Bone Bottom in Silksong) on F4, respecting all travel guards and the first-trip rule.
/// </summary>
internal sealed class TownWarp : MonoBehaviour
{
    private bool _warping;

    internal static TownWarp? Instance { get; private set; }
    internal bool IsWarping => _warping;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (DoubleSteelSoul.IsOverlayVisible || ApOverlay.IsPanelOpen || Benchwarp.IsPanelOpen) return;
        if (PlayerOptions.Pressed(Shortcut.TownWarp))
        {
            TryWarpToTown();
        }
    }

    internal bool TryWarpToTown()
    {
        if (_warping) return false;

        if (!TravelGuard.CanLeave(out string reason))
        {
            ApManager.Instance?.PushToast($"Town warp refused: {reason}", ToastKind.Error);
            HkssPlugin.Log.LogInfo($"Town warp refused: {reason}.");
            return false;
        }

#if SILKSONG
        if (!HkssPlugin.State.Load().BonetownGateOpen && !OpeningMayMissBoneBottom())
        {
            ApManager.Instance?.PushToast("Reach Bone Bottom first to open its gate.", ToastKind.Error);
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

        // Same guard as the randomizer's own Bone Bottom warp: an unfinished opening Moss Mother
        // stays undefeated when Hornet skips past her.
        bool protectedOpeningBoss = SilksongRandomizer.Patches.MossMotherWarpSafety.PrepareForBoneBottomWarp();
        try
        {
            gm.RespawningHero = true;
            gm.BeginSceneTransition(new GameManager.SceneLoadInfo
            {
                SceneName = TownGate.TownScene,
                EntryGateName = TownGate.RespawnMarkerName,
            });
        }
        catch
        {
            if (protectedOpeningBoss) SilksongRandomizer.Patches.MossMotherWarpSafety.CancelPreparedWarp();
            _warping = false;
            throw;
        }
        while (gm.IsInSceneTransition) yield return null;
#endif

        _warping = false;
    }

#if SILKSONG
    /// <summary>
    /// True when this run's Silksong opening might not walk to Bone Bottom without items: an
    /// alternate starting location, or shuffled entrances on the Moss Grotto route. The gate warp
    /// is then open from the first arrival, so the way back to Hallownest never depends on
    /// items that may be in Hallownest.
    /// </summary>
    internal static bool OpeningMayMissBoneBottom()
    {
        SilksongRandomizer.SaveState? state = SilksongRandomizer.SaveState.Instance;
        if (state == null || !state.IsRoomBound) return false;
        bool alternateStart = !string.IsNullOrEmpty(state.startingLocation)
            && state.startingLocation != SilksongRandomizer.Archipelago.StartingLocationVanilla;
        bool shuffledEntrances = !string.IsNullOrEmpty(state.entranceLayoutJson)
            && state.entranceLayoutJson != "{}";
        return alternateStart || shuffledEntrances;
    }
#endif
}

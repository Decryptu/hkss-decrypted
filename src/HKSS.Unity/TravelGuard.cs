using GlobalEnums;
using HarmonyLib;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Decides whether the player can safely leave the current room or game (via a gate or town warp).
/// Refuses without control, during cutscenes, in memories/dreams, and during active boss/arena fights.
/// </summary>
internal static class TravelGuard
{
    internal static bool CanLeave(out string reason, bool recoverInputLock = false)
    {
        if (!AutoLoad.IsReady)
        {
            reason = "still loading";
            return false;
        }

        if (DoubleSteelSoul.IsDefeated)
        {
            reason = "Steel Soul run is defeated";
            return false;
        }

        if (Travel.Instance?.IsTravelling == true
            || TownWarp.Instance?.IsWarping == true
            || Benchwarp.Instance?.IsWarping == true)
        {
            reason = "travel already in progress";
            return false;
        }

        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        PlayerData pd = PlayerData.instance;
        if (gm == null || hero == null || pd == null)
        {
            reason = "not in gameplay";
            return false;
        }

        if (gm.IsInSceneTransition || !gm.IsGameplayScene() || Time.timeScale <= 0f)
        {
            reason = "scene transition or paused";
            return false;
        }

#if HOLLOW_KNIGHT
        if (gm.gameState != GameState.PLAYING || gm.isPaused)
        {
            reason = "not playing (cutscene or menu)";
            return false;
        }
#elif SILKSONG
        if (gm.GameState != GameState.PLAYING)
        {
            reason = "not playing (cutscene or menu)";
            return false;
        }
        if (TransitionPoint.IsTransitionBlocked)
        {
            reason = "transitions are blocked";
            return false;
        }
#endif

        if (pd.disablePause)
        {
            reason = "cutscene or scripted sequence in progress";
            return false;
        }

        if ((!recoverInputLock && !hero.CanInput())
            || hero.cState.dead
            || hero.cState.hazardDeath
            || hero.cState.hazardRespawning
            || hero.cState.transitioning)
        {
            reason = "player does not have control";
            return false;
        }

#if HOLLOW_KNIGHT
        if (hero.IsDreamReturning)
        {
            reason = "returning from a dream";
            return false;
        }

        if (gm.sm != null)
        {
            MapZone zone = gm.sm.mapZone;
            if (zone == MapZone.DREAM_WORLD || zone == MapZone.GODS_GLORY || zone == MapZone.FINAL_BOSS)
            {
                reason = $"in memory or boss zone ({zone})";
                return false;
            }
        }

        if (BossSceneController.IsBossScene)
        {
            reason = "in a boss scene";
            return false;
        }

        if (FindClosedBattleGate(gm.sceneName) is PlayMakerFSM gate)
        {
            reason = $"battle gate closed in {gm.sceneName} ({gate.gameObject.name}, {gate.ActiveStateName})";
            return false;
        }
#elif SILKSONG
        if (gm.IsMemoryScene())
        {
            reason = "inside a memory sequence";
            return false;
        }

        if (pd.blackThreadWorld && (!pd.act3_wokeUp || !pd.act3_enclaveWakeSceneCompleted))
        {
            reason = "Act 3 wake-up sequence in progress";
            return false;
        }

        if (hero.cState.isInCutsceneMovement
            || InteractManager.BlockingInteractable != null
            || GenericMessageCanvas.IsActive)
        {
            reason = "cutscene or dialogue in progress";
            return false;
        }

        if (hero.transform.parent != null && hero.GetComponentInParent<HeroPlatformStick>() != null)
        {
            reason = "on a moving platform or lift";
            return false;
        }

        if (IsInActiveSilksongBattle())
        {
            reason = "in an active battle or boss fight";
            return false;
        }
#endif

        reason = string.Empty;
        return true;
    }

#if HOLLOW_KNIGHT
    private static PlayMakerFSM? FindClosedBattleGate(string sceneName)
    {
        PlayMakerFSM[] fsms = UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None);
        foreach (PlayMakerFSM fsm in fsms)
        {
            if (fsm == null || !fsm.isActiveAndEnabled || fsm.gameObject.scene.name != sceneName
                || fsm.FsmName != "BG Control")
                continue;

            // Battle Control can wait indefinitely in Detect before combat starts.
            // Check the gate's own close sequence and the state that waits for it to reopen.
            if (fsm.ActiveStateName is "Close 1" or "Close 2" or "Quick Close" or "Double Close")
                return fsm;

            var transitions = fsm.Fsm.ActiveState?.Transitions;
            if (transitions == null) continue;
            foreach (var transition in transitions)
            {
                if (transition.EventName == "BG OPEN") return fsm;
            }
        }
        return null;
    }
#elif SILKSONG
    private static bool IsInActiveSilksongBattle()
    {
        BattleScene[] battles = UnityEngine.Object.FindObjectsByType<BattleScene>(FindObjectsSortMode.None);
        for (int i = 0; i < battles.Length; i++)
        {
            BattleScene battle = battles[i];
            if (battle == null || !battle.isActiveAndEnabled) continue;
            var traverse = Traverse.Create(battle);
            bool started = traverse.Field<bool>("started").Value;
            bool completed = traverse.Field<bool>("completed").Value;
            if (started && !completed) return true;
        }
        return false;
    }
#endif
}

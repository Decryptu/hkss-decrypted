using System;
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
    internal static bool CanLeave(out string reason)
    {
        if (!AutoLoad.IsReady)
        {
            reason = "still loading";
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

        if (!hero.CanInput()
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

        if (IsInActiveHollowKnightBattle())
        {
            reason = "in an active battle or boss fight";
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
    private static bool IsInActiveHollowKnightBattle()
    {
        PlayMakerFSM[] fsms = UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None);
        for (int i = 0; i < fsms.Length; i++)
        {
            PlayMakerFSM fsm = fsms[i];
            if (fsm == null || !fsm.isActiveAndEnabled) continue;
            if (!string.Equals(fsm.FsmName, "Battle Control", StringComparison.Ordinal)) continue;

            string state = fsm.ActiveStateName ?? string.Empty;
            if (state.Length == 0) continue;
            if (IsInactiveBattleState(state)) continue;
            return true;
        }
        return false;
    }

    private static bool IsInactiveBattleState(string state)
    {
        return state.Equals("Init", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Check", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Dormant", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Wait", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Activated", StringComparison.OrdinalIgnoreCase)
            || state.Equals("End", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Done", StringComparison.OrdinalIgnoreCase);
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

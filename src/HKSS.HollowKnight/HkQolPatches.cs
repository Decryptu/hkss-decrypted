using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
using ItemChanger.Internal;
using ItemChanger.UIDefs;
using TMProOld;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Quality-of-life patches for Hollow Knight matching Silksong parity:
/// fast dialogue reveal, fast Stag arrival and travel skip, fast non-blocking item pickups,
/// map check pins for unobtained placements, and automatic Wayward Compass.
/// Installed early so they apply to both new and existing saves without changing save files.
/// </summary>
internal static class HkQolPatches
{
    private static bool _installed;
    private static ManualLogSource? _log;

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        if (_installed) return;
        _installed = true;
        _log = log;

        TryPatch(harmony, typeof(FastDialoguePatch), "FastDialoguePatch");
        TryPatch(harmony, typeof(FastStagTravelPatch), "FastStagTravelPatch");
        TryPatch(harmony, typeof(FastBigPopupPatch), "FastBigPopupPatch");
        TryPatch(harmony, typeof(FastLorePopupPatch), "FastLorePopupPatch");

        HkCheckMap.Install(harmony, log);

        Events.AddFsmEdit(new FsmID("Shiny Control"), EditShinyControl);
        Events.AddFsmEdit(new FsmID("Stag", "Stag Control"), EditStagControl);
    }

    private static void TryPatch(Harmony harmony, Type patchType, string name)
    {
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[HK QoL] Failed to install {name}: {ex.Message}");
        }
    }

    private static void EditShinyControl(PlayMakerFSM fsm)
    {
        StripPickupDelays(fsm.GetState("Hero Down"));
        StripPickupDelays(fsm.GetState("Trink Flash"));
        StripPickupDelays(fsm.GetState("Hero Up"));
    }

    private static void StripPickupDelays(FsmState? state)
    {
        if (state?.Actions == null) return;
        state.Actions = state.Actions
            .Where(a => a is not Wait && a is not Tk2dPlayAnimationWithEvents)
            .ToArray();
    }

    private static void EditStagControl(PlayMakerFSM fsm)
    {
        // Make the Stag immediately present at already-opened stations so the player doesn't have to wait on the bell.
        FsmState? locCheck = fsm.GetState("Current Location Check");
        if (locCheck != null)
        {
            locCheck.AddFirstAction(new Lambda(() =>
            {
                PlayerData? pd = PlayerData.instance;
                if (pd == null) return;

                FsmString? openedVar = fsm.FsmVariables.FindFsmString("Station Opened Bool");
                FsmInt? posVar = fsm.FsmVariables.FindFsmInt("Station Position Number");
                if (openedVar == null || posVar == null) return;

                string openedBool = openedVar.Value;
                int pos = posVar.Value;
                if (!string.IsNullOrEmpty(openedBool) && pos > 0 && pd.GetBool(openedBool))
                {
                    pd.SetInt(nameof(PlayerData.stagPosition), pos);
                }
            }));
        }

        // Shorten any remaining arrival/call waits when ringing the bell manually.
        if (fsm.FsmStates == null) return;
        foreach (FsmState state in fsm.FsmStates)
        {
            if (state?.Actions == null) continue;
            string name = state.Name ?? string.Empty;
            if (name.IndexOf("Arrive", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Call", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Wait", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            foreach (FsmStateAction action in state.Actions)
            {
                if (action is Wait wait && wait.time != null && wait.time.Value > 0.05f)
                {
                    wait.time.Value = 0.05f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(DialogueBox), "ShowNextChar")]
    private static class FastDialoguePatch
    {
        private static bool Prefix(DialogueBox __instance, TextMeshPro? ___textMesh)
        {
            if (___textMesh == null) return true;
            TMP_TextInfo? info = ___textMesh.textInfo;
            if (info != null && info.pageInfo != null && __instance.currentPage > 0 && __instance.currentPage <= info.pageCount)
            {
                ___textMesh.maxVisibleCharacters = info.pageInfo[__instance.currentPage - 1].lastCharacterIndex + 1;
            }
            else
            {
                ___textMesh.maxVisibleCharacters = int.MaxValue;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(StagTravel))]
    private static class FastStagTravelPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        private static void StartPrefix(ref float ___minimumLoadDuration, ref float ___minimumSkipDuration)
        {
            ___minimumLoadDuration = 0f;
            ___minimumSkipDuration = 0f;
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static void UpdatePrefix(
            bool ___isAsync,
            bool ___isFetchComplete,
            ref bool ___isSkipping,
            ref bool ___isSkipFadeComplete,
            ref bool ___isReadyToActivate)
        {
            if (___isAsync)
            {
                if (___isFetchComplete && !___isSkipping)
                {
                    ___isSkipping = true;
                    ___isSkipFadeComplete = true;
                    ___isReadyToActivate = true;
                }
            }
            else
            {
                ___isSkipFadeComplete = true;
            }
        }
    }

    [HarmonyPatch(typeof(BigUIDef), nameof(BigUIDef.SendMessage))]
    private static class FastBigPopupPatch
    {
        private static void Prefix(ref MessageType type)
        {
            if ((type & MessageType.Big) == MessageType.Big)
            {
                type = (type & ~MessageType.Big) | MessageType.Corner;
            }
        }
    }

    [HarmonyPatch(typeof(LoreUIDef), nameof(LoreUIDef.SendMessage))]
    private static class FastLorePopupPatch
    {
        private static void Prefix(ref MessageType type)
        {
            if ((type & MessageType.Lore) == MessageType.Lore)
            {
                type = (type & ~MessageType.Lore) | MessageType.Corner;
            }
        }
    }
}

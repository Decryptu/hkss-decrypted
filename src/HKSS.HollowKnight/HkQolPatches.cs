using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using GlobalEnums;
using HarmonyLib;
using HKSS.Unity;
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
/// map check pins, automatic Wayward Compass, skippable cutscenes &amp; boss intros,
/// bulk relic/egg selling (Lemm &amp; Jinn), and configurable enemy Geo multiplier.
/// </summary>
internal static class HkQolPatches
{
    internal static readonly float[] GeoMultiplierSteps = { 1f, 1.5f, 2f, 3f, 5f, 10f };

    private static readonly HashSet<string> ProtectedCutsceneScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Opening_Sequence",
        "End_Credits",
        "End_Credits_Scroll",
        "Menu_Credits",
        "End_Game_Completion",
        "PermaDeath",
        "PermaDeath_Unlock",
        "Cinematic_MrMushroom",
        "GG_End_Sequence",
    };

    private static bool _installed;
    private static ManualLogSource? _log;
    private static ConfigEntry<bool>? _skipCutscenesCfg;
    private static ConfigEntry<bool>? _skipBossIntrosCfg;
    private static ConfigEntry<bool>? _bulkSellCfg;
    private static ConfigEntry<float>? _enemyGeoMultiplierCfg;

    internal static bool SkipCutscenes
    {
        get => _skipCutscenesCfg?.Value ?? true;
        set { if (_skipCutscenesCfg != null) _skipCutscenesCfg.Value = value; }
    }

    internal static bool SkipBossIntros
    {
        get => _skipBossIntrosCfg?.Value ?? false;
        set { if (_skipBossIntrosCfg != null) _skipBossIntrosCfg.Value = value; }
    }

    internal static bool BulkSellRelicsAndEggs
    {
        get => _bulkSellCfg?.Value ?? true;
        set { if (_bulkSellCfg != null) _bulkSellCfg.Value = value; }
    }

    internal static float EnemyGeoMultiplier
    {
        get => _enemyGeoMultiplierCfg?.Value ?? 1f;
        set
        {
            if (_enemyGeoMultiplierCfg != null)
                _enemyGeoMultiplierCfg.Value = Mathf.Clamp(value, 1f, 10f);
        }
    }

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        if (_installed) return;
        _installed = true;
        _log = log;

        BindConfig(HkssPlugin.PluginConfig);

        TryPatch(harmony, typeof(FastDialoguePatch), "FastDialoguePatch");
        TryPatch(harmony, typeof(FastStagTravelPatch), "FastStagTravelPatch");
        TryPatch(harmony, typeof(FastBigPopupPatch), "FastBigPopupPatch");
        TryPatch(harmony, typeof(FastLorePopupPatch), "FastLorePopupPatch");
        TryPatch(harmony, typeof(FastCutsceneSkipPatch), "FastCutsceneSkipPatch");
        TryPatch(harmony, typeof(EnemyGeoMultiplierPatch), "EnemyGeoMultiplierPatch");

        HkCheckMap.Install(harmony, log);

        Events.AddFsmEdit(new FsmID("Shiny Control"), EditShinyControl);
        Events.AddFsmEdit(new FsmID("Stag", "Stag Control"), EditStagControl);
        Events.AddFsmEdit(new FsmID("Roar Lock"), EditRoarLock);
        Events.AddFsmEdit(SceneNames.Ruins1_05b, new FsmID("Relic Dealer", "Conversation Control"), EditLemmBulkSell);
        Events.AddFsmEdit(SceneNames.Room_Jinn, new FsmID("Jinn NPC", "Conversation Control"), EditJinnBulkSell);
    }

    internal static void BindConfig(ConfigFile? config)
    {
        if (config == null) return;
        _skipCutscenesCfg ??= config.Bind("QoL", "SkipCutscenes", true, "Allow instant skipping of non-ending cutscenes in Hollow Knight.");
        _skipBossIntrosCfg ??= config.Bind("QoL", "SkipBossIntros", false, "Skip boss roar locks so the Knight keeps control during boss intros.");
        _bulkSellCfg ??= config.Bind("QoL", "BulkSellRelicsAndEggs", true, "Sell all copies of a relic to Lemm or all Rancid Eggs to Steel Soul Jinn at once.");
        _enemyGeoMultiplierCfg ??= config.Bind("QoL", "EnemyGeoMultiplier", 1f, "Multiplier for Geo dropped by defeated enemies (1.0 = vanilla).");
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

    private static void EditRoarLock(PlayMakerFSM fsm)
    {
        FsmState? roarEnter = fsm.GetState("Roar Lock") ?? fsm.GetState("Check");
        if (roarEnter == null) return;

        roarEnter.AddFirstAction(new Lambda(() =>
        {
            if (SkipBossIntros)
            {
                fsm.SendEvent("ROAR EXIT");
            }
        }));
    }

    private static void EditLemmBulkSell(PlayMakerFSM fsm)
    {
        // When Lemm sells a relic (Trinket 1..4), bulk-sell any additional copies of that relic
        // immediately before vanilla decrements the last one, preserving vanilla dialogue & audio.
        (string StateName, string TrinketField, string SoldField, int Price)[] sales =
        {
            ("Sell 1", nameof(PlayerData.trinket1), nameof(PlayerData.soldTrinket1), 200),
            ("Sell 2", nameof(PlayerData.trinket2), nameof(PlayerData.soldTrinket2), 450),
            ("Sell 3", nameof(PlayerData.trinket3), nameof(PlayerData.soldTrinket3), 800),
            ("Sell 4", nameof(PlayerData.trinket4), nameof(PlayerData.soldTrinket4), 1200),
        };

        foreach (var sale in sales)
        {
            FsmState? state = fsm.GetState(sale.StateName);
            if (state == null) continue;

            string trinketField = sale.TrinketField;
            string soldField = sale.SoldField;
            int price = sale.Price;

            state.AddFirstAction(new Lambda(() =>
            {
                if (!BulkSellRelicsAndEggs) return;
                PlayerData? pd = PlayerData.instance;
                HeroController? hc = HeroController.instance;
                if (pd == null || hc == null) return;

                int held = pd.GetInt(trinketField);
                if (held <= 1) return;

                int extra = held - 1;
                pd.SetInt(trinketField, 1);
                pd.SetInt(soldField, pd.GetInt(soldField) + extra);
                int bonusGeo = extra * price;
                hc.AddGeo(bonusGeo);
                _log?.LogInfo($"[HK QoL] Bulk-sold {extra + 1} relics to Lemm (+{bonusGeo} extra Geo).");
            }));
        }
    }

    private static void EditJinnBulkSell(PlayMakerFSM fsm)
    {
        // Steel Soul Jinn gives 290..450 Geo per Rancid Egg. Skip if Egg Shop randomizer overrides Jinn.
        FsmState? giveGeo = fsm.GetState("Give Geo") ?? fsm.GetState("Take Egg");
        if (giveGeo == null) return;

        giveGeo.AddFirstAction(new Lambda(() =>
        {
            if (!BulkSellRelicsAndEggs) return;
            if ((HkClientHost.CurrentSlotData?.Options?.EggShopSlots ?? 0) > 0) return;

            PlayerData? pd = PlayerData.instance;
            HeroController? hc = HeroController.instance;
            if (pd == null || hc == null) return;

            int eggs = pd.GetInt(nameof(PlayerData.rancidEggs));
            if (eggs <= 1) return;

            int extra = eggs - 1;
            pd.SetInt(nameof(PlayerData.rancidEggs), 1);
            int totalBonus = 0;
            for (int i = 0; i < extra; i++)
            {
                totalBonus += UnityEngine.Random.Range(290, 451);
            }
            pd.SetInt(nameof(PlayerData.jinnEggsSold), pd.GetInt(nameof(PlayerData.jinnEggsSold)) + extra);
            hc.AddGeo(totalBonus);
            _log?.LogInfo($"[HK QoL] Bulk-sold {extra + 1} Rancid Eggs to Jinn (+{totalBonus} extra Geo).");
        }));
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

    private static bool IsProtectedCutsceneScene(string? sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return ProtectedCutsceneScenes.Contains(sceneName!)
            || sceneName!.StartsWith("Cinematic_Ending_", StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(InputHandler), nameof(InputHandler.SetSkipMode))]
    private static class FastCutsceneSkipPatch
    {
        private static void Prefix(ref SkipPromptMode newMode)
        {
            if (!SkipCutscenes) return;
            if (newMode != SkipPromptMode.SKIP_PROMPT) return;

            string? scene = GameManager._instance?.sceneName;
            if (IsProtectedCutsceneScene(scene)) return;

            newMode = SkipPromptMode.SKIP_INSTANT;
        }
    }

    [HarmonyPatch(typeof(HealthManager), nameof(HealthManager.Die))]
    private static class EnemyGeoMultiplierPatch
    {
        private static void Prefix(
            ref int ___smallGeoDrops,
            ref int ___mediumGeoDrops,
            ref int ___largeGeoDrops)
        {
            float mult = EnemyGeoMultiplier;
            if (mult <= 1.001f) return;

            if (___smallGeoDrops > 0)
                ___smallGeoDrops = Mathf.Max(1, Mathf.RoundToInt(___smallGeoDrops * mult));
            if (___mediumGeoDrops > 0)
                ___mediumGeoDrops = Mathf.Max(1, Mathf.RoundToInt(___mediumGeoDrops * mult));
            if (___largeGeoDrops > 0)
                ___largeGeoDrops = Mathf.Max(1, Mathf.RoundToInt(___largeGeoDrops * mult));
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

using System;
using System.Collections.Generic;
using HarmonyLib;
using HKSS.Core;
using UnityEngine.SceneManagement;

namespace HKSS.Unity;

/// <summary>
/// Phase 3 thin slice: hooks real early pickups in each game to report Archipelago checks
/// and applies received Archipelago items locally to the active game.
/// Replaced by the full ItemChanger / Silksong randomizer ports in Phases 4 and 5.
/// </summary>
internal static class ThinSlice
{
    private static bool _applyingGrant;

    internal static void Install(Harmony harmony)
    {
#if HOLLOW_KNIGHT
        var setBool = AccessTools.Method(typeof(PlayerData), nameof(PlayerData.SetBool), new[] { typeof(string), typeof(bool) });
        if (setBool != null)
            harmony.Patch(setBool, postfix: new HarmonyMethod(typeof(ThinSlice), nameof(OnHkSetBool)));

        var setInt = AccessTools.Method(typeof(PlayerData), nameof(PlayerData.SetInt), new[] { typeof(string), typeof(int) });
        if (setInt != null)
            harmony.Patch(setInt, postfix: new HarmonyMethod(typeof(ThinSlice), nameof(OnHkSetInt)));

        var incInt = AccessTools.Method(typeof(PlayerData), nameof(PlayerData.IncrementInt), new[] { typeof(string) });
        if (incInt != null)
            harmony.Patch(incInt, postfix: new HarmonyMethod(typeof(ThinSlice), nameof(OnHkIncrementInt)));
#elif SILKSONG
        var pickup = AccessTools.Method(typeof(CollectableItemPickup), "DoPickupAction");
        if (pickup != null)
            harmony.Patch(pickup, prefix: new HarmonyMethod(typeof(ThinSlice), nameof(OnSilksongPickup)));
#endif
    }

    internal static void GrantItem(RoutedItem item)
    {
        HeroController? hero = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (hero == null || pd == null)
            throw new InvalidOperationException("Cannot grant item while hero or PlayerData is unavailable.");

        string name = item.DisplayName;
        _applyingGrant = true;
        try
        {
#if HOLLOW_KNIGHT
            GrantHollowKnightItem(hero, pd, name);
#elif SILKSONG
            GrantSilksongItem(hero, pd, name);
#endif
        }
        finally
        {
            _applyingGrant = false;
        }
    }

#if HOLLOW_KNIGHT
    private static readonly Dictionary<string, string> CrossroadsGrubByScene = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Crossroads_03"] = "Grub-Crossroads_Acid",
        ["Crossroads_05"] = "Grub-Crossroads_Guard",
        ["Crossroads_31"] = "Grub-Crossroads_Wall",
        ["Crossroads_35"] = "Grub-Crossroads_Vengefly",
        ["Crossroads_48"] = "Grub-Crossroads_Spike",
    };

    private static void OnHkSetBool(string boolName, bool value)
    {
        if (_applyingGrant || !value) return;
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        switch (boolName)
        {
            case "gotCharm_6":
                ap.ReportCheckByName("HK Fury_of_the_Fallen");
                break;
            case "gotCharm_20":
                ap.ReportCheckByName("HK Soul_Catcher");
                break;
            case "hasDash":
                ap.ReportCheckByName("HK Mothwing_Cloak");
                break;
        }
    }

    private static void OnHkSetInt(string intName, int value)
    {
        if (_applyingGrant) return;
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        if (intName == "fireballLevel" && value == 1)
            ap.ReportCheckByName("HK Vengeful_Spirit");
    }

    private static void OnHkIncrementInt(string intName)
    {
        if (_applyingGrant || intName != "grubsCollected") return;
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (CrossroadsGrubByScene.TryGetValue(scene, out string locationName))
            ap.ReportCheckByName(IdSpace.HollowKnightPrefix + locationName);
    }

    private static void GrantHollowKnightItem(HeroController hero, PlayerData pd, string name)
    {
        switch (name)
        {
            case "Mothwing_Cloak":
            case "Left_Mothwing_Cloak":
            case "Right_Mothwing_Cloak":
                pd.hasDash = true;
                pd.canDash = true;
                return;

            case "Shade_Cloak":
                pd.hasDash = true;
                pd.canDash = true;
                pd.hasShadowDash = true;
                pd.canShadowDash = true;
                return;

            case "Mantis_Claw":
            case "Left_Mantis_Claw":
            case "Right_Mantis_Claw":
                pd.hasWalljump = true;
                pd.canWallJump = true;
                return;

            case "Crystal_Heart":
            case "Left_Crystal_Heart":
            case "Right_Crystal_Heart":
                pd.hasSuperDash = true;
                pd.canSuperDash = true;
                return;

            case "Monarch_Wings":
                pd.hasDoubleJump = true;
                return;

            case "Isma's_Tear":
                pd.hasAcidArmour = true;
                return;

            case "Vengeful_Spirit":
                if (pd.fireballLevel < 1) pd.fireballLevel = 1;
                return;

            case "Shade_Soul":
                if (pd.fireballLevel < 2) pd.fireballLevel = 2;
                return;

            case "One_Geo":
                hero.AddGeo(1);
                return;

            case "Grub":
                pd.grubsCollected++;
                hero.AddGeo(25);
                return;
        }

        if (name.IndexOf("Geo", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            hero.AddGeo(100);
            return;
        }

        // Fallback during the thin slice so any received HK item has a visible, saved effect.
        hero.AddGeo(25);
    }
#elif SILKSONG
    private static readonly Dictionary<string, string> MossberryByScene = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tut_01b"] = "Mossberry: Moss Grotto #1",
        ["Tut_02"] = "Mossberry: Moss Grotto #2",
        ["Bonetown"] = "Mossberry: Bone Bottom",
        ["Bone_05b"] = "Mossberry: Mosshome",
    };

    private static void OnSilksongPickup(CollectableItemPickup __instance)
    {
        if (_applyingGrant || __instance == null) return;
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        string? itemName = __instance.Item != null ? __instance.Item.name : null;
        if (!string.Equals(itemName, "Mossberry", StringComparison.OrdinalIgnoreCase))
            return;

        string scene = __instance.gameObject.scene.name;
        if (MossberryByScene.TryGetValue(scene, out string locationName))
            ap.ReportCheckByName(IdSpace.SilksongPrefix + locationName);
    }

    private static void GrantSilksongItem(HeroController hero, PlayerData pd, string name)
    {
        switch (name)
        {
            case "Swift Step":
            case "Progressive Swift Step":
                pd.hasDash = true;
                return;

            case "Drifter's Cloak":
                pd.hasBrolly = true;
                return;

            case "Cling Grip":
                pd.hasWalljump = true;
                return;

            case "Faydown Cloak":
                pd.hasDoubleJump = true;
                return;

            case "Silk Spear":
                pd.hasNeedleThrow = true;
                return;

            case "Frayed Rosary String":
                hero.AddGeo(30);
                return;

            case "Rosary String":
                hero.AddGeo(60);
                return;

            case "Rosary Necklace":
                hero.AddGeo(120);
                return;

            case "Heavy Rosary Necklace":
                hero.AddGeo(220);
                return;

            case "Pale Rosary Necklace":
                hero.AddGeo(340);
                return;

            case "Shard Bundle":
            case "Beast Shard":
                hero.AddShards(80);
                return;
        }

        if (name.IndexOf("Rosar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            hero.AddGeo(50);
            return;
        }

        if (name.IndexOf("Shard", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            hero.AddShards(50);
            return;
        }

        // Fallback during the thin slice so any received Silksong item has a visible, saved effect.
        hero.AddGeo(25);
    }
#endif
}

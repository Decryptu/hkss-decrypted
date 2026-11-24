using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Moves everything the game saves into the run's own folder and keeps run progress out of the
/// player's Steam achievements. If any part cannot be installed the game is closed.
/// </summary>
internal static class SaveIsolation
{
    private static readonly MethodInfo PersistentDataPath =
        AccessTools.PropertyGetter(typeof(Application), nameof(Application.persistentDataPath));

    internal static void Install(Harmony harmony)
    {
        if (Platform.Current != null)
            throw new InvalidOperationException("The game's platform started before the plugin.");

        Directory.CreateDirectory(HkssPlugin.Run.SaveDir);
        Type platform = typeof(DesktopPlatform);

        harmony.Patch(Require(AccessTools.Method(platform, "Awake")),
            postfix: Own(nameof(VerifySaveFolder)), transpiler: Own(nameof(UseRunFolder)));
        harmony.Patch(Require(AccessTools.PropertyGetter(typeof(Platform), nameof(Platform.UserDataDirectory))),
            prefix: Own(nameof(RunFolder)));
        harmony.Patch(Require(AccessTools.Method(typeof(Platform), "GetSaveSlotFileName")),
            postfix: Own(nameof(PrefixFileName)));
        harmony.Patch(Require(AccessTools.Method(typeof(ConfigManager), nameof(ConfigManager.SaveConfig))),
            prefix: Own(nameof(Skip)));

        foreach (string name in new[] { "WriteSaveSlot", "ClearSaveSlot" })
            harmony.Patch(Require(AccessTools.Method(platform, name)), prefix: Own(nameof(VerifySaveFolder)));

        harmony.Patch(Require(AccessTools.Method(platform, "PushAchievementUnlock")), prefix: Own(nameof(Skip)));
        harmony.Patch(Require(AccessTools.Method(platform, "ResetAchievements")), prefix: Own(nameof(Skip)));
#if HOLLOW_KNIGHT
        // Hollow Knight also copies unlocks into the registry shared with normal play.
        harmony.Patch(Require(AccessTools.Method(platform, nameof(DesktopPlatform.CreateUpgradedSharedData))),
            prefix: Own(nameof(KeepOnlyRunSharedData)));
#else
        harmony.Patch(Require(AccessTools.Method(platform, "UpdateAchievementProgress")), prefix: Own(nameof(Skip)));
#endif
    }

    private static HarmonyMethod Own(string name) => new(AccessTools.Method(typeof(SaveIsolation), name));

    private static MethodBase Require(MethodBase? method) =>
        method ?? throw new MissingMethodException("A save method changed in this game version.");

    private static string SaveDir() => HkssPlugin.Run.SaveDir;

    private static IEnumerable<CodeInstruction> UseRunFolder(IEnumerable<CodeInstruction> code)
    {
        List<CodeInstruction> result = code.ToList();
        int replaced = 0;
        foreach (CodeInstruction instruction in result)
        {
            if (!instruction.Calls(PersistentDataPath)) continue;
            instruction.operand = AccessTools.Method(typeof(SaveIsolation), nameof(SaveDir));
            replaced++;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Expected one save folder in DesktopPlatform.Awake, found {replaced}.");
        return result;
    }

    private static void VerifySaveFolder(string ___saveDirPath, DesktopOnlineSubsystem? ___onlineSubsystem)
    {
        if (!HkssPlugin.Run.OwnsSavePath(___saveDirPath))
            HkssPlugin.Abort($"Saves were about to go to {___saveDirPath}, outside this run.");
        if (___onlineSubsystem is { HandlesGameSaves: true })
            HkssPlugin.Abort("This edition of the game stores saves through its own service.");
    }

    private static bool RunFolder(ref string __result)
    {
        __result = SaveDir();
        return false;
    }

    private static void PrefixFileName(ref string __result) => __result = "hkss_" + __result;

    private static bool Skip() => false;

#if HOLLOW_KNIGHT
    private static bool KeepOnlyRunSharedData(Platform.ISharedData newSharedData, ref Platform.ISharedData __result)
    {
        __result = newSharedData;
        return false;
    }
#endif
}

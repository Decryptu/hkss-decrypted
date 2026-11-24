using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>Removes the waits between process start and gameplay, which nobody sees.</summary>
internal static class FastBoot
{
    private static readonly ConstructorInfo WaitForSeconds =
        AccessTools.Constructor(typeof(WaitForSeconds), new[] { typeof(float) });

    /// <summary>The title menu has finished appearing, so leaving it is safe.</summary>
    internal static bool MenuShown { get; private set; }

    internal static void Install(Harmony harmony)
    {
        // Leaving earlier makes this coroutine resume on menu objects the game has destroyed.
        harmony.Patch(AccessTools.Method(typeof(UIManager), nameof(UIManager.GoToMainMenu)),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(FastBoot), nameof(TrackMenu))));

        harmony.Patch(AccessTools.Method(typeof(StartManager), "Start"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(FastBoot), nameof(SpeedUpLogos))));

        foreach (string name in new[] { "RunContinueGame", "RunStartNewGame" })
        {
            MethodInfo routine = AccessTools.Method(typeof(GameManager), name)
                ?? throw new MissingMethodException(nameof(GameManager), name);
            harmony.Patch(AccessTools.EnumeratorMoveNext(routine),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(FastBoot), nameof(ShortenWaits))));
        }
    }

    private static void TrackMenu(UIManager __instance, ref IEnumerator __result) =>
        __result = ShowMenu(__instance, __result);

    private static IEnumerator ShowMenu(UIManager ui, IEnumerator original)
    {
        MenuShown = false;
        AccessTools.Field(typeof(UIManager), "startMenuTime").SetValue(ui, Convert.ChangeType(0,
            AccessTools.Field(typeof(UIManager), "startMenuTime").FieldType));
        while (original.MoveNext()) yield return original.Current;
        MenuShown = true;
    }

    private static void SpeedUpLogos(StartManager __instance) => __instance.startManagerAnimator.speed = 100f;

    private static IEnumerable<CodeInstruction> ShortenWaits(IEnumerable<CodeInstruction> code)
    {
        List<CodeInstruction> result = code.ToList();
        int shortened = 0;
        for (int i = 1; i < result.Count; i++)
        {
            if (result[i].opcode != OpCodes.Newobj || !Equals(result[i].operand, WaitForSeconds)) continue;
            if (result[i - 1].opcode != OpCodes.Ldc_R4) continue;
            result[i - 1].operand = 0.02f;
            shortened++;
        }
        if (shortened == 0) HkssPlugin.Log.LogWarning("Found no menu wait to shorten.");
        return result;
    }
}

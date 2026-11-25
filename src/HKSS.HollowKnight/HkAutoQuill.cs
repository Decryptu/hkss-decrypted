using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using HKSS.Unity;

namespace HKSS.HollowKnight;

[HarmonyPatch]
internal static class HkAutoQuill
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameMap), nameof(GameMap.SetupMap));
        yield return AccessTools.Method(typeof(GameMap), nameof(GameMap.SetupMapMarkers));
        yield return AccessTools.Method(typeof(PlayerData), nameof(PlayerData.UpdateGameMap));
    }

    private static void Prefix(object __instance, MethodBase __originalMethod)
    {
        if (PlayerOptions.AutoQuill.Value && __originalMethod.Name == nameof(GameMap.SetupMapMarkers)
            && __instance is GameMap map && PlayerData.instance is PlayerData data)
        {
            data.UpdateGameMap();
            map.SetupMap();
        }
    }

    private static bool CanUseQuill(PlayerData data) => data.hasQuill || PlayerOptions.AutoQuill.Value;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hasQuill = AccessTools.Field(typeof(PlayerData), nameof(PlayerData.hasQuill));
        var useQuill = AccessTools.Method(typeof(HkAutoQuill), nameof(CanUseQuill));
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(hasQuill))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = useQuill;
            }
            yield return instruction;
        }
    }
}

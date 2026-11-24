// Ported from Decryptu/silksong-decrypted, src/SilksongRandomizer/Patches/BindPatches.cs, commit 34857d0 (MIT)
#nullable disable
using HarmonyLib;

namespace SilksongRandomizer.Patches
{
    internal static class BindPatches
    {
        [HarmonyPatch(typeof(HeroController), nameof(HeroController.CanBind))]
        private static class Healing
        {
            private static void Postfix(ref bool __result) => __result &= BindManager.CanBind(SaveState.Instance);
        }
    }
}

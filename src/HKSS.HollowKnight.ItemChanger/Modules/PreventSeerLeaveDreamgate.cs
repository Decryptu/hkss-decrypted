// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Modules/PreventSeerLeaveDreamgate.cs, commit e57bc4e (LGPL-2.1)
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace ItemChanger.Modules
{
    /// <summary>
    /// Default module which removes the failsafe that gives Dream Gate automatically in saves where the Seer has ascended.
    /// </summary>
    [DefaultModule]
    public class PreventSeerLeaveDreamgate : Module
    {
        public override void Initialize()
        {
            IL.GameManager.CheckAllAchievements += DoNotGiveDreamgate;
        }
        public override void Unload()
        {
            IL.GameManager.CheckAllAchievements -= DoNotGiveDreamgate;
        }

        private void DoNotGiveDreamgate(ILContext il)
        {
            ILCursor cursor = new(il);

            // Stock HK 1.5.12620 emits a direct field store: this.playerData.hasDreamGate = true.
            if (cursor.TryGotoNext(
                i => i.Match(OpCodes.Ldarg_0),
                i => i.MatchLdfld<GameManager>(nameof(GameManager.playerData)),
                i => i.Match(OpCodes.Ldc_I4_1),
                i => i.MatchStfld<PlayerData>(nameof(PlayerData.hasDreamGate))
              ))
            {
                cursor.RemoveRange(4);
                return;
            }

            // Fallback for MAPI-rewritten Assembly-CSharp where stfld was turned into SetBoolSwappedArgs.
            cursor.Index = 0;
            if (cursor.TryGotoNext(
                i => i.Match(OpCodes.Ldarg_0),
                i => i.MatchLdfld<GameManager>(nameof(GameManager.playerData)),
                i => i.Match(OpCodes.Ldc_I4_1),
                i => i.MatchLdstr(nameof(PlayerData.hasDreamGate)),
                i => i.MatchCallvirt<PlayerData>("SetBoolSwappedArgs")
              ))
            {
                cursor.RemoveRange(5);
            }
        }
    }
}

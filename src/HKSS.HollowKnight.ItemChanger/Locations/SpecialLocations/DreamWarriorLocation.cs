// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/DreamWarriorLocation.cs, commit e57bc4e (LGPL-2.1)
using Modding;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Boss Essence location which applies to dream warriors and supports respawning items.
    /// </summary>
    public class DreamWarriorLocation : BossEssenceLocation
    {
        public string playerDataInt;
        protected override void OnLoad()
        {
            base.OnLoad();
            ModHooks.GetPlayerIntHook += OverrideCollectedInt;
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            ModHooks.GetPlayerIntHook -= OverrideCollectedInt;
        }

        private int OverrideCollectedInt(string name, int orig)
        {
            // 0 = initial, 1 = defeated, 2 = collected
            if (name == playerDataInt && orig == 2 && !Placement.AllObtained())
            {
                return 1;
            }
            return orig;
        }
    }
}

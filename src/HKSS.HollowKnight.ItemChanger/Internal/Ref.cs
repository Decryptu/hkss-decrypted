// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Internal/Ref.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Internal
{
    public static class Ref
    {
        public static Settings Settings => ItemChangerMod.SET;
        public static PlayerData PD => PlayerData.instance;
        public static GameManager GM => GameManager.instance;
        public static HeroController HC => HeroController.SilentInstance;



        public static void QuickSave(params AbstractPlacement[] placements) => Settings.SavePlacements(placements);
    }
}

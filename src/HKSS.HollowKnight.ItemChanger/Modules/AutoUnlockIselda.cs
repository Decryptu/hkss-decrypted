// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Modules/AutoUnlockIselda.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Modules
{
    [DefaultModule]
    public class AutoUnlockIselda : Module
    {
        public override void Initialize()
        {
            PlayerData.instance.SetBool(nameof(PlayerData.openedMapperShop), true);
        }

        public override void Unload() { }
    }
}

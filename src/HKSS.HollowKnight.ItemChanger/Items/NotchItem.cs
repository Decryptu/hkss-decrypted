// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Items/NotchItem.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Items
{
    public class NotchItem : IntItem
    {
        public bool refillHealth = true;

        public override void GiveImmediate(GiveInfo info)
        {
            base.GiveImmediate(info);
            if (refillHealth)
            {
                HeroController.instance.CharmUpdate();
                EventRegister.SendEvent("UPDATE BLUE HEALTH");
            }
            GameManager.instance.RefreshOvercharm();
        }
    }
}

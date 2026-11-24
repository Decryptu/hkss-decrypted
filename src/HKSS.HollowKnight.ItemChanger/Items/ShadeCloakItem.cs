// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Items/ShadeCloakItem.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Items
{
    /// <summary>
    /// MultiBoolItem which sends an event for ShadowGateColliderControls to recheck whether the player has Shade Cloak.
    /// </summary>
    public class ShadeCloakItem : MultiBoolItem
    {
        public override void GiveImmediate(GiveInfo info)
        {
            base.GiveImmediate(info);
            EventRegister.SendEvent("GOT SHADOW DASH");
        }
    }
}

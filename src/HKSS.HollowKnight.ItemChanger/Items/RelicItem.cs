// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Items/RelicItem.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Items
{
    /// <summary>
    /// Item which increments the number of relics for the specified trinketNum in [1,2,3,4] and makes the number of relics visible in the menu.
    /// </summary>
    public class RelicItem : AbstractItem
    {
        public int trinketNum;

        public override void GiveImmediate(GiveInfo info)
        {
            PlayerData.instance.SetBool($"foundTrinket{trinketNum}", true);
            PlayerData.instance.IncrementInt($"trinket{trinketNum}");
        }

    }
}

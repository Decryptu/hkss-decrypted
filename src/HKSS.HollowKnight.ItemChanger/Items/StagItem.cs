// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Items/StagItem.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Items
{
    /// <summary>
    /// Item which sets the specified field true and increments the number of unlocked stag stations toward Stag Nest.
    /// </summary>
    public class StagItem : AbstractItem
    {
        public string fieldName;

        public override void GiveImmediate(GiveInfo info)
        {
            PlayerData.instance.SetBool(fieldName, true);
            PlayerData.instance.IncrementInt(nameof(PlayerData.stationsOpened));
        }

        public override bool Redundant()
        {
            return PlayerData.instance.GetBool(fieldName);
        }
    }
}

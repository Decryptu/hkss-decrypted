// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/SetPDBoolOnLoadTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Tag which sets a PlayerData bool when loaded.
    /// </summary>
    public class SetPDBoolOnLoadTag : Tag
    {
        public string boolName;
        public bool value = true;

        public override void Load(object parent)
        {
            base.Load(parent);
            PlayerData.instance.SetBool(boolName, value);
        }
    }
}

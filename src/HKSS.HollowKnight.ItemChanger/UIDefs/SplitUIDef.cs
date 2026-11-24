// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/UIDefs/SplitUIDef.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.UIDefs
{
    /// <summary>
    /// A MsgUIDef with distinct preview and postview names.
    /// </summary>
    public class SplitUIDef : MsgUIDef
    {
        public IString preview;
        public override string GetPreviewName()
        {
            return preview.GetValue();
        }
        public override UIDef Clone()
        {
            return new SplitUIDef
            {
                name = name.Clone(),
                preview = preview.Clone(),
                shopDesc = shopDesc?.Clone(),
                sprite = sprite.Clone()
            };
        }
    }
}

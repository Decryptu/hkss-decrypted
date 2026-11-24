// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/MultiPreviewRecordTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Tag which contains preview information for each item of a multi cost placement.
    /// </summary>
    [PlacementTag]
    public class MultiPreviewRecordTag : Tag
    {
        public string[] previewTexts;
    }
}

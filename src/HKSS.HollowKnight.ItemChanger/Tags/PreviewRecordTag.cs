// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/PreviewRecordTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Placement tag which stores a record of the information shared in the most recent preview of a single cost placement.
    /// </summary>
    [PlacementTag]
    public class PreviewRecordTag : Tag
    {
        public string previewText;
    }
}

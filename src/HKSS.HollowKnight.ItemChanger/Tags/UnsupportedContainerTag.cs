// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/UnsupportedContainerTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Tag for a location or placement to indicate that a container is not supported and should not be chosen.
    /// </summary>
    [LocationTag]
    [PlacementTag]
    public class UnsupportedContainerTag : Tag
    {
        public string containerType;
    }
}

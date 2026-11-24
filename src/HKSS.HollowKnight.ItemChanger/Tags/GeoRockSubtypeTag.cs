// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/GeoRockSubtypeTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// A <see cref="PreferredDefaultContainerTag"/> for <see cref="Container.GeoRock"/>, which allows specifying the <see cref="GeoRockSubtype"/>.
    /// </summary>
    [LocationTag]
    [PlacementTag]
    public class GeoRockSubtypeTag : PreferredDefaultContainerTag
    {
        public GeoRockSubtype Type;
        public GeoRockSubtypeTag()
        {
            base.containerType = Container.GeoRock;
        }
    }
}

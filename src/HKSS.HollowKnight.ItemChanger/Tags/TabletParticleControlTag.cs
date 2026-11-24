// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/TabletParticleControlTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    [LocationTag]
    [PlacementTag]
    public class TabletParticleControlTag : Tag
    {
        public bool forceDisableParticles;
        public float offsetX;
        public float offsetY;
        public float offsetZ;
    }
}

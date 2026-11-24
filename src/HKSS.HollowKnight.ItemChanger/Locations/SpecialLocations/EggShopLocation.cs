// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/EggShopLocation.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Wrapper location to allow converting a PlaceableLocation to an EggShopPlacement (instead of MutablePlacement)
    /// </summary>
    public class EggShopLocation : AbstractLocation
    {
        public PlaceableLocation location;

        public override AbstractPlacement Wrap()
        {
            return new Placements.EggShopPlacement(name)
            {
                Location = location,
                tags = tags,
            };
        }

        protected override void OnLoad()
        {
            throw new NotImplementedException();
        }

        protected override void OnUnload()
        {
            throw new NotImplementedException();
        }
    }
}

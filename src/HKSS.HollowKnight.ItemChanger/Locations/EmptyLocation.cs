// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/EmptyLocation.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Locations
{
    /// <summary>
    /// A location with no effects. Use, for example, with DualPlacement, or in other situations where a dummy location may be needed.
    /// </summary>
    public class EmptyLocation : AutoLocation
    {
        protected override void OnLoad() { }
        protected override void OnUnload() { }
    }
}

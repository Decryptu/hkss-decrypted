// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Placements/ISingleCostPlacement.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Placements
{
    /// <summary>
    /// Interface which indicates that placement expects all items to share a common cost.
    /// </summary>
    public interface ISingleCostPlacement
    {
        Cost? Cost { get; set; }
    }
}

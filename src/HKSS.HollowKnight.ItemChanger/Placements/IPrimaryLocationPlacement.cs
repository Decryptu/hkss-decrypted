// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Placements/IPrimaryLocationPlacement.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Placements
{
    /// <summary>
    /// Interface for accessing the primary location of a placement, if it has one.
    /// </summary>
    public interface IPrimaryLocationPlacement
    {
        AbstractLocation Location { get; }
    }
}

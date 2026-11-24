// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Placements/IContainerPlacement.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Placements
{
    /// <summary>
    /// Interace for placements which can be used by ContainerLocation. In other words, on demand the placement returns an object which is capable of giving its items.
    /// </summary>
    public interface IContainerPlacement
    {
        void GetContainer(AbstractLocation location, out GameObject obj, out string containerType);
    }
}

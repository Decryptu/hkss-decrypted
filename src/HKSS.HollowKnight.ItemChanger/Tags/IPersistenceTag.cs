// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/IPersistenceTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Interface used when ItemChanger checks tags to determine whether an item is persistent or semipersistent, and should be refreshed.
    /// </summary>
    public interface IPersistenceTag
    {
        Persistence Persistence { get; }
    }
}

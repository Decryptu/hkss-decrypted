// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/PersistentItemTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Tag which indicates an item has a fixed persistence.
    /// </summary>
    [ItemTag]
    public class PersistentItemTag : Tag, IPersistenceTag
    {
        public Persistence Persistence { get; set; }
    }
}

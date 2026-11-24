// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/SmallPlatform.cs, commit e57bc4e (LGPL-2.1)
using ItemChanger.Internal;

namespace ItemChanger
{
    /// <summary>
    /// A Deployer which creates a platform at the specified point.
    /// </summary>
    public record SmallPlatform : Deployer
    {
        public override GameObject Instantiate()
        {
            return ObjectCache.SmallPlatform;
        }
    }
}

// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Internal/GlobalSettings.cs, commit e57bc4e (LGPL-2.1)
using Modding;

namespace ItemChanger.Internal
{
    public class GlobalSettings
    {
        public PreloadSettings PreloadSettings = new();
        public ItemSettings ItemSettings = new();
        public LocationSettings LocationSettings = new();
    }
}

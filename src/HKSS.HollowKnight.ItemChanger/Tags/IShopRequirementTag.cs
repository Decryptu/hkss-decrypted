// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Tags/IShopRequirementTag.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Tags
{
    /// <summary>
    /// Interface for tags used by shops to determine special conditions which must be met before an item appears in stock.
    /// </summary>
    public interface IShopRequirementTag
    {
        bool MeetsRequirement { get; }
    }
}

// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Components/ModShopItemStats.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Components
{
    /// <summary>
    /// A component which is specially handled by ShopLocation to be used as a shop item.
    /// </summary>
    public class ModShopItemStats : MonoBehaviour
    {
        public AbstractItem item;
        public AbstractPlacement? placement;
        public CostDisplayer? costDisplayer;

        public bool IsSecretItem()
        {
            return item.HasTag<Tags.DisableItemPreviewTag>()
                || (placement != null && placement.HasTag<Tags.DisableItemPreviewTag>());
        }
        public Sprite? GetSprite() => item.GetPreviewSprite(placement);
        public string GetPreviewName() => item.GetPreviewName(placement);
        public string GetShopDesc()
        {
            if (item.HasTag<Tags.DisableItemPreviewTag>()
                || (placement != null && placement.HasTag<Tags.DisableItemPreviewTag>())) return Language.Language.Get("???", "IC");
            UIDef? def = item.GetResolvedUIDef(placement);
            return def?.GetShopDesc() ?? Language.Language.Get("???", "IC");
        }

        public string? GetShopCostText()
        {
            if (item.HasTag<Tags.DisableCostPreviewTag>()
                || (placement != null && placement.HasTag<Tags.DisableCostPreviewTag>()))
                return Language.Language.Get("SECRET_COST_SHOPDESC", "IC");
            return cost is not null ? costDisplayer?.GetAdditionalCostText(cost) : null;
        }

        public string GetRecordText()
        {
            string text = GetPreviewName();
            if (item.HasTag<Tags.DisableCostPreviewTag>()
                || (placement != null && placement.HasTag<Tags.DisableCostPreviewTag>()))
            {
                text += "  -  " + Language.Language.Get("SECRET_COST_SHOPDESC", "IC");
            }
            else if (cost is not null && !cost.Paid)
            {
                text += $"  -  {cost.GetCostText()}";
            }
            return text;
        }

        internal int GetDisplayedCost()
        {
            Cost? itemCost = cost;
            if (placement == null || placement.HasTag<Tags.DisableCostPreviewTag>() ||
                item.HasTag<Tags.DisableCostPreviewTag>() || itemCost == null || itemCost.Paid)
                return 0;

            return costDisplayer?.GetDisplayAmount(itemCost) ?? 0;
        }

        public Cost? cost
        {
            get => item.GetTag<CostTag>()?.Cost;
            set => item.GetOrAddTag<CostTag>().Cost = cost!;
        }
    }
}

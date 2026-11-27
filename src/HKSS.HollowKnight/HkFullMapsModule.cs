using ItemChanger;
using ItemChanger.Modules;
using UnityEngine.SceneManagement;

namespace HKSS.HollowKnight;

/// <summary>
/// Unlocks the map panel, Quill, all area maps, and reveals every room on the Hollow Knight
/// world map from the start of a run when <c>start_with_full_maps</c> is enabled.
/// </summary>
public sealed class HkFullMapsModule : Module
{
    public override void Initialize()
    {
        Events.OnEnterGame += Apply;
        Events.OnSceneChange += OnSceneChange;
        Apply();
    }

    public override void Unload()
    {
        Events.OnEnterGame -= Apply;
        Events.OnSceneChange -= OnSceneChange;
    }

    private void OnSceneChange(Scene _) => Apply();

    private static void Apply()
    {
        PlayerData? pd = PlayerData.instance;
        if (pd == null) return;

        bool changed = false;
        changed |= Enable(ref pd.hasMap);
        changed |= Enable(ref pd.hasQuill);
        changed |= Enable(ref pd.mapAllRooms);
        changed |= Enable(ref pd.openedMapperShop);
        changed |= Enable(ref pd.mapDirtmouth);
        changed |= Enable(ref pd.mapCrossroads);
        changed |= Enable(ref pd.mapGreenpath);
        changed |= Enable(ref pd.mapFogCanyon);
        changed |= Enable(ref pd.mapRoyalGardens);
        changed |= Enable(ref pd.mapFungalWastes);
        changed |= Enable(ref pd.mapCity);
        changed |= Enable(ref pd.mapWaterways);
        changed |= Enable(ref pd.mapMines);
        changed |= Enable(ref pd.mapDeepnest);
        changed |= Enable(ref pd.mapCliffs);
        changed |= Enable(ref pd.mapOutskirts);
        changed |= Enable(ref pd.mapRestingGrounds);
        changed |= Enable(ref pd.mapAbyss);

        if (!changed) return;

        GameMap? gameMap = GameManager._instance?.gameMap?.GetComponent<GameMap>();
        if (gameMap != null)
        {
            gameMap.SetupMap();
        }
        HkCheckMap.MarkDirty();
    }

    private static bool Enable(ref bool field)
    {
        if (field) return false;
        field = true;
        return true;
    }
}

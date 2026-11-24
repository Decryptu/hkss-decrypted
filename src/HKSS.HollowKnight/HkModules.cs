// Ported from ArchipelagoMW/Archipelago.HollowKnight, Goals.cs & IC/Modules/{GoalModule,DupeHandlingModule,RepositionShadeModule,RepickableFlowerModule}.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Generic;
using System.Linq;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.Internal;
using ItemChanger.Items;
using ItemChanger.Modules;
using ItemChanger.Placements;
using ItemChanger.Tags;
using ItemChanger.UIDefs;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HKSS.HollowKnight;

public sealed class HkGoalItem : AbstractItem
{
    public override void GiveImmediate(GiveInfo info)
    {
        HkGoalModule.DeclareHkGoalComplete();
    }
}

public sealed class HkGoalModule : Module
{
    public GoalsLookup SelectedGoal { get; set; } = GoalsLookup.Any;
    public int GrubsRequired { get; set; } = 46;

    private static readonly List<string> VictoryScenes = new()
    {
        SceneNames.Cinematic_Ending_A,
        SceneNames.Cinematic_Ending_B,
        SceneNames.Cinematic_Ending_C,
        "Cinematic_Ending_D",
        SceneNames.Cinematic_Ending_E,
    };

    public override void Initialize()
    {
        if (SelectedGoal == GoalsLookup.GrubHunt)
        {
            PlaceGrubHuntVictoryItem();
        }
        else
        {
            ItemChangerMod.Modules.GetOrAdd<HkRepickableFlowerModule>();
            Events.OnSceneChange += OnSceneChanged;
        }
    }

    public override void Unload()
    {
        Events.OnSceneChange -= OnSceneChanged;
    }

    public static void DeclareHkGoalComplete()
    {
        HkssPlugin.Log.LogInfo("[HK] Hollow Knight goal achieved! Reporting to combo GoalTracker.");
        HkssPlugin.State.MarkHkGoalCompleted();
        ApManager.Instance?.Client.ReportLocalGoalComplete();
    }

    private void OnSceneChanged(UnityEngine.SceneManagement.Scene scene)
    {
        string active = scene.name;
        if (!active.StartsWith("Cinematic_Ending_", StringComparison.Ordinal)) return;

        int sceneIndex = VictoryScenes.IndexOf(active);
        if (sceneIndex < 0) return;

        int minIndex = SelectedGoal switch
        {
            GoalsLookup.Any or GoalsLookup.HollowKnight => 0,
            GoalsLookup.SealedSiblings => 1,
            GoalsLookup.Radiance => 2,
            GoalsLookup.Godhome => 3,
            GoalsLookup.GodhomeFlower => 4,
            _ => 0,
        };

        if (sceneIndex >= minIndex)
        {
            DeclareHkGoalComplete();
        }
    }

    private void PlaceGrubHuntVictoryItem()
    {
        if (!Ref.Settings.Placements.TryGetValue(LocationNames.Grubfather, out AbstractPlacement? plt) || plt == null)
        {
            AbstractLocation? loc = Finder.GetLocation(LocationNames.Grubfather);
            if (loc == null) return;
            plt = loc.Wrap();
            ItemChangerMod.AddPlacements(plt.Yield());
        }

        if (plt.Items.Any(i => i is HkGoalItem)) return;

        AbstractItem item = new HkGoalItem
        {
            name = "Grubfather's Victory",
            UIDef = new HkUIDef
            {
                name = new BoxedString("Victory"),
                shopDesc = new BoxedString("Rescue enough grubs to complete Hallownest's goal!"),
                sprite = new EmptySprite(),
            },
        };
        item.AddTag(new CostTag { Cost = Cost.NewGrubCost(GrubsRequired) });
        plt.Add(item);
    }
}

public sealed class HkDupeHandlingModule : Module
{
    public override void Initialize()
    {
        AbstractItem.ModifyRedundantItemGlobal += ModifyRedundantItem;
    }

    public override void Unload()
    {
        AbstractItem.ModifyRedundantItemGlobal -= ModifyRedundantItem;
    }

    private static void ModifyRedundantItem(GiveEventArgs args)
    {
        string baseName = args.Orig?.UIDef?.GetPostviewName() ?? args.Orig?.name ?? "Item";
        args.Item = new SpawnLumafliesItem
        {
            name = $"Lumafly_Escape-{args.Orig?.name}",
            UIDef = new MsgUIDef
            {
                name = new BoxedString($"{baseName} (Duplicate)"),
                shopDesc = new BoxedString("You already have this item."),
                sprite = new EmptySprite(),
            },
        };
    }
}

public sealed class HkRepositionShadeModule : Module
{
    private static readonly Dictionary<string, (float x, float y)> Fixes = new()
    {
        { "Abyss_08", (90.0f, 90.0f) },
        { "Room_Colosseum_Spectate", (124.0f, 10.0f) },
        { "Resting_Grounds_09", (7.4f, 10.0f) },
        { "Ruins1_18", (11.5f, 23.0f) },
    };

    public override void Initialize()
    {
        ModHooks.AfterPlayerDeadHook += FixShade;
    }

    public override void Unload()
    {
        ModHooks.AfterPlayerDeadHook -= FixShade;
    }

    private static void FixShade()
    {
        PlayerData pd = PlayerData.instance;
        if (pd != null && Fixes.TryGetValue(pd.shadeScene, out var pos))
        {
            pd.shadePositionX = pos.x;
            pd.shadePositionY = pos.y;
        }
    }
}

public sealed class HkRepickableFlowerModule : Module
{
    public override void Initialize()
    {
        Events.AddSceneChangeEdit(SceneNames.Room_Mansion, SpawnFlower);
    }

    public override void Unload()
    {
        Events.RemoveSceneChangeEdit(SceneNames.Room_Mansion, SpawnFlower);
    }

    private static void SpawnFlower(Scene scene)
    {
        GameObject? flowerSource = HkClientHost.CreateFlowerPickup();
        if (flowerSource == null) return;
        flowerSource.transform.position = new Vector3(25.0f, 6.4f, 2f);
        flowerSource.SetActive(true);
    }
}

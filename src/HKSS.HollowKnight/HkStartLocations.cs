// Ported from ArchipelagoMW/Archipelago.HollowKnight, IC/RM/StartDef.cs & IC/RM/StartLocationSceneEditsModule.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Generic;
using System.IO;
using GlobalEnums;
using HKSS.Unity;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace HKSS.HollowKnight;

internal static class HkStartLocationNames
{
    public const string KingsPass = "King's Pass";
    public const string Hive = "Hive";
    public const string FarGreenpath = "Far Greenpath";
    public const string LowerGreenpath = "Lower Greenpath";
    public const string WestWaterways = "West Waterways";
    public const string AncestralMound = "Ancestral Mound";
    public const string FungalCore = "Fungal Core";
    public const string WestCrossroads = "West Crossroads";
}

public sealed class HkStartDef
{
    private const string ResourceName = "HKSS.HollowKnight.Data.starts.json";
    private static Dictionary<string, HkStartDef>? _lookup;

    public static Dictionary<string, HkStartDef> Lookup
    {
        get
        {
            if (_lookup != null) return _lookup;

            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Converters = { new StringEnumConverter() },
            };

            using Stream? stream = typeof(HkStartDef).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                HkssPlugin.Log.LogError($"[HK] Embedded start definitions resource '{ResourceName}' not found.");
                return _lookup = new Dictionary<string, HkStartDef>(StringComparer.Ordinal);
            }

            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            _lookup = serializer.Deserialize<Dictionary<string, HkStartDef>>(jsonReader)
                ?? new Dictionary<string, HkStartDef>(StringComparer.Ordinal);
            return _lookup;
        }
    }

    public string Name { get; set; } = string.Empty;
    public string SceneName { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public MapZone Zone { get; set; }
    public string Transition { get; set; } = string.Empty;

    public StartDef ToItemChangerStartDef()
    {
        return new StartDef
        {
            SceneName = SceneName,
            X = X,
            Y = Y,
            MapZone = (int)Zone,
            RespawnFacingRight = true,
            SpecialEffects = SpecialStartEffects.Default | SpecialStartEffects.SlowSoulRefill,
        };
    }
}

public sealed class HkStartLocationSceneEditsModule : Module
{
    public string StartLocationName { get; set; } = HkStartLocationNames.KingsPass;
    public bool RandomizeNail { get; set; }

    public override void Initialize()
    {
        ToggleSceneHooks(true);
    }

    public override void Unload()
    {
        ToggleSceneHooks(false);
    }

    private void ToggleSceneHooks(bool enable)
    {
        switch (StartLocationName)
        {
            case HkStartLocationNames.AncestralMound:
                if (RandomizeNail)
                {
                    if (enable)
                        Events.AddSceneChangeEdit(SceneNames.Crossroads_ShamanTemple, DestroyPlanksForAncestralMoundStart);
                    else
                        Events.RemoveSceneChangeEdit(SceneNames.Crossroads_ShamanTemple, DestroyPlanksForAncestralMoundStart);
                }
                break;

            case HkStartLocationNames.FungalCore:
                if (enable)
                    Events.AddSceneChangeEdit(SceneNames.Fungus2_30, CreateBounceShroomsForFungalCoreStart);
                else
                    Events.RemoveSceneChangeEdit(SceneNames.Fungus2_30, CreateBounceShroomsForFungalCoreStart);
                break;

            case HkStartLocationNames.WestCrossroads:
                if (enable)
                    Events.AddSceneChangeEdit(SceneNames.Crossroads_36, MoveShadeMarkerForWestCrossroadsStart);
                else
                    Events.RemoveSceneChangeEdit(SceneNames.Crossroads_36, MoveShadeMarkerForWestCrossroadsStart);
                break;
        }
    }

    private static void DestroyPlanksForAncestralMoundStart(Scene to)
    {
        foreach ((_, GameObject go) in to.Traverse())
        {
            if (go.name.StartsWith("Plank", StringComparison.Ordinal))
            {
                UObject.Destroy(go);
            }
        }
    }

    private static void CreateBounceShroomsForFungalCoreStart(Scene to)
    {
        GameObject? bounceShroom = to.FindGameObjectByName("Bounce Shroom C");
        if (bounceShroom == null) return;

        GameObject s0 = UObject.Instantiate(bounceShroom);
        s0.transform.SetPosition3D(12.5f, 26f, 0f);
        s0.SetActive(true);

        GameObject s1 = UObject.Instantiate(bounceShroom);
        s1.transform.SetPosition3D(12.5f, 54f, 0f);
        s1.SetActive(true);

        GameObject s2 = UObject.Instantiate(bounceShroom);
        s2.transform.SetPosition3D(21.7f, 133f, 0f);
        s2.SetActive(true);
    }

    private static void MoveShadeMarkerForWestCrossroadsStart(Scene to)
    {
        GameObject? marker = to.FindGameObject("_Props/Hollow_Shade Marker 1");
        if (marker != null)
        {
            marker.transform.position = new Vector3(46.2f, 28f, marker.transform.position.z);
        }
    }
}

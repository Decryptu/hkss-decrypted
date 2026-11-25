using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
using ItemChanger.Internal;
using ItemChanger.UIDefs;
using TMProOld;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Quality-of-life patches for Hollow Knight matching Silksong parity:
/// fast dialogue reveal, fast Stag arrival and travel skip, fast non-blocking item pickups,
/// map check pins for unobtained placements, and automatic Wayward Compass.
/// Installed early so they apply to both new and existing saves without changing save files.
/// </summary>
internal static class HkQolPatches
{
    private static bool _installed;
    private static ManualLogSource? _log;

    internal static void Install(Harmony harmony, ManualLogSource log)
    {
        if (_installed) return;
        _installed = true;
        _log = log;

        TryPatch(harmony, typeof(FastDialoguePatch), "FastDialoguePatch");
        TryPatch(harmony, typeof(FastStagTravelPatch), "FastStagTravelPatch");
        TryPatch(harmony, typeof(FastBigPopupPatch), "FastBigPopupPatch");
        TryPatch(harmony, typeof(FastLorePopupPatch), "FastLorePopupPatch");
        TryPatch(harmony, typeof(MapMarkersAndCompassPatch), "MapMarkersAndCompassPatch");

        Events.AddFsmEdit(new FsmID("Shiny Control"), EditShinyControl);
        Events.AddFsmEdit(new FsmID("Stag", "Stag Control"), EditStagControl);
        AbstractItem.AfterGiveGlobal += _ => MapMarkersAndCompassPatch.MarkDirty();
    }

    private static void TryPatch(Harmony harmony, Type patchType, string name)
    {
        try
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
        catch (Exception ex)
        {
            _log?.LogWarning($"[HK QoL] Failed to install {name}: {ex.Message}");
        }
    }

    private static void EditShinyControl(PlayMakerFSM fsm)
    {
        StripPickupDelays(fsm.GetState("Hero Down"));
        StripPickupDelays(fsm.GetState("Trink Flash"));
        StripPickupDelays(fsm.GetState("Hero Up"));
    }

    private static void StripPickupDelays(FsmState? state)
    {
        if (state?.Actions == null) return;
        state.Actions = state.Actions
            .Where(a => a is not Wait && a is not Tk2dPlayAnimationWithEvents)
            .ToArray();
    }

    private static void EditStagControl(PlayMakerFSM fsm)
    {
        // Make the Stag immediately present at already-opened stations so the player doesn't have to wait on the bell.
        FsmState? locCheck = fsm.GetState("Current Location Check");
        if (locCheck != null)
        {
            locCheck.AddFirstAction(new Lambda(() =>
            {
                PlayerData? pd = PlayerData.instance;
                if (pd == null) return;

                FsmString? openedVar = fsm.FsmVariables.FindFsmString("Station Opened Bool");
                FsmInt? posVar = fsm.FsmVariables.FindFsmInt("Station Position Number");
                if (openedVar == null || posVar == null) return;

                string openedBool = openedVar.Value;
                int pos = posVar.Value;
                if (!string.IsNullOrEmpty(openedBool) && pos > 0 && pd.GetBool(openedBool))
                {
                    pd.SetInt(nameof(PlayerData.stagPosition), pos);
                }
            }));
        }

        // Shorten any remaining arrival/call waits when ringing the bell manually.
        if (fsm.FsmStates == null) return;
        foreach (FsmState state in fsm.FsmStates)
        {
            if (state?.Actions == null) continue;
            string name = state.Name ?? string.Empty;
            if (name.IndexOf("Arrive", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Call", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Wait", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Run", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            foreach (FsmStateAction action in state.Actions)
            {
                if (action is Wait wait && wait.time != null && wait.time.Value > 0.05f)
                {
                    wait.time.Value = 0.05f;
                }
            }
        }
    }

    [HarmonyPatch(typeof(DialogueBox), "ShowNextChar")]
    private static class FastDialoguePatch
    {
        private static bool Prefix(DialogueBox __instance, TextMeshPro? ___textMesh)
        {
            if (___textMesh == null) return true;
            TMP_TextInfo? info = ___textMesh.textInfo;
            if (info != null && info.pageInfo != null && __instance.currentPage > 0 && __instance.currentPage <= info.pageCount)
            {
                ___textMesh.maxVisibleCharacters = info.pageInfo[__instance.currentPage - 1].lastCharacterIndex + 1;
            }
            else
            {
                ___textMesh.maxVisibleCharacters = int.MaxValue;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(StagTravel))]
    private static class FastStagTravelPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPrefix]
        private static void StartPrefix(ref float ___minimumLoadDuration, ref float ___minimumSkipDuration)
        {
            ___minimumLoadDuration = 0f;
            ___minimumSkipDuration = 0f;
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static void UpdatePrefix(
            bool ___isAsync,
            bool ___isFetchComplete,
            ref bool ___isSkipping,
            ref bool ___isSkipFadeComplete,
            ref bool ___isReadyToActivate)
        {
            if (___isAsync)
            {
                if (___isFetchComplete && !___isSkipping)
                {
                    ___isSkipping = true;
                    ___isSkipFadeComplete = true;
                    ___isReadyToActivate = true;
                }
            }
            else
            {
                ___isSkipFadeComplete = true;
            }
        }
    }

    [HarmonyPatch(typeof(BigUIDef), nameof(BigUIDef.SendMessage))]
    private static class FastBigPopupPatch
    {
        private static void Prefix(ref MessageType type)
        {
            if ((type & MessageType.Big) == MessageType.Big)
            {
                type = (type & ~MessageType.Big) | MessageType.Corner;
            }
        }
    }

    [HarmonyPatch(typeof(LoreUIDef), nameof(LoreUIDef.SendMessage))]
    private static class FastLorePopupPatch
    {
        private static void Prefix(ref MessageType type)
        {
            if ((type & MessageType.Lore) == MessageType.Lore)
            {
                type = (type & ~MessageType.Lore) | MessageType.Corner;
            }
        }
    }

    [HarmonyPatch(typeof(GameMap))]
    private static class MapMarkersAndCompassPatch
    {
        private static readonly Dictionary<string, string> InteriorToMapRoom = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Room_shop"] = "Town",
            ["Room_mapper"] = "Town",
            ["Room_Ouiji"] = "Town",
            ["Room_Sly_Storeroom"] = "Town",
            ["Room_Town_Stag_Station"] = "Town",
            ["Room_Bretta"] = "Town",
            ["Room_Jinn"] = "Town",
            ["Grimm_Main_Tent"] = "Town",
            ["Room_Charm_Shop"] = "Crossroads_04",
            ["Room_Mender_House"] = "Crossroads_04",
            ["Crossroads_ShamanTemple"] = "Crossroads_06",
            ["Room_temple"] = "Crossroads_02",
            ["Room_Final_Boss_Atrium"] = "Crossroads_02",
            ["Room_Mansion"] = "RestingGrounds_05",
            ["Room_Colosseum_01"] = "Deepnest_East_09",
            ["Room_Colosseum_02"] = "Deepnest_East_09",
            ["Room_Wyrm"] = "Deepnest_East_12",
            ["Room_nailsmith"] = "Ruins1_04",
            ["Room_ruinhouse"] = "Ruins1_05b",
            ["Room_Queen"] = "Fungus3_48",
            ["Room_Mask_Maker"] = "Deepnest_35",
            ["Room_Slug_Shrine"] = "Fungus1_26",
            ["Room_Fungus_Shaman"] = "Fungus3_26",
            ["Room_Nailmaster"] = "Cliffs_02",
            ["Room_Nailmaster_02"] = "Fungus1_15",
            ["Room_Nailmaster_03"] = "Deepnest_East_06",
            ["Room_GG_Shortcut"] = "Waterways_09",
            ["Dream_Nailcollection"] = "RestingGrounds_04",
        };

        private static Sprite? _pinSprite;
        private static readonly Dictionary<string, GameObject> _pinsByRoom = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (GameObject Area, Transform Room)> _roomLookup = new(StringComparer.OrdinalIgnoreCase);
        private static GameMap? _indexedMap;
        private static bool _dirty = true;

        internal static void MarkDirty() => _dirty = true;

        [HarmonyPatch("PositionCompass")]
        [HarmonyPrefix]
        private static void PositionCompassPrefix(out bool __state)
        {
            PlayerData? pd = PlayerData.instance;
            __state = pd != null && pd.equippedCharm_2;
            if (pd != null)
            {
                pd.equippedCharm_2 = true;
            }
        }

        [HarmonyPatch("PositionCompass")]
        [HarmonyFinalizer]
        private static void PositionCompassFinalizer(bool __state)
        {
            PlayerData? pd = PlayerData.instance;
            if (pd != null)
            {
                pd.equippedCharm_2 = __state;
            }
        }

        [HarmonyPatch(nameof(GameMap.SetupMap))]
        [HarmonyPostfix]
        private static void SetupMapPostfix(GameMap __instance)
        {
            _dirty = true;
            RefreshPins(__instance);
        }

        [HarmonyPatch(nameof(GameMap.SetupMapMarkers))]
        [HarmonyPostfix]
        private static void SetupMapMarkersPostfix(GameMap __instance)
        {
            RefreshPins(__instance);
        }

        private static void EnsureIndexed(GameMap map)
        {
            if (_indexedMap == map && _roomLookup.Count > 0) return;

            _indexedMap = map;
            _roomLookup.Clear();
            foreach (GameObject pin in _pinsByRoom.Values)
            {
                if (pin != null) UnityEngine.Object.Destroy(pin);
            }
            _pinsByRoom.Clear();

            GameObject?[] areas =
            {
                map.areaAncientBasin,
                map.areaCity,
                map.areaCliffs,
                map.areaCrossroads,
                map.areaCrystalPeak,
                map.areaDeepnest,
                map.areaFogCanyon,
                map.areaFungalWastes,
                map.areaGreenpath,
                map.areaKingdomsEdge,
                map.areaQueensGardens,
                map.areaRestingGrounds,
                map.areaDirtmouth,
                map.areaWaterways,
            };

            foreach (GameObject? area in areas)
            {
                if (area == null) continue;
                Transform t = area.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    Transform child = t.GetChild(i);
                    if (child == null || string.IsNullOrEmpty(child.name)) continue;
                    if (child.name.StartsWith("HKSS_CheckPin_", StringComparison.Ordinal)) continue;
                    if (!_roomLookup.ContainsKey(child.name))
                    {
                        _roomLookup[child.name] = (area, child);
                    }
                }
            }
        }

        private static void RefreshPins(GameMap map)
        {
            if (map == null || Ref.Settings?.Placements == null) return;
            EnsureIndexed(map);

            if (!_dirty && _pinsByRoom.Count > 0) return;
            _dirty = false;

            var unobtainedByRoom = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AbstractPlacement pmt in Ref.Settings.Placements.Values)
            {
                if (pmt == null || pmt.Items == null || pmt.Items.Count == 0) continue;
                int remaining = 0;
                foreach (AbstractItem item in pmt.Items)
                {
                    if (item != null && !item.WasEverObtained()) remaining++;
                }
                if (remaining == 0) continue;

                string? sceneName = (pmt as ItemChanger.Placements.IPrimaryLocationPlacement)?.Location?.sceneName;
                if (string.IsNullOrEmpty(sceneName)) continue;

                string mapRoom = ResolveMapRoom(sceneName);
                if (!_roomLookup.ContainsKey(mapRoom)) continue;

                unobtainedByRoom[mapRoom] = unobtainedByRoom.TryGetValue(mapRoom, out int prev) ? prev + remaining : remaining;
            }

            foreach (var kv in _pinsByRoom)
            {
                if (kv.Value != null)
                {
                    kv.Value.SetActive(unobtainedByRoom.ContainsKey(kv.Key));
                }
            }

            foreach (var kv in unobtainedByRoom)
            {
                if (_pinsByRoom.TryGetValue(kv.Key, out GameObject? existing) && existing != null)
                {
                    existing.SetActive(true);
                    continue;
                }

                if (!_roomLookup.TryGetValue(kv.Key, out var entry) || entry.Area == null || entry.Room == null)
                    continue;

                GameObject pin = CreatePinObject(kv.Key, entry.Area, entry.Room);
                _pinsByRoom[kv.Key] = pin;
            }
        }

        private static string ResolveMapRoom(string sceneName)
        {
            if (_roomLookup.ContainsKey(sceneName)) return sceneName;
            if (InteriorToMapRoom.TryGetValue(sceneName, out string? mapped)) return mapped;
            return sceneName;
        }

        private static GameObject CreatePinObject(string roomName, GameObject area, Transform room)
        {
            var pin = new GameObject($"HKSS_CheckPin_{roomName}");
            pin.layer = area.layer;
            pin.transform.SetParent(area.transform, worldPositionStays: false);
            Vector3 pos = room.localPosition;
            pin.transform.localPosition = new Vector3(pos.x, pos.y, -0.5f);
            pin.transform.localScale = new Vector3(0.55f, 0.55f, 1f);

            var sr = pin.AddComponent<SpriteRenderer>();
            sr.sprite = GetPinSprite();
            sr.color = new Color(1f, 0.84f, 0.25f, 0.95f);
            sr.sortingOrder = 50;

            SpriteRenderer? roomSr = room.GetComponent<SpriteRenderer>();
            if (roomSr != null)
            {
                sr.sortingLayerID = roomSr.sortingLayerID;
                sr.sortingOrder = roomSr.sortingOrder + 10;
            }

            return pin;
        }

        private static Sprite GetPinSprite()
        {
            if (_pinSprite != null) return _pinSprite;

            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float center = (size - 1) * 0.5f;
            float outer = center - 1f;
            float inner = outer - 3f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Abs(x - center) + Mathf.Abs(y - center);
                    Color c;
                    if (dist <= inner)
                        c = Color.white;
                    else if (dist <= outer)
                        c = new Color(0.12f, 0.08f, 0.02f, 1f);
                    else
                        c = Color.clear;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            _pinSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
            return _pinSprite;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Procedural schematic room map for White Palace and Path of Pain on Hollow Knight's World Map.
/// Placed cleanly below Ancient Basin so White Palace &amp; Path of Pain checks appear on their exact
/// rooms instead of stacking on Palace Grounds, with live compass tracking when inside White Palace.
/// </summary>
internal static class HkWhitePalaceMap
{
    internal readonly struct RoomSpec
    {
        public readonly string SceneName;
        public readonly string Label;
        public readonly int GridX;
        public readonly int GridY;
        public readonly bool IsPathOfPain;

        public RoomSpec(string sceneName, string label, int gridX, int gridY, bool isPathOfPain = false)
        {
            SceneName = sceneName;
            Label = label;
            GridX = gridX;
            GridY = gridY;
            IsPathOfPain = isPathOfPain;
        }
    }

    private const float RoomWidth = 1.05f;
    private const float StepX = 1.35f;
    private const float StepY = 0.92f;
    private const float LineThickness = 0.055f;

    internal static readonly RoomSpec[] Rooms =
    {
        new("White_Palace_11", "Palace Entry", 1, 4),
        new("White_Palace_01", "Entrance Hall", 2, 4),
        new("White_Palace_02", "Entrance Wing", 3, 4),
        new("White_Palace_03_hub", "Central Hub", 2, 3),
        new("White_Palace_04", "Left Wing Lower", 0, 4),
        new("White_Palace_14", "Left Wing Upper", 0, 3),
        new("White_Palace_15", "Right Wing Entry", 3, 3),
        new("White_Palace_05", "Right Wing Sawblades", 4, 3),
        new("White_Palace_16", "Right Wing Upper", 5, 3),
        new("White_Palace_06", "Central Atrium", 2, 2),
        new("White_Palace_07", "Upper Ascent", 3, 2),
        new("White_Palace_12", "Upper Corridor", 3, 1),
        new("White_Palace_13", "Throne Tower", 4, 0),
        new("White_Palace_08", "Palace Workshop", 5, 0),
        new("White_Palace_09", "Throne Room", 3, 0),
        new("White_Palace_18", "PoP: Entrance", 0, 2, isPathOfPain: true),
        new("White_Palace_17", "PoP: Lever Room", 0, 1, isPathOfPain: true),
        new("White_Palace_19", "PoP: Vertical Shaft", 1, 1, isPathOfPain: true),
        new("White_Palace_20", "PoP: Final Gauntlet", 1, 0, isPathOfPain: true),
    };

    private static readonly (string From, string To)[] Routes =
    {
        ("White_Palace_11", "White_Palace_01"),
        ("White_Palace_01", "White_Palace_02"),
        ("White_Palace_01", "White_Palace_03_hub"),
        ("White_Palace_03_hub", "White_Palace_04"),
        ("White_Palace_03_hub", "White_Palace_14"),
        ("White_Palace_04", "White_Palace_14"),
        ("White_Palace_03_hub", "White_Palace_15"),
        ("White_Palace_15", "White_Palace_05"),
        ("White_Palace_05", "White_Palace_16"),
        ("White_Palace_03_hub", "White_Palace_06"),
        ("White_Palace_06", "White_Palace_07"),
        ("White_Palace_07", "White_Palace_12"),
        ("White_Palace_12", "White_Palace_13"),
        ("White_Palace_13", "White_Palace_09"),
        ("White_Palace_13", "White_Palace_08"),
        ("White_Palace_06", "White_Palace_18"),
        ("White_Palace_18", "White_Palace_17"),
        ("White_Palace_17", "White_Palace_19"),
        ("White_Palace_19", "White_Palace_20"),
    };

    private static readonly Dictionary<string, RoomSpec> SpecByScene = BuildSpecLookup();
    private static readonly Dictionary<string, Transform> RoomTransforms = new(StringComparer.OrdinalIgnoreCase);

    private static Sprite? _wpRoomSprite;
    private static Sprite? _popRoomSprite;
    private static Sprite? _lineSprite;

    internal static GameObject? AreaObject { get; private set; }

    private static Dictionary<string, RoomSpec> BuildSpecLookup()
    {
        var lookup = new Dictionary<string, RoomSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomSpec room in Rooms)
            lookup[room.SceneName] = room;
        return lookup;
    }

    internal static bool IsWhitePalaceScene(string? sceneName) =>
        !string.IsNullOrEmpty(sceneName) && SpecByScene.ContainsKey(sceneName!);

    internal static string? GetRoomLabel(string? sceneName) =>
        !string.IsNullOrEmpty(sceneName) && SpecByScene.TryGetValue(sceneName!, out RoomSpec spec)
            ? spec.Label
            : null;

    internal static bool TryGetRoomTransform(string? sceneName, out Transform? roomTransform)
    {
        roomTransform = null;
        return !string.IsNullOrEmpty(sceneName) && RoomTransforms.TryGetValue(sceneName!, out roomTransform) && roomTransform != null;
    }

    internal static GameObject BuildAndRegister(
        GameMap map,
        Dictionary<string, (GameObject Area, Transform Room, string MapOwnedBool)> roomLookup)
    {
        if (AreaObject != null)
            UnityEngine.Object.Destroy(AreaObject);
        RoomTransforms.Clear();

        EnsureSprites();

        int sortingLayerId = 0;
        int baseSortingOrder = 10;
        Vector3 anchorLocalPos = ComputeAnchorPosition(map, ref sortingLayerId, ref baseSortingOrder);

        var areaGo = new GameObject("HKSS_Area_WhitePalace");
        areaGo.layer = map.gameObject.layer;
        areaGo.transform.SetParent(map.transform, worldPositionStays: false);
        areaGo.transform.localPosition = anchorLocalPos;
        areaGo.transform.localScale = Vector3.one;

        var localPositions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomSpec room in Rooms)
        {
            float lx = (room.GridX - 2.5f) * StepX;
            float ly = (2.0f - room.GridY) * StepY;
            var pos = new Vector3(lx, ly, 0f);
            localPositions[room.SceneName] = pos;

            var roomGo = new GameObject(room.SceneName);
            roomGo.layer = areaGo.layer;
            roomGo.transform.SetParent(areaGo.transform, worldPositionStays: false);
            roomGo.transform.localPosition = pos;
            roomGo.transform.localScale = new Vector3(RoomWidth, RoomWidth, 1f);

            var sr = roomGo.AddComponent<SpriteRenderer>();
            sr.sprite = room.IsPathOfPain ? _popRoomSprite : _wpRoomSprite;
            sr.color = Color.white;
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = baseSortingOrder + 2;

            RoomTransforms[room.SceneName] = roomGo.transform;
            roomLookup[room.SceneName] = (areaGo, roomGo.transform, string.Empty);
        }

        // Draw connecting lines between adjacent rooms behind the room rectangles.
        foreach (var (fromScene, toScene) in Routes)
        {
            if (!localPositions.TryGetValue(fromScene, out Vector3 a) ||
                !localPositions.TryGetValue(toScene, out Vector3 b))
                continue;

            bool isPopEdge = SpecByScene[fromScene].IsPathOfPain && SpecByScene[toScene].IsPathOfPain;
            Vector3 diff = b - a;
            float dist = diff.magnitude;
            if (dist < 0.001f) continue;

            var lineGo = new GameObject($"HKSS_Route_{fromScene}_{toScene}");
            lineGo.layer = areaGo.layer;
            lineGo.transform.SetParent(areaGo.transform, worldPositionStays: false);
            lineGo.transform.localPosition = (a + b) * 0.5f + new Vector3(0f, 0f, 0.05f);
            lineGo.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
            lineGo.transform.localScale = new Vector3(dist, LineThickness, 1f);

            var lineSr = lineGo.AddComponent<SpriteRenderer>();
            lineSr.sprite = _lineSprite;
            lineSr.color = isPopEdge
                ? new Color(0.84f, 0.72f, 0.46f, 0.75f)
                : new Color(0.45f, 0.62f, 0.85f, 0.70f);
            lineSr.sortingLayerID = sortingLayerId;
            lineSr.sortingOrder = baseSortingOrder + 1;
        }

        areaGo.SetActive(false);
        AreaObject = areaGo;
        return areaGo;
    }

    internal static void SetVisible(bool visible)
    {
        if (AreaObject != null && AreaObject.activeSelf != visible)
            AreaObject.SetActive(visible);
    }

    internal static bool PositionCompassIfInside(GameMap map)
    {
        GameManager? gm = GameManager.instance;
        if (gm == null || map?.compassIcon == null || AreaObject == null || !AreaObject.activeInHierarchy)
            return false;

        if (!string.Equals(gm.GetCurrentMapZone(), "WHITE_PALACE", StringComparison.OrdinalIgnoreCase) &&
            !IsWhitePalaceScene(gm.sceneName))
            return false;

        if (!TryGetRoomTransform(gm.sceneName, out Transform? roomT) || roomT == null)
            return false;

        Vector3 pos = AreaObject.transform.localPosition + roomT.localPosition;
        pos.z = map.compassIcon.transform.localPosition.z;
        map.compassIcon.transform.localPosition = pos;
        map.compassIcon.SetActive(true);
        return true;
    }

    private static Vector3 ComputeAnchorPosition(GameMap map, ref int sortingLayerId, ref int baseSortingOrder)
    {
        GameObject? basin = map.areaAncientBasin;
        if (basin == null)
            return new Vector3(2.0f, -15.5f, 0f);

        Vector3 basinOrigin = basin.transform.localPosition;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        bool foundAny = false;

        Transform t = basin.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform child = t.GetChild(i);
            if (child == null || child.name.StartsWith("HKSS_", StringComparison.Ordinal)) continue;
            SpriteRenderer? sr = child.GetComponent<SpriteRenderer>();
            if (sr != null && !foundAny)
            {
                sortingLayerId = sr.sortingLayerID;
                baseSortingOrder = sr.sortingOrder;
            }
            Vector3 p = basinOrigin + child.localPosition;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            foundAny = true;
        }

        if (!foundAny)
            return new Vector3(basinOrigin.x, basinOrigin.y - 4.5f, 0f);

        float centerX = (minX + maxX) * 0.5f;
        // Top of White Palace sits at +2.15f above anchor, so anchor at minY - 3.6f leaves ~1.45f gap.
        return new Vector3(centerX, minY - 3.6f, 0f);
    }

    private static void EnsureSprites()
    {
        if (_wpRoomSprite == null)
        {
            _wpRoomSprite = CreateBoxSprite(
                fill: new Color(0.10f, 0.12f, 0.18f, 0.90f),
                border: new Color(0.52f, 0.72f, 0.95f, 0.95f));
        }

        if (_popRoomSprite == null)
        {
            _popRoomSprite = CreateBoxSprite(
                fill: new Color(0.15f, 0.13f, 0.11f, 0.92f),
                border: new Color(0.86f, 0.74f, 0.48f, 0.95f));
        }

        if (_lineSprite == null)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _lineSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }

    private static Sprite CreateBoxSprite(Color fill, Color border)
    {
        const int w = 48;
        const int h = 28;
        const int b = 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool isBorder = x < b || x >= w - b || y < b || y >= h - b;
                pixels[y * w + x] = isBorder ? border : fill;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        // 48x28 at 48 PPU yields a 1.0 x 0.583 unit sprite; uniform scale by RoomWidth keeps border thickness even.
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Illustrated proportional room map for White Palace and Path of Pain on Hollow Knight's World Map.
/// Placed cleanly below Ancient Basin so White Palace &amp; Path of Pain checks appear on their exact
/// rooms instead of stacking on Palace Grounds, with live compass tracking when inside White Palace.
/// Room proportions and layout mirror data/map_geometry.json.
/// </summary>
internal static class HkWhitePalaceMap
{
    internal readonly struct RoomSpec
    {
        public readonly string SceneName;
        public readonly string Label;
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;
        public readonly bool IsPathOfPain;

        public RoomSpec(string sceneName, string label, float x, float y, float width, float height, bool isPathOfPain = false)
        {
            SceneName = sceneName;
            Label = label;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            IsPathOfPain = isPathOfPain;
        }
    }

    // Scale from data/map_geometry.json White Palace units into HK GameMap units below Ancient Basin.
    private const float MapScale = 0.78f;
    private const float LineThickness = 0.065f;
    private const int PixelsPerUnit = 64;

    internal static readonly RoomSpec[] Rooms =
    {
        // Entry & lower wing
        new("White_Palace_11", "Palace Entry", -1.45f, -3.20f, 1.65f, 0.72f),
        new("White_Palace_01", "Entrance Hall", 0.35f, -3.15f, 1.50f, 0.85f),
        new("White_Palace_02", "Entrance Wing", 2.05f, -3.20f, 1.45f, 0.62f),
        // Central hub & left/right wings
        new("White_Palace_03_hub", "Central Hub", 0.35f, -1.75f, 1.35f, 1.35f),
        new("White_Palace_04", "Left Wing Lower", -1.65f, -2.10f, 1.75f, 0.78f),
        new("White_Palace_14", "Left Wing Upper", -1.80f, -1.00f, 2.00f, 0.82f),
        new("White_Palace_15", "Right Wing Entry", 2.05f, -1.95f, 1.50f, 0.75f),
        new("White_Palace_05", "Right Wing Sawblades", 3.95f, -1.85f, 1.85f, 0.88f),
        new("White_Palace_16", "Right Wing Upper", 4.20f, -0.70f, 1.55f, 0.85f),
        // Central atrium & upper spire
        new("White_Palace_06", "Central Atrium", 0.35f, -0.15f, 1.70f, 0.78f),
        new("White_Palace_07", "Upper Ascent", 2.05f, 0.35f, 1.25f, 1.35f),
        new("White_Palace_12", "Upper Corridor", 2.05f, 1.85f, 1.55f, 0.85f),
        new("White_Palace_13", "Throne Tower", 3.50f, 2.85f, 1.15f, 1.35f),
        new("White_Palace_08", "Palace Workshop", 5.05f, 2.85f, 1.35f, 0.90f),
        new("White_Palace_09", "Throne Room", 1.85f, 3.20f, 1.55f, 0.92f),
        // Path of Pain (branches left from Central Atrium)
        new("White_Palace_18", "PoP: Entrance", -1.80f, 0.35f, 1.95f, 0.82f, isPathOfPain: true),
        new("White_Palace_17", "PoP: Lever Room", -2.40f, 1.65f, 1.80f, 0.85f, isPathOfPain: true),
        new("White_Palace_19", "PoP: Vertical Shaft", -0.75f, 1.85f, 1.05f, 1.45f, isPathOfPain: true),
        new("White_Palace_20", "PoP: Final Gauntlet", -0.55f, 3.25f, 2.05f, 0.88f, isPathOfPain: true),
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
    private static readonly Dictionary<string, Sprite> RoomSprites = new(StringComparer.OrdinalIgnoreCase);

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
            var pos = new Vector3((room.X - 1.15f) * MapScale, room.Y * MapScale, 0f);
            localPositions[room.SceneName] = pos;

            var roomGo = new GameObject(room.SceneName);
            roomGo.layer = areaGo.layer;
            roomGo.transform.SetParent(areaGo.transform, worldPositionStays: false);
            roomGo.transform.localPosition = pos;
            roomGo.transform.localScale = Vector3.one;

            var sr = roomGo.AddComponent<SpriteRenderer>();
            sr.sprite = RoomSprites[room.SceneName];
            sr.color = Color.white;
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = baseSortingOrder + 2;

            RoomTransforms[room.SceneName] = roomGo.transform;
            roomLookup[room.SceneName] = (areaGo, roomGo.transform, string.Empty);
        }

        // Draw connecting doorway passages between adjacent rooms behind the room silhouettes.
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
                ? new Color(0.88f, 0.75f, 0.48f, 0.72f)
                : new Color(0.64f, 0.76f, 0.90f, 0.68f);
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
            return new Vector3(2.0f, -16.0f, 0f);

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
            return new Vector3(basinOrigin.x, basinOrigin.y - 4.8f, 0f);

        float centerX = (minX + maxX) * 0.5f;
        // Top of White Palace sits at ~(3.7 * MapScale = 2.89f) above anchor; leave a clean ~1.4f gap below Ancient Basin.
        return new Vector3(centerX, minY - 4.3f, 0f);
    }

    private static void EnsureSprites()
    {
        foreach (RoomSpec room in Rooms)
        {
            if (RoomSprites.ContainsKey(room.SceneName)) continue;
            Color fill = room.IsPathOfPain
                ? new Color(0.24f, 0.20f, 0.14f, 0.93f)
                : new Color(0.18f, 0.21f, 0.27f, 0.92f);
            Color innerBorder = room.IsPathOfPain
                ? new Color(0.90f, 0.78f, 0.50f, 0.28f)
                : new Color(0.78f, 0.85f, 0.95f, 0.24f);
            Color outerBorder = room.IsPathOfPain
                ? new Color(0.92f, 0.79f, 0.52f, 0.96f)
                : new Color(0.81f, 0.86f, 0.94f, 0.95f);

            RoomSprites[room.SceneName] = CreateIllustratedRoomSprite(
                room.Width * MapScale,
                room.Height * MapScale,
                fill,
                innerBorder,
                outerBorder,
                room.SceneName.GetHashCode());
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

    private static Sprite CreateIllustratedRoomSprite(
        float worldW, float worldH, Color fill, Color innerBorder, Color outerBorder, int seed)
    {
        int w = Mathf.Clamp(Mathf.RoundToInt(worldW * PixelsPerUnit), 28, 192);
        int h = Mathf.Clamp(Mathf.RoundToInt(worldH * PixelsPerUnit), 22, 160);
        int chamfer = Mathf.Clamp(Mathf.Min(w, h) / 7, 3, 8);
        const int borderW = 2;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color[w * h];
        Color clear = new(0f, 0f, 0f, 0f);

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            for (int x = 0; x < w; x++)
            {
                // Subtle deterministic hand-inked edge wobble (+/- 0.75 px).
                float wobble = Mathf.Sin((x * 0.21f) + (y * 0.17f) + (seed & 0xFF)) * 0.65f;
                float dx = Mathf.Min(x, w - 1 - x);
                float dy = Mathf.Min(y, h - 1 - y);
                float cornerDist = (dx + dy) - chamfer;
                float edgeDist = Mathf.Min(Mathf.Min(dx, dy), cornerDist * 0.78f) + wobble;

                if (edgeDist < 0f)
                {
                    pixels[y * w + x] = clear;
                    continue;
                }

                if (edgeDist < borderW)
                {
                    float aa = Mathf.Clamp01(edgeDist);
                    pixels[y * w + x] = new Color(outerBorder.r, outerBorder.g, outerBorder.b, outerBorder.a * aa);
                    continue;
                }

                // Subtle vertical parchment shading + delicate architectural inner frame line.
                float shade = 0.92f + 0.14f * v;
                Color c = new(fill.r * shade, fill.g * shade, fill.b * shade, fill.a);
                if (edgeDist >= borderW + 3f && edgeDist < borderW + 4.2f)
                {
                    c = Color.Lerp(c, innerBorder, innerBorder.a);
                }
                pixels[y * w + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), PixelsPerUnit);
    }
}

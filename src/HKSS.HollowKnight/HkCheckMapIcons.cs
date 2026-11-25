using System;
using System.Collections.Generic;
using HKSS.Core;
using UnityEngine;

namespace HKSS.HollowKnight;

internal enum HkPinCategory
{
    Generic = 0,
    Shop = 1,
    Skill = 2,
    Charm = 3,
    Key = 4,
    Dreamer = 5,
    Grub = 6,
    Mask = 7,
    Vessel = 8,
    Notch = 9,
    PaleOre = 10,
    Stag = 11,
    Map = 12,
    Relic = 13,
    Essence = 14,
    Geo = 15,
    Soul = 16,
    Lore = 17,
    Flame = 18,
}

/// <summary>
/// Clean-room category classification and procedural high-contrast 32x32 map pin icons
/// for Hollow Knight check markers, matching Silksong's category and shop pins.
/// </summary>
internal static class HkCheckMapIcons
{
    private static readonly Dictionary<HkPinCategory, Sprite> Sprites = new();
    private static Sprite? _hintOutlineSprite;

    private static readonly HashSet<string> ShopPlacements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sly",
        "Sly_(Key)",
        "Iselda",
        "Salubra",
        "Salubra_(Requires_Charms)",
        "Leg_Eater",
        "Grubfather",
        "Seer",
        "Egg_Shop",
    };

    internal static bool IsShopPlacement(string? placementName)
    {
        if (string.IsNullOrWhiteSpace(placementName)) return false;
        string clean = StripNumericSuffix(placementName!);
        return ShopPlacements.Contains(clean);
    }

    internal static HkPinCategory Categorize(string? placementName, string? rawLocationName)
    {
        if (IsShopPlacement(placementName) || IsShopPlacement(rawLocationName))
            return HkPinCategory.Shop;

        string candidate = !string.IsNullOrWhiteSpace(rawLocationName) ? rawLocationName! : (placementName ?? string.Empty);
        string clean = IdSpace.StripPrefix(candidate);
        string? itemType = HkLogicEvaluator.GetItemType(clean) ?? HkLogicEvaluator.GetItemType(StripNumericSuffix(clean));

        if (!string.IsNullOrEmpty(itemType))
        {
            return itemType switch
            {
                "Skill" or "Focus" or "Swim" or "CursedNail" or "SplitClaw" or "SplitCloak" or "SplitSuperdash" => HkPinCategory.Skill,
                "Charm" => HkPinCategory.Charm,
                "Key" or "ElevatorPass" => HkPinCategory.Key,
                "Dreamer" => HkPinCategory.Dreamer,
                "Grub" or "Mimic" => HkPinCategory.Grub,
                "Mask" => HkPinCategory.Mask,
                "Vessel" => HkPinCategory.Vessel,
                "Notch" => HkPinCategory.Notch,
                "Ore" => HkPinCategory.PaleOre,
                "Stag" => HkPinCategory.Stag,
                "Map" => HkPinCategory.Map,
                "Relic" or "Egg" => HkPinCategory.Relic,
                "Root" or "DreamWarrior" or "DreamBoss" => HkPinCategory.Essence,
                "Geo" or "Rock" or "Boss_Geo" or "JunkPitChest" => HkPinCategory.Geo,
                "Soul" or "Cocoon" => HkPinCategory.Soul,
                "Lore" or "PalaceLore" or "Journal" => HkPinCategory.Lore,
                "Flame" => HkPinCategory.Flame,
                _ => HkPinCategory.Generic,
            };
        }

        // Fallback keyword matching for locations whose names differ from item names
        if (clean.StartsWith("Grub-", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Mimic_Grub", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Grub;
        if (clean.StartsWith("Mask_Shard", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Mask;
        if (clean.StartsWith("Vessel_Fragment", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Vessel;
        if (clean.StartsWith("Charm_Notch", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Notch;
        if (clean.StartsWith("Pale_Ore", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.PaleOre;
        if (clean.StartsWith("Whispering_Root", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Boss_Essence", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Essence;
        if (clean.StartsWith("Geo_Rock", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Geo_Chest", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Boss_Geo", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Geo;
        if (clean.StartsWith("Soul_Totem", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Lifeblood_Cocoon", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Soul;
        if (clean.StartsWith("Lore_Tablet", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Journal_Entry", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Lore;
        if (clean.StartsWith("Grimmkin_Flame", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Flame;
        if (clean.StartsWith("Rancid_Egg", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("Wanderer's_Journal", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Hallownest_Seal", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("King's_Idol", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("Arcane_Egg", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Relic;
        if (clean.EndsWith("_Stag", StringComparison.OrdinalIgnoreCase) || clean.Contains("Stag_Nest", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Stag;
        if (clean.EndsWith("_Map", StringComparison.OrdinalIgnoreCase))
            return HkPinCategory.Map;

        return HkPinCategory.Generic;
    }

    internal static string GetCategoryLabel(HkPinCategory category) => category switch
    {
        HkPinCategory.Shop => "Shop",
        HkPinCategory.Skill => "Skill / Spell",
        HkPinCategory.Charm => "Charm",
        HkPinCategory.Key => "Key",
        HkPinCategory.Dreamer => "Dreamer",
        HkPinCategory.Grub => "Grub",
        HkPinCategory.Mask => "Mask Shard",
        HkPinCategory.Vessel => "Vessel Fragment",
        HkPinCategory.Notch => "Charm Notch",
        HkPinCategory.PaleOre => "Pale Ore",
        HkPinCategory.Stag => "Stag Station",
        HkPinCategory.Map => "Map",
        HkPinCategory.Relic => "Relic / Egg",
        HkPinCategory.Essence => "Essence",
        HkPinCategory.Geo => "Geo",
        HkPinCategory.Soul => "Soul / Lifeblood",
        HkPinCategory.Lore => "Lore / Journal",
        HkPinCategory.Flame => "Grimmkin Flame",
        _ => "Check",
    };

    internal static Sprite GetSprite(HkPinCategory category)
    {
        if (Sprites.TryGetValue(category, out Sprite? cached) && cached != null)
            return cached;

        Sprite created = BuildCategorySprite(category);
        Sprites[category] = created;
        return created;
    }

    internal static Sprite GetHintOutlineSprite()
    {
        if (_hintOutlineSprite != null) return _hintOutlineSprite;

        const int size = 36;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        float c = (size - 1) * 0.5f;
        float outer = c - 0.5f;
        float inner = outer - 2.6f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                Color col = (r <= outer && r >= inner) ? new Color(1f, 1f, 1f, 0.96f) : Color.clear;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        _hintOutlineSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return _hintOutlineSprite;
    }

    private static string StripNumericSuffix(string name)
    {
        int idx = name.LastIndexOf('_');
        if (idx > 0 && int.TryParse(name.Substring(idx + 1), out _))
            return name.Substring(0, idx);
        return name;
    }

    private static Sprite BuildCategorySprite(HkPinCategory category)
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        (Color fill, Color accent) = GetColors(category);
        Color border = new(0.08f, 0.07f, 0.10f, 1f);
        float c = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / (c - 1.5f); // -1..1
                float dy = (y - c) / (c - 1.5f); // -1..1
                float sdf = EvaluateShape(category, dx, dy);

                Color px;
                if (sdf <= -0.18f)
                {
                    px = IsAccentPixel(category, dx, dy) ? accent : fill;
                }
                else if (sdf <= 0.04f)
                {
                    px = border;
                }
                else
                {
                    px = Color.clear;
                }
                tex.SetPixel(x, y, px);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }

    private static (Color Fill, Color Accent) GetColors(HkPinCategory category) => category switch
    {
        HkPinCategory.Shop => (new Color(1.00f, 0.78f, 0.20f, 1f), new Color(0.45f, 0.25f, 0.04f, 1f)),
        HkPinCategory.Skill => (new Color(0.28f, 0.88f, 1.00f, 1f), new Color(0.92f, 0.99f, 1.00f, 1f)),
        HkPinCategory.Charm => (new Color(0.76f, 0.44f, 0.98f, 1f), new Color(0.96f, 0.86f, 1.00f, 1f)),
        HkPinCategory.Key => (new Color(0.98f, 0.68f, 0.24f, 1f), new Color(0.42f, 0.22f, 0.05f, 1f)),
        HkPinCategory.Dreamer => (new Color(0.93f, 0.91f, 0.98f, 1f), new Color(0.40f, 0.30f, 0.56f, 1f)),
        HkPinCategory.Grub => (new Color(0.32f, 0.86f, 0.42f, 1f), new Color(0.86f, 1.00f, 0.86f, 1f)),
        HkPinCategory.Mask => (new Color(0.96f, 0.96f, 0.94f, 1f), new Color(0.16f, 0.16f, 0.22f, 1f)),
        HkPinCategory.Vessel => (new Color(0.74f, 0.82f, 0.92f, 1f), new Color(0.18f, 0.22f, 0.32f, 1f)),
        HkPinCategory.Notch => (new Color(0.32f, 0.58f, 0.98f, 1f), new Color(0.88f, 0.95f, 1.00f, 1f)),
        HkPinCategory.PaleOre => (new Color(0.86f, 0.95f, 1.00f, 1f), new Color(0.42f, 0.64f, 0.80f, 1f)),
        HkPinCategory.Stag => (new Color(0.84f, 0.58f, 0.34f, 1f), new Color(0.36f, 0.22f, 0.10f, 1f)),
        HkPinCategory.Map => (new Color(0.92f, 0.82f, 0.60f, 1f), new Color(0.46f, 0.34f, 0.18f, 1f)),
        HkPinCategory.Relic => (new Color(0.94f, 0.64f, 0.28f, 1f), new Color(1.00f, 0.92f, 0.68f, 1f)),
        HkPinCategory.Essence => (new Color(0.98f, 0.42f, 0.72f, 1f), new Color(1.00f, 0.88f, 0.95f, 1f)),
        HkPinCategory.Geo => (new Color(0.86f, 0.84f, 0.74f, 1f), new Color(0.45f, 0.42f, 0.32f, 1f)),
        HkPinCategory.Soul => (new Color(0.48f, 0.78f, 0.98f, 1f), new Color(0.92f, 0.98f, 1.00f, 1f)),
        HkPinCategory.Lore => (new Color(0.56f, 0.72f, 0.72f, 1f), new Color(0.20f, 0.30f, 0.32f, 1f)),
        HkPinCategory.Flame => (new Color(0.98f, 0.30f, 0.22f, 1f), new Color(1.00f, 0.82f, 0.35f, 1f)),
        _ => (new Color(1.00f, 0.84f, 0.25f, 1f), new Color(1.00f, 0.96f, 0.78f, 1f)),
    };

    private static float EvaluateShape(HkPinCategory category, float x, float y)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        float r = Mathf.Sqrt(x * x + y * y);

        return category switch
        {
            // 4-point star for Skills
            HkPinCategory.Skill => (Mathf.Pow(ax, 0.65f) + Mathf.Pow(ay, 0.65f)) - 0.92f,
            // Rounded square / badge for Shops & Maps & Lore
            HkPinCategory.Shop => Mathf.Max(ax, ay) - 0.82f,
            HkPinCategory.Map => Mathf.Max(ax * 0.95f, ay * 1.15f) - 0.82f,
            HkPinCategory.Lore => Mathf.Max(ax * 1.15f, ay * 0.92f) - 0.82f,
            // Circle medallion for Charms, Essence, Notches, Vessel, Geo
            HkPinCategory.Charm or HkPinCategory.Essence or HkPinCategory.Notch or HkPinCategory.Vessel or HkPinCategory.Geo => r - 0.86f,
            // Hexagon for Pale Ore & Keys
            HkPinCategory.PaleOre or HkPinCategory.Key => Mathf.Max(ax * 0.866f + ay * 0.5f, ay) - 0.84f,
            // Teardrop / oval for Grub, Flame, Soul, Mask, Dreamer, Relic, Stag
            HkPinCategory.Grub or HkPinCategory.Flame or HkPinCategory.Soul => Mathf.Sqrt((x * 1.1f) * (x * 1.1f) + (y * 0.9f) * (y * 0.9f)) - 0.84f,
            HkPinCategory.Mask or HkPinCategory.Dreamer => Mathf.Sqrt((x * 1.15f) * (x * 1.15f) + (y * 0.88f) * (y * 0.88f)) - 0.84f,
            // Classic diamond for Generic & others
            _ => (ax + ay) - 0.92f,
        };
    }

    private static bool IsAccentPixel(HkPinCategory category, float x, float y)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        float r = Mathf.Sqrt(x * x + y * y);

        return category switch
        {
            HkPinCategory.Shop => (ax < 0.28f && ay < 0.42f) || (ax < 0.42f && Mathf.Abs(ay - 0.2f) < 0.12f),
            HkPinCategory.Skill => r < 0.28f,
            HkPinCategory.Charm => r > 0.36f && r < 0.52f,
            HkPinCategory.Key => r < 0.24f || (y < -0.1f && ax < 0.14f),
            HkPinCategory.Dreamer or HkPinCategory.Mask => (Mathf.Abs(ax - 0.28f) < 0.12f && Mathf.Abs(y - 0.05f) < 0.18f),
            HkPinCategory.Grub => (ax < 0.22f && y > 0.10f && y < 0.42f),
            HkPinCategory.Vessel => (ax < 0.12f && y > -0.25f && y < 0.40f),
            HkPinCategory.Notch => (ax + ay) < 0.42f,
            HkPinCategory.PaleOre => (ax + ay) < 0.36f,
            HkPinCategory.Essence => r > 0.34f && r < 0.52f,
            HkPinCategory.Flame => r < 0.32f && y < 0.1f,
            HkPinCategory.Lore => ay < 0.45f && ax < 0.45f && (Mathf.Abs(y - 0.22f) < 0.09f || Mathf.Abs(y + 0.12f) < 0.09f),
            _ => r < 0.24f,
        };
    }
}

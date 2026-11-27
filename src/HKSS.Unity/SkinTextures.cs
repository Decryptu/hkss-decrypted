// Atlas composition follows Patchwork's sprite loader:
// https://github.com/Ashiepaws/silksong-patchwork handlers/SpriteLoader.cs and util/SpriteUtil.cs
// at 06e3558d1cc7596b4e5c166d3f8f02f6b4dde71c, MIT License, Copyright (c) 2025 Ashiepaws.
// Patchwork draws each sprite on the GPU with a shader from its own asset bundle; this version
// composes on the CPU so it needs no bundle and works the same in both games.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HKSS.Unity;

/// <summary>
/// Texture work shared by both games' skins: loading skin images, swapping a material's texture
/// or a renderer's sprite while remembering the original, and building a tk2d atlas from a whole
/// replacement sheet and single sprite images.
/// </summary>
internal static class SkinTextures
{
    private static readonly Dictionary<string, Texture2D?> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Material, Texture> OriginalTextures = new();
    private static readonly Dictionary<SpriteRenderer, Sprite> OriginalSprites = new();
    private static readonly List<Object> Owned = new();

    /// <summary>Loads a PNG once per skin. Returns null when the file is missing or not an image.</summary>
    internal static Texture2D? Load(string path)
    {
        if (Loaded.TryGetValue(path, out Texture2D? cached) && cached != null) return cached;
        Texture2D? tex = null;
        try
        {
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(path) };
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    Object.Destroy(tex);
                    tex = null;
                    HkssPlugin.Log.LogWarning($"[Skins] {path} is not a readable PNG.");
                }
                else
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    Owned.Add(tex);
                }
            }
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not read {path}: {e.Message}");
            tex = null;
        }
        Loaded[path] = tex;
        return tex;
    }

    internal static void TrackOwned(Object obj) => Owned.Add(obj);

    /// <summary>The texture the game itself gave this material.</summary>
    internal static Texture? Original(Material material) =>
        OriginalTextures.TryGetValue(material, out Texture original) ? original : material.mainTexture;

    internal static void SetTexture(Material? material, Texture? texture)
    {
        if (material == null || texture == null) return;
        if (!OriginalTextures.ContainsKey(material))
        {
            if (material.mainTexture == null) return;
            OriginalTextures[material] = material.mainTexture;
        }
        material.mainTexture = texture;
        // Keep the game's filtering so a new sheet is as crisp as the one it replaces.
        if (OriginalTextures[material] is Texture original) texture.filterMode = original.filterMode;
    }

    internal static Sprite? OriginalSprite(SpriteRenderer renderer) =>
        OriginalSprites.TryGetValue(renderer, out Sprite original) ? original : renderer.sprite;

    internal static void SetSprite(SpriteRenderer? renderer, Sprite? sprite)
    {
        if (renderer == null || sprite == null) return;
        if (!OriginalSprites.ContainsKey(renderer)) OriginalSprites[renderer] = renderer.sprite;
        renderer.sprite = sprite;
    }

    /// <summary>A sprite made from a skin image, sized and anchored like the one it replaces.</summary>
    internal static Sprite MakeSprite(Texture2D tex, Sprite like)
    {
        Rect rect = like.rect;
        Vector2 pivot = rect.width > 0 && rect.height > 0
            ? new Vector2(like.pivot.x / rect.width, like.pivot.y / rect.height)
            : new Vector2(0.5f, 0.5f);
        float ppu = rect.width > 0 ? like.pixelsPerUnit * tex.width / rect.width : like.pixelsPerUnit;
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
        sprite.name = like.name;
        Owned.Add(sprite);
        return sprite;
    }

    /// <summary>Puts every texture and sprite back and frees what the last skin loaded.</summary>
    internal static void RestoreAll()
    {
        foreach (var pair in OriginalTextures)
        {
            if (pair.Key != null && pair.Value != null) pair.Key.mainTexture = pair.Value;
        }
        OriginalTextures.Clear();
        foreach (var pair in OriginalSprites)
        {
            if (pair.Key != null) pair.Key.sprite = pair.Value;
        }
        OriginalSprites.Clear();
        foreach (Object obj in Owned)
        {
            if (obj != null) Object.Destroy(obj);
        }
        Owned.Clear();
        Loaded.Clear();
    }

    /// <summary>
    /// Builds one atlas of a tk2d collection: the replacement sheet when there is one (else a copy of
    /// the game's atlas), with each replacement sprite drawn into its own rectangle, turned the way
    /// the atlas stores it. Returns null when nothing applies.
    /// </summary>
    internal static Texture2D? ComposeAtlas(
        tk2dSpriteCollectionData collection,
        Material material,
        string? sheetPath,
        IReadOnlyDictionary<string, string>? sprites)
    {
        Texture? original = Original(material);
        if (original == null || original.width == 0 || original.height == 0) return null;

        Texture2D? sheet = sheetPath != null ? Load(sheetPath) : null;
        if (sheet != null && (long)sheet.width * original.height != (long)sheet.height * original.width)
        {
            HkssPlugin.Log.LogWarning(
                $"[Skins] {sheetPath} is {sheet.width}x{sheet.height}, but {collection.name}/{material.name} is "
                + $"{original.width}x{original.height}. A sheet must keep the atlas's shape, so it was skipped.");
            sheet = null;
        }

        bool anySprite = false;
        if (sprites != null)
        {
            foreach (tk2dSpriteDefinition def in collection.spriteDefinitions)
            {
                if (def != null && def.material == material && !string.IsNullOrEmpty(def.name) && sprites.ContainsKey(def.name))
                {
                    anySprite = true;
                    break;
                }
            }
        }
        if (sheet == null && !anySprite) return null;
        if (!anySprite) return sheet;

        int width, height;
        Color32[] pixels;
        if (sheet != null)
        {
            width = sheet.width;
            height = sheet.height;
            pixels = sheet.GetPixels32();
        }
        else
        {
            pixels = ReadBack(original, out width, out height);
        }

        int drawn = 0;
        foreach (tk2dSpriteDefinition def in collection.spriteDefinitions)
        {
            if (def == null || def.material != material || string.IsNullOrEmpty(def.name)) continue;
            if (!sprites!.TryGetValue(def.name, out string path)) continue;
            Texture2D? image = Load(path);
            if (image == null) continue;
            if (DrawSprite(def, image, pixels, width, height)) drawn++;
        }

        var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"HKSS skin {collection.name} {material.name}",
            wrapMode = original.wrapMode,
            filterMode = original.filterMode,
        };
        atlas.SetPixels32(pixels);
        atlas.Apply(false, true);
        Owned.Add(atlas);
        HkssPlugin.Log.LogInfo($"[Skins] {collection.name}/{material.name}: {(sheet != null ? "sheet + " : string.Empty)}{drawn} sprite(s).");
        return atlas;
    }

    internal static Color32[] ReadBack(Texture source, out int width, out int height)
    {
        width = source.width;
        height = source.height;
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            return copy.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(copy);
        }
    }

#if DEBUG
    /// <summary>Development only: writes an RGBA image as a PNG.</summary>
    internal static void DevWritePng(string path, Color32[] pixels, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    /// <summary>Development only: the game's texture with its colours inverted.</summary>
    internal static void DevWriteInverted(string path, Texture source)
    {
        Color32[] pixels = ReadBack(source, out int width, out int height);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            pixels[i] = new Color32((byte)(255 - c.r), (byte)(255 - c.g), (byte)(255 - c.b), c.a);
        }
        DevWritePng(path, pixels, width, height);
    }

    /// <summary>
    /// Development only: a test image whose quarters are red (top left), green (top right), blue
    /// (bottom left) and yellow (bottom right), to check that sprites are drawn the right way up.
    /// </summary>
    internal static void DevWriteQuarters(string path, int width, int height)
    {
        width = Mathf.Max(2, width);
        height = Mathf.Max(2, height);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool top = y >= height / 2, left = x < width / 2;
                pixels[y * width + x] = top
                    ? (left ? new Color32(230, 40, 40, 255) : new Color32(40, 200, 60, 255))
                    : (left ? new Color32(40, 80, 230, 255) : new Color32(240, 220, 40, 255));
            }
        }
        DevWritePng(path, pixels, width, height);
    }
#endif

    /// <summary>
    /// Draws one sprite image into its rectangle of the atlas, turned the way Patchwork turns it, so
    /// images made from Patchwork or Stitchwork dumps land the right way round. For a rotated sprite
    /// (tk2d's <c>Tk2d</c> and <c>TPackerCW</c> flip modes) the image's width runs along the
    /// rectangle's height.
    /// </summary>
    private static bool DrawSprite(tk2dSpriteDefinition def, Texture2D image, Color32[] atlas, int width, int height)
    {
        Vector2[] uvs = def.uvs;
        if (uvs == null || uvs.Length == 0) return false;
        float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
        foreach (Vector2 uv in uvs)
        {
            minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x);
            minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y);
        }
        int xMin = Mathf.Clamp((int)(minU * width), 0, width);
        int xMax = Mathf.Clamp((int)(maxU * width), 0, width);
        int yMin = Mathf.Clamp((int)(minV * height), 0, height);
        int yMax = Mathf.Clamp((int)(maxV * height), 0, height);
        int w = xMax - xMin, h = yMax - yMin;
        if (w <= 0 || h <= 0) return false;

        Color32[] src = image.GetPixels32();
        int iw = image.width, ih = image.height;
        tk2dSpriteDefinition.FlipMode flip = def.flipped;
        for (int y = yMin; y < yMax; y++)
        {
            float fv = (y - yMin + 0.5f) / h;
            for (int x = xMin; x < xMax; x++)
            {
                float fu = (x - xMin + 0.5f) / w;
                // Where this atlas pixel sits in the upright image (0..1, origin bottom left).
                float sx, sy;
                switch (flip)
                {
                    case tk2dSpriteDefinition.FlipMode.Tk2d:
                        sx = 1f - fv;
                        sy = fu;
                        break;
                    case tk2dSpriteDefinition.FlipMode.TPackerCW:
                        sx = fv;
                        sy = 1f - fu;
                        break;
                    default:
                        sx = fu;
                        sy = fv;
                        break;
                }
                int ix = Mathf.Clamp((int)(sx * iw), 0, iw - 1);
                int iy = Mathf.Clamp((int)(sy * ih), 0, ih - 1);
                atlas[y * width + x] = src[iy * iw + ix];
            }
        }
        return true;
    }
}

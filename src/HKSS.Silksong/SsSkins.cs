// Ported from Patchwork: https://github.com/Ashiepaws/silksong-patchwork
// handlers/SpriteLoader.cs and handlers/T2DHandler.cs at 06e3558d1cc7596b4e5c166d3f8f02f6b4dde71c,
// MIT License, Copyright (c) 2025 Ashiepaws. Packs use the folder layout Patchwork and Stitchwork
// share, so existing packs work unchanged. Sounds, videos, text and recolours are not covered.
using System;
using System.Collections.Generic;
using HarmonyLib;
using HKSS.Core;
using HKSS.Unity;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HKSS.Silksong;

/// <summary>
/// Puts a Patchwork or Stitchwork pack on Hornet, her effects and the HUD: tk2d atlases are rebuilt
/// from the pack's sheets and sprite images, and Unity sprites (T2D) are swapped where they show.
/// </summary>
internal sealed class SsSkins : ISkinApplier
{
    private const float ScanInterval = 0.5f;

    private static SsSkins? _current;
    private static bool _patched;
    [ThreadStatic] private static bool _swapping;

    private SkinPackIndex _index = SkinPackIndex.Empty;
    private readonly Dictionary<string, Texture2D?> _atlases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Sprite, Sprite?> _t2d = new();
    private readonly Dictionary<Sprite, Sprite> _t2dOriginals = new();
    private readonly HashSet<SpriteRenderer> _watchedRenderers = new();
    private readonly HashSet<Image> _watchedImages = new();
    private float _nextScan;

    public bool Ready
    {
        get
        {
            GameManager? gm = GameManager._instance;
            return gm != null && gm.IsGameplayScene() && HeroController.instance != null;
        }
    }

    public SsSkins()
    {
        _current = this;
        if (_patched) return;
        _patched = true;
        var harmony = new Harmony("decryptu.hkss.skins");
        Patch(harmony, AccessTools.Method(typeof(tk2dSpriteCollectionData), "Init"), nameof(AfterCollectionInit), "tk2d atlases");
        Patch(harmony, AccessTools.PropertySetter(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite)), nameof(AfterRendererSprite), "sprites");
        Patch(harmony, AccessTools.PropertySetter(typeof(Image), nameof(Image.sprite)), nameof(AfterImageSprite), "UI sprites");
    }

    private static void Patch(Harmony harmony, System.Reflection.MethodBase? target, string postfix, string what)
    {
        if (target == null)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Hook for {what} not found; packs will not change them.");
            return;
        }
        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SsSkins), postfix));
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogWarning($"[Skins] Could not hook {what}: {e.Message}");
        }
    }

    public void Apply(SkinInfo? skin)
    {
        RestoreT2D();
        SkinTextures.RestoreAll();
        _atlases.Clear();
        _t2d.Clear();
        _index = skin == null ? SkinPackIndex.Empty : SkinPackIndex.Load(skin.ContentDir);
        if (skin == null) return;

        HkssPlugin.Log.LogInfo(
            $"[Skins] {skin.Id}: {_index.SpriteCount} sprite(s), {_index.SheetCount} sheet(s), {_index.T2DCount} T2D file(s).");
        foreach (tk2dSpriteCollectionData collection in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
            ApplyCollection(collection);
        ScanT2D();
    }

    public void OnSceneChanged() => _nextScan = 0f;

    public void LateUpdate()
    {
        if (!_index.HasT2D) return;
        _watchedRenderers.RemoveWhere(r => r == null);
        _watchedImages.RemoveWhere(i => i == null);
        foreach (SpriteRenderer renderer in _watchedRenderers) SwapRenderer(renderer);
        foreach (Image image in _watchedImages) SwapImage(image);
        if (Time.unscaledTime >= _nextScan) ScanT2D();
    }

#if DEBUG
    // Hornet's atlases inverted as whole sheets, her idle frames as quartered sprite images (to check
    // which way up they are drawn), and a few HUD sprites as quartered T2D images.
    public void DevWriteTestSkin(string folder)
    {
        string root = System.IO.Path.Combine(folder, "Stitchwork");
        tk2dSpriteAnimator? animator = HeroController.instance.GetComponent<tk2dSpriteAnimator>();
        tk2dSpriteAnimationClip? idle = animator?.GetClipByName("Idle");
        tk2dSpriteCollectionData? collection = idle?.frames[0].spriteCollection;
        if (collection != null)
        {
            foreach (Material material in collection.materials)
            {
                if (material?.mainTexture == null) continue;
                SkinTextures.DevWriteInverted(System.IO.Path.Combine(root, "Spritesheets", collection.name,
                    material.name.Split(' ')[0] + ".png"), material.mainTexture);
            }
            foreach (tk2dSpriteAnimationFrame frame in idle!.frames)
            {
                tk2dSpriteDefinition def = frame.spriteCollection.spriteDefinitions[frame.spriteId];
                float w = 0f, h = 0f;
                if (def.positions != null && def.positions.Length > 0)
                {
                    float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                    foreach (Vector3 p in def.positions)
                    {
                        minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                    }
                    w = maxX - minX;
                    h = maxY - minY;
                }
                int height = w > 0f ? Mathf.RoundToInt(120f * h / w) : 120;
                SkinTextures.DevWriteQuarters(System.IO.Path.Combine(root, "Sprites", frame.spriteCollection.name,
                    def.material.name.Split(' ')[0], def.name + ".png"), 120, height);
            }
            HkssPlugin.Log.LogInfo($"[dev] Hornet idle: {collection.name}, {idle.frames.Length} frame(s), flip {idle.frames[0].spriteCollection.spriteDefinitions[idle.frames[0].spriteId].flipped}, scale x {HeroController.instance.transform.localScale.x}.");
        }

        // The largest scene sprites near Hornet, so the screenshot shows them.
        var visible = new List<SpriteRenderer>();
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Sprite? sprite = renderer.sprite;
            if (sprite == null || sprite.texture == null || !renderer.enabled) continue;
            Vector2 offset = renderer.bounds.center - HeroController.instance.transform.position;
            if (Mathf.Abs(offset.x) > 12f || Mathf.Abs(offset.y) > 6f) continue;
            visible.Add(renderer);
        }
        HkssPlugin.Log.LogInfo($"[dev] {visible.Count} visible sprite renderer(s).");
        visible.Sort((a, b) => (b.bounds.size.x * b.bounds.size.y).CompareTo(a.bounds.size.x * a.bounds.size.y));
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SpriteRenderer renderer in visible)
        {
            if (written.Count >= 3) break;
            Sprite sprite = renderer.sprite;
            string textureName = sprite.texture.name ?? string.Empty;
            bool packed = textureName.Contains("-BC7-") || textureName.Contains("DXT5|BC3-");
            string file = packed
                ? System.IO.Path.Combine("T2D", CleanTextureName(textureName), sprite.name + ".png")
                : System.IO.Path.Combine("T2D", textureName + ".png");
            HkssPlugin.Log.LogInfo($"[dev] texture '{textureName}' sprite '{sprite.name}' rect {sprite.rect} tex {sprite.texture.width}x{sprite.texture.height}");
            if (!written.Add(file)) continue;
            SkinTextures.DevWriteQuarters(System.IO.Path.Combine(root, "Sprites", file),
                (int)sprite.rect.width, (int)sprite.rect.height);
            HkssPlugin.Log.LogInfo($"[dev] T2D test: {renderer.name} at {renderer.transform.position} -> {file}");
        }
    }
#endif

    private static void AfterCollectionInit(tk2dSpriteCollectionData __instance) => _current?.ApplyCollection(__instance);

    private void ApplyCollection(tk2dSpriteCollectionData? collection)
    {
        if (collection == null || collection.materials == null || !_index.Touches(collection.name)) return;
        foreach (Material material in collection.materials)
        {
            if (material == null) continue;
            string key = collection.name + "/" + material.name;
            if (!_atlases.TryGetValue(key, out Texture2D? atlas))
            {
                try
                {
                    atlas = Compose(collection, material);
                }
                catch (Exception e)
                {
                    HkssPlugin.Log.LogWarning($"[Skins] Could not rebuild {key}: {e.Message}");
                    atlas = null;
                }
                _atlases[key] = atlas;
            }
            if (atlas != null) SkinTextures.SetTexture(material, atlas);
        }
    }

    // Packs name an atlas by the material's first word ("atlas0"), or by the texture's own name.
    private Texture2D? Compose(tk2dSpriteCollectionData collection, Material material)
    {
        string atlasName = material.name.Split(' ')[0];
        string? textureName = SkinTextures.Original(material)?.name;
        string? sheet = _index.Sheet(collection.name, atlasName)
            ?? (textureName != null ? _index.Sheet(collection.name, textureName) : null);
        IReadOnlyDictionary<string, string>? sprites = _index.Sprites(collection.name, atlasName)
            ?? (textureName != null ? _index.Sprites(collection.name, textureName) : null);
        if (sheet == null && sprites == null) return null;
        return SkinTextures.ComposeAtlas(collection, material, sheet, sprites);
    }

    private void ScanT2D()
    {
        _nextScan = Time.unscaledTime + ScanInterval;
        if (!_index.HasT2D) return;
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            SwapRenderer(renderer);
        foreach (Image image in Object.FindObjectsByType<Image>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            SwapImage(image);
    }

    private static void AfterRendererSprite(SpriteRenderer __instance)
    {
        if (_swapping || _current == null || !_current._index.HasT2D) return;
        _current.SwapRenderer(__instance);
    }

    private static void AfterImageSprite(Image __instance)
    {
        if (_swapping || _current == null || !_current._index.HasT2D) return;
        _current.SwapImage(__instance);
    }

    private void SwapRenderer(SpriteRenderer renderer)
    {
        Sprite? shown = renderer.sprite;
        if (shown == null || _t2dOriginals.ContainsKey(shown)) return;
        Sprite? replacement = Replacement(shown);
        if (replacement == null) return;
        _watchedRenderers.Add(renderer);
        _swapping = true;
        try
        {
            renderer.sprite = replacement;
        }
        finally
        {
            _swapping = false;
        }
    }

    private void SwapImage(Image image)
    {
        Sprite? shown = image.sprite;
        if (shown == null || _t2dOriginals.ContainsKey(shown)) return;
        Sprite? replacement = Replacement(shown);
        if (replacement == null) return;
        _watchedImages.Add(image);
        _swapping = true;
        try
        {
            image.sprite = replacement;
        }
        finally
        {
            _swapping = false;
        }
    }

    private Sprite? Replacement(Sprite original)
    {
        if (_t2d.TryGetValue(original, out Sprite? cached)) return cached;
        Sprite? made = null;
        try
        {
            made = MakeReplacement(original);
        }
        catch (Exception e)
        {
            HkssPlugin.Log.LogDebug($"[Skins] T2D {original.name}: {e.Message}");
        }
        _t2d[original] = made;
        if (made != null) _t2dOriginals[made] = original;
        return made;
    }

    private Sprite? MakeReplacement(Sprite original)
    {
        Texture2D? texture = original.texture;
        if (texture == null) return null;
        string textureName = texture.name ?? string.Empty;
        bool packed = textureName.Contains("-BC7-") || textureName.Contains("DXT5|BC3-");
        string atlas = packed ? CleanTextureName(textureName) : textureName;

        string? path = packed
            ? _index.T2DSprite(atlas, original.name)
            : _index.T2DSprite(null, textureName) ?? _index.T2DSprite(null, original.name);
        if (path != null && SkinTextures.Load(path) is Texture2D image)
            return SkinTextures.MakeSprite(image, original);

        // A whole replacement atlas keeps every sprite's rectangle.
        string? sheetPath = _index.T2DSheet(atlas);
        if (sheetPath == null || SkinTextures.Load(sheetPath) is not Texture2D sheet) return null;
        if (sheet.width != texture.width || sheet.height != texture.height)
        {
            HkssPlugin.Log.LogWarning($"[Skins] {sheetPath} must be {texture.width}x{texture.height} like the game's atlas.");
            return null;
        }
        Rect rect = original.textureRect;
        Vector2 pivot = new(original.pivot.x / original.rect.width, original.pivot.y / original.rect.height);
        Sprite sprite = Sprite.Create(sheet, rect, pivot, original.pixelsPerUnit);
        sprite.name = original.name;
        SkinTextures.TrackOwned(sprite);
        return sprite;
    }

    // "sactx-0-2048x2048-BC7-Hud-1a2b3c4d" -> "Hud": the atlas name without its prefix and hash.
    private static string CleanTextureName(string textureName)
    {
        foreach (string marker in new[] { "-BC7-", "DXT5|BC3-" })
        {
            int at = textureName.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            string rest = textureName.Substring(at + marker.Length);
            int dash = rest.LastIndexOf('-');
            return dash > 0 ? rest.Substring(0, dash) : rest;
        }
        return textureName;
    }

    private void RestoreT2D()
    {
        if (_t2dOriginals.Count == 0) return;
        _swapping = true;
        try
        {
            foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.sprite != null && _t2dOriginals.TryGetValue(renderer.sprite, out Sprite original))
                    renderer.sprite = original;
            }
            foreach (Image image in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (image.sprite != null && _t2dOriginals.TryGetValue(image.sprite, out Sprite original))
                    image.sprite = original;
            }
        }
        finally
        {
            _swapping = false;
        }
        _t2dOriginals.Clear();
        _watchedRenderers.Clear();
        _watchedImages.Clear();
    }
}

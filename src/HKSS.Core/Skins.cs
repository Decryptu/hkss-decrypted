using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

/// <summary>One installed skin: a folder under <c>skins/&lt;game&gt;/</c>.</summary>
public sealed class SkinInfo
{
    public SkinInfo(string id, string displayName, string folder, string contentDir)
    {
        Id = id;
        DisplayName = displayName;
        Folder = folder;
        ContentDir = contentDir;
    }

    /// <summary>The folder name, stored in the player options.</summary>
    public string Id { get; }

    public string DisplayName { get; }

    public string Folder { get; }

    /// <summary>
    /// The folder that holds the images. For Hollow Knight it is the Custom Knight skin folder (with
    /// <c>Knight.png</c> and the rest); for Silksong it is the pack's <c>Stitchwork</c> or
    /// <c>Patchwork</c> folder, which holds <c>Sprites</c> and <c>Spritesheets</c>.
    /// </summary>
    public string ContentDir { get; }
}

/// <summary>
/// Finds installed skins. Skins are shared by every run: they live in the HKSS folder, next to
/// <c>runs</c>, in <c>skins/hollow_knight</c> (Custom Knight skin folders) and <c>skins/silksong</c>
/// (Patchwork or Stitchwork packs).
/// </summary>
public static class SkinLibrary
{
    /// <summary>The id of the game's own look.</summary>
    public const string Vanilla = "";

    private const int MaxSearchDepth = 3;

    /// <summary>The skins folder for a run folder (<c>&lt;home&gt;/runs/slot-n</c>).</summary>
    public static string RootForRun(string runDir) =>
        Path.GetFullPath(Path.Combine(runDir, "..", "..", "skins"));

    public static string GameFolder(string root, GameId game) => Path.Combine(root, game.Key());

    public static IReadOnlyList<SkinInfo> List(string root, GameId game)
    {
        string folder = GameFolder(root, game);
        var skins = new List<SkinInfo>();
        if (!Directory.Exists(folder)) return skins;

        foreach (string dir in SafeDirectories(folder).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(dir);
            string? content = game == GameId.HollowKnight ? FindKnightContent(dir, 0) : FindPackContent(dir, 0);
            if (content == null) continue;
            string name = game == GameId.Silksong ? PackDisplayName(content, id) : id;
            skins.Add(new SkinInfo(id, name, dir, content));
        }
        return skins;
    }

    public static SkinInfo? Find(IReadOnlyList<SkinInfo> skins, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return skins.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Picks an installed skin at random for a bench rest, never the one already worn when there is a
    /// choice. Returns <see cref="Vanilla"/> when nothing is installed.
    /// </summary>
    public static string PickRandom(IReadOnlyList<SkinInfo> skins, string? current, Random rng)
    {
        if (skins.Count == 0) return Vanilla;
        var choices = skins.Where(s => !string.Equals(s.Id, current, StringComparison.OrdinalIgnoreCase)).ToList();
        if (choices.Count == 0) return skins[0].Id;
        return choices[rng.Next(choices.Count)].Id;
    }

    /// <summary>
    /// A pack's name: <c>pack.json</c>'s <c>name</c>, else the folder name, where Thunderstore's
    /// <c>Author-Pack_Name</c> reads as <c>Pack Name</c>.
    /// </summary>
    public static string PackDisplayName(string contentDir, string folderName)
    {
        try
        {
            string json = Path.Combine(contentDir, "pack.json");
            if (File.Exists(json))
            {
                string? name = JObject.Parse(File.ReadAllText(json))["name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name!.Trim();
            }
        }
        catch (Exception)
        {
            // A broken pack.json only loses the pretty name.
        }

        string shown = folderName;
        int dash = shown.IndexOf('-');
        if (dash > 0 && dash < shown.Length - 1) shown = shown.Substring(dash + 1);
        return shown.Replace('_', ' ').Trim();
    }

    // Custom Knight skins are a folder of sheets (Knight.png, Sprint.png, ...). Archives often add one
    // more folder level, so look a little deeper for the folder with the images.
    private static string? FindKnightContent(string dir, int depth)
    {
        if (HasPng(dir) || HasPng(Path.Combine(dir, "Charms"))) return dir;
        if (depth >= MaxSearchDepth) return null;
        foreach (string sub in SafeDirectories(dir))
        {
            string? found = FindKnightContent(sub, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    // Patchwork and Stitchwork packs keep their files in a Patchwork/ or Stitchwork/ folder, which a
    // Thunderstore archive puts under plugins/. A bare Sprites/ or Spritesheets/ folder works too.
    private static string? FindPackContent(string dir, int depth)
    {
        if (IsPackContent(dir)) return dir;
        if (depth >= MaxSearchDepth) return null;
        string[] subs = SafeDirectories(dir);
        foreach (string sub in subs)
        {
            string name = Path.GetFileName(sub);
            if ((name.Equals("Stitchwork", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Patchwork", StringComparison.OrdinalIgnoreCase))
                && IsPackContent(sub))
                return sub;
        }
        foreach (string sub in subs)
        {
            string? found = FindPackContent(sub, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsPackContent(string dir) =>
        Directory.Exists(Path.Combine(dir, "Sprites")) || Directory.Exists(Path.Combine(dir, "Spritesheets"));

    private static bool HasPng(string dir)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.png").Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string[] SafeDirectories(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// The image files of a Silksong pack, in the layout Patchwork and Stitchwork share:
/// <list type="bullet">
/// <item><c>Sprites/{Collection}/{Material}/{Sprite}.png</c>: one tk2d sprite</item>
/// <item><c>Spritesheets/{Collection}/{Material}.png</c>: a whole tk2d atlas</item>
/// <item><c>Sprites/T2D/{Atlas}/{Sprite}.png</c> or <c>Sprites/T2D/{Sprite}.png</c>: one Unity sprite</item>
/// <item><c>Spritesheets/T2D/{Atlas}.png</c>: a whole Unity sprite atlas</item>
/// </list>
/// Names are matched without regard to case.
/// </summary>
public sealed class SkinPackIndex
{
    private const string T2D = "T2D";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _sprites = new(Names);
    private readonly Dictionary<string, Dictionary<string, string>> _sheets = new(Names);
    private readonly Dictionary<string, Dictionary<string, string>> _t2dSprites = new(Names);
    private readonly Dictionary<string, string> _t2dFlat = new(Names);
    private readonly Dictionary<string, string> _t2dSheets = new(Names);

    public static readonly SkinPackIndex Empty = new();

    public int SpriteCount { get; private set; }
    public int SheetCount => _sheets.Values.Sum(m => m.Count);
    public int T2DCount => _t2dFlat.Count + _t2dSprites.Values.Sum(a => a.Count) + _t2dSheets.Count;
    public bool HasT2D => T2DCount > 0;

    public static SkinPackIndex Load(string contentDir)
    {
        var index = new SkinPackIndex();
        index.ScanSprites(Path.Combine(contentDir, "Sprites"));
        index.ScanSheets(Path.Combine(contentDir, "Spritesheets"));
        return index;
    }

    /// <summary>True when the pack changes anything in this tk2d collection.</summary>
    public bool Touches(string collection) => _sprites.ContainsKey(collection) || _sheets.ContainsKey(collection);

    public string? Sheet(string collection, string material) =>
        _sheets.TryGetValue(collection, out var mats) && mats.TryGetValue(material, out string path) ? path : null;

    /// <summary>The sprite files for one atlas of a collection, keyed by sprite name.</summary>
    public IReadOnlyDictionary<string, string>? Sprites(string collection, string material) =>
        _sprites.TryGetValue(collection, out var mats) && mats.TryGetValue(material, out var sprites) ? sprites : null;

    public string? T2DSprite(string? atlas, string sprite)
    {
        if (!string.IsNullOrEmpty(atlas) && _t2dSprites.TryGetValue(atlas!, out var sprites)
            && sprites.TryGetValue(sprite, out string path))
            return path;
        return _t2dFlat.TryGetValue(sprite, out string flat) ? flat : null;
    }

    public string? T2DSheet(string atlas) => _t2dSheets.TryGetValue(atlas, out string path) ? path : null;

    private void ScanSprites(string root)
    {
        foreach (string[] parts in PngParts(root))
        {
            string name = parts[parts.Length - 1];
            if (Names.Equals(parts[0], T2D))
            {
                if (parts.Length == 2)
                    _t2dFlat[name] = Join(root, parts);
                else
                    Add(_t2dSprites, parts[parts.Length - 2], name, Join(root, parts));
                continue;
            }
            if (parts.Length < 3) continue;
            string collection = parts[parts.Length - 3];
            string material = parts[parts.Length - 2];
            if (!_sprites.TryGetValue(collection, out var mats))
                _sprites[collection] = mats = new Dictionary<string, Dictionary<string, string>>(Names);
            if (Add(mats, material, name, Join(root, parts))) SpriteCount++;
        }
    }

    private void ScanSheets(string root)
    {
        foreach (string[] parts in PngParts(root))
        {
            string name = parts[parts.Length - 1];
            if (Names.Equals(parts[0], T2D) && parts.Length == 2)
            {
                _t2dSheets[name] = Join(root, parts);
                continue;
            }
            if (parts.Length < 2) continue;
            Add(_sheets, parts[parts.Length - 2], name, Join(root, parts));
        }
    }

    private static bool Add(Dictionary<string, Dictionary<string, string>> map, string group, string name, string path)
    {
        if (!map.TryGetValue(group, out var entries))
            map[group] = entries = new Dictionary<string, string>(Names);
        if (entries.ContainsKey(name)) return false;
        entries[name] = path;
        return true;
    }

    // Relative path parts of every PNG, with the extension taken off the last part.
    private static IEnumerable<string[]> PngParts(string root)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.GetFiles(root, "*.png", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            yield break;
        }

        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in files)
        {
            if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = file.Substring(prefix.Length)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            parts[parts.Length - 1] = Path.GetFileNameWithoutExtension(parts[parts.Length - 1]);
            yield return parts;
        }
    }

    private static string Join(string root, string[] parts)
    {
        string[] copy = (string[])parts.Clone();
        copy[copy.Length - 1] += ".png";
        return Path.Combine(root, Path.Combine(copy));
    }
}

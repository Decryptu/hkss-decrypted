using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace HKSS.Core;

public sealed class BenchEntry : IEquatable<BenchEntry>
{
    public string SceneName { get; set; } = string.Empty;
    public string MarkerName { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RespawnType { get; set; } = 1;
    public string MapZoneName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Key => BuildKey(SceneName, MarkerName);

    public static string BuildKey(string? sceneName, string? markerName) =>
        $"{(sceneName ?? string.Empty).Trim()}|{(markerName ?? string.Empty).Trim()}";

    public bool Equals(BenchEntry? other) =>
        other != null
        && string.Equals(SceneName, other.SceneName, StringComparison.Ordinal)
        && string.Equals(MarkerName, other.MarkerName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as BenchEntry);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Key);
}

public sealed class BenchSaveData
{
    public List<BenchEntry> Discovered { get; set; } = new();
    public BenchEntry? LastBench { get; set; }
}

/// <summary>
/// Tracks discovered benches per game run with friendly area/display names and JSON persistence.
/// </summary>
public sealed class BenchStore
{
    private readonly GameId _game;
    private readonly Dictionary<string, BenchEntry> _discovered = new(StringComparer.Ordinal);
    private readonly List<BenchEntry> _order = new();

    public BenchEntry? LastBench { get; private set; }
    public IReadOnlyList<BenchEntry> Discovered => _order;
    public int Count => _order.Count;

    public BenchStore(GameId game)
    {
        _game = game;
    }

    public bool RecordBench(
        string sceneName,
        string markerName,
        int respawnType = 1,
        string? mapZoneName = null,
        bool setAsLast = true)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(markerName))
            return false;

        string scene = sceneName.Trim();
        string marker = markerName.Trim();
        if (BenchCatalog.IsIgnoredMarker(scene, marker))
            return false;

        string key = BenchEntry.BuildKey(scene, marker);
        bool added = false;
        if (!_discovered.TryGetValue(key, out BenchEntry? entry))
        {
            (string area, string display) = BenchCatalog.ResolveNames(_game, scene, marker, mapZoneName);
            entry = new BenchEntry
            {
                SceneName = scene,
                MarkerName = marker,
                AreaName = area,
                DisplayName = display,
                RespawnType = respawnType,
                MapZoneName = mapZoneName ?? string.Empty,
            };
            _discovered[key] = entry;
            _order.Add(entry);
            added = true;
        }
        else
        {
            if (respawnType > 0) entry.RespawnType = respawnType;
            if (!string.IsNullOrWhiteSpace(mapZoneName) && string.IsNullOrEmpty(entry.MapZoneName))
                entry.MapZoneName = mapZoneName!;
        }

        if (setAsLast)
            LastBench = entry;

        return added;
    }

    public int Merge(IEnumerable<BenchEntry> entries)
    {
        if (entries == null) return 0;
        int added = 0;
        foreach (BenchEntry e in entries)
        {
            if (e == null) continue;
            if (RecordBench(e.SceneName, e.MarkerName, e.RespawnType, e.MapZoneName, setAsLast: false))
                added++;
        }
        return added;
    }

    public List<(string Area, List<BenchEntry> Benches)> GetGroupedByArea(string? filter = null)
    {
        string needle = (filter ?? string.Empty).Trim();
        IEnumerable<BenchEntry> source = _order;
        if (needle.Length > 0)
        {
            source = source.Where(b =>
                b.DisplayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                || b.AreaName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                || b.SceneName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        return source
            .GroupBy(b => string.IsNullOrWhiteSpace(b.AreaName) ? "Other" : b.AreaName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.OrderBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    public string Serialize()
    {
        var data = new BenchSaveData
        {
            Discovered = new List<BenchEntry>(_order),
            LastBench = LastBench,
        };
        return JsonConvert.SerializeObject(data, Formatting.Indented);
    }

    public void LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        BenchSaveData? data = JsonConvert.DeserializeObject<BenchSaveData>(json);
        if (data == null) return;

        if (data.Discovered != null)
        {
            Merge(data.Discovered);
        }
        if (data.LastBench != null && !string.IsNullOrWhiteSpace(data.LastBench.SceneName))
        {
            RecordBench(
                data.LastBench.SceneName,
                data.LastBench.MarkerName,
                data.LastBench.RespawnType,
                data.LastBench.MapZoneName,
                setAsLast: true);
        }
    }

    public void SaveToFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize());
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public void LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        LoadFromJson(File.ReadAllText(path));
    }
}

/// <summary>
/// Friendly area and bench display names for Hollow Knight and Silksong benches,
/// with automatic formatting fallback for any unlisted or custom bench.
/// </summary>
public static class BenchCatalog
{
    private static readonly HashSet<string> IgnoredMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "HKSS_Gate_Respawn",
        "Death Respawn Marker",
        "Death Respawn Marker Init",
    };

    private static readonly Dictionary<string, (string Area, string Name)> HkBenchesByScene = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Town"] = ("Dirtmouth", "Dirtmouth"),
        ["Room_Mato"] = ("Howling Cliffs", "Mato's Hut"),
        ["Crossroads_04"] = ("Forgotten Crossroads", "Salubra"),
        ["Crossroads_30"] = ("Forgotten Crossroads", "Hot Springs"),
        ["Crossroads_47"] = ("Forgotten Crossroads", "Crossroads Stag"),
        ["Crossroads_ShamanTemple"] = ("Forgotten Crossroads", "Ancestral Mound"),
        ["Room_Final_Boss_Atrium"] = ("Forgotten Crossroads", "Black Egg Temple"),
        ["Fungus1_01b"] = ("Greenpath", "Waterfall"),
        ["Fungus1_15"] = ("Greenpath", "Stone Sanctuary"),
        ["Fungus1_16_alt"] = ("Greenpath", "Greenpath Stag"),
        ["Fungus1_31"] = ("Greenpath", "Toll Bench"),
        ["Fungus1_37"] = ("Greenpath", "Nailmaster Sheo"),
        ["Room_Slug_Shrine"] = ("Greenpath", "Lake of Unn"),
        ["Fungus3_archive"] = ("Fog Canyon", "Teacher's Archives"),
        ["Fungus2_02"] = ("Fungal Wastes", "Queen's Station"),
        ["Fungus2_13"] = ("Fungal Wastes", "Leg Eater"),
        ["Fungus2_26"] = ("Fungal Wastes", "Bretta"),
        ["Fungus2_31"] = ("Fungal Wastes", "Mantis Village"),
        ["Ruins1_02"] = ("City of Tears", "Quirrel"),
        ["Ruins1_18"] = ("City of Tears", "Toll Bench"),
        ["Ruins1_29"] = ("City of Tears", "City Storerooms"),
        ["Ruins1_31"] = ("City of Tears", "Watcher's Spire"),
        ["Ruins2_08"] = ("City of Tears", "King's Station"),
        ["Ruins_Bathhouse"] = ("City of Tears", "Pleasure House"),
        ["Waterways_02"] = ("Royal Waterways", "Waterways Bench"),
        ["GG_Atrium"] = ("Godhome", "Godhome Atrium"),
        ["GG_Atrium_Roof"] = ("Godhome", "Godhome Roof"),
        ["GG_Workshop"] = ("Godhome", "Hall of Gods"),
        ["Mines_18"] = ("Crystal Peak", "Crystal Guardian"),
        ["Mines_29"] = ("Crystal Peak", "Dark Room"),
        ["RestingGrounds_09"] = ("Resting Grounds", "Resting Grounds Stag"),
        ["RestingGrounds_12"] = ("Resting Grounds", "Grey Mourner"),
        ["Deepnest_14"] = ("Deepnest", "Failed Tramway"),
        ["Deepnest_30"] = ("Deepnest", "Hot Springs"),
        ["Deepnest_Spider_Town"] = ("Deepnest", "Beast's Den"),
        ["Abyss_18"] = ("Ancient Basin", "Basin Toll"),
        ["Abyss_22"] = ("Ancient Basin", "Hidden Station"),
        ["Deepnest_East_06"] = ("Kingdom's Edge", "Camp Bench"),
        ["Deepnest_East_13"] = ("Kingdom's Edge", "Colosseum"),
        ["Room_Colosseum_02"] = ("Kingdom's Edge", "Colosseum Bench"),
        ["Room_nailmaster"] = ("Kingdom's Edge", "Oro's Hut"),
        ["Hive_01"] = ("The Hive", "Hive Bench"),
        ["Fungus3_40"] = ("Queen's Gardens", "Cornifer"),
        ["Fungus3_50"] = ("Queen's Gardens", "Gardens Toll"),
        ["Fungus1_24"] = ("Queen's Gardens", "Gardens Stag"),
        ["White_Palace_01"] = ("White Palace", "Palace Entrance"),
        ["White_Palace_03_hub"] = ("White Palace", "Palace Atrium"),
        ["White_Palace_06"] = ("White Palace", "Palace Balcony"),
        ["Room_Tram"] = ("Tram", "Lower Tram"),
        ["Room_Tram_RG"] = ("Tram", "Upper Tram"),
    };

    private static readonly Dictionary<string, (string Area, string Name)> SsBenchesByScene = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bonetown"] = ("Bone Bottom", "Bone Bottom"),
        ["Belltown"] = ("Bellhart", "Bellhart"),
        ["Song_Enclave"] = ("Songclave", "Songclave"),
        ["Greymoor_08"] = ("Greymoor", "Halfway Home"),
        ["Bellway_01"] = ("The Marrow", "Marrow Bellway"),
        ["Bellway_02"] = ("Deep Docks", "Deep Docks Bellway"),
        ["Bellway_03"] = ("Far Fields", "Far Fields Bellway"),
        ["Bellway_04"] = ("Greymoor", "Greymoor Bellway"),
        ["Bellway_08"] = ("Shellwood", "Shellwood Bellway"),
        ["Bellway_City"] = ("Citadel", "Grand Bellway"),
        ["Tube_Hub"] = ("Citadel", "Terminus"),
        ["Slab_06"] = ("The Slab", "The Slab"),
    };

    public static bool IsIgnoredMarker(string sceneName, string markerName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(markerName))
            return true;
        if (IgnoredMarkers.Contains(markerName))
            return true;
        if (sceneName.Equals("Tut_01", StringComparison.OrdinalIgnoreCase)
            && markerName.IndexOf("Init", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return false;
    }

    public static (string Area, string DisplayName) ResolveNames(
        GameId game,
        string sceneName,
        string markerName,
        string? mapZoneName = null)
    {
        var table = game == GameId.HollowKnight ? HkBenchesByScene : SsBenchesByScene;
        if (table.TryGetValue(sceneName, out var exact))
        {
            return exact;
        }

        string area = !string.IsNullOrWhiteSpace(mapZoneName)
            ? FormatIdentifier(mapZoneName!)
            : InferAreaFromScene(game, sceneName);

        string display = FormatIdentifier(sceneName);
        if (!string.Equals(markerName, "RestBench", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(markerName, "Death Respawn Marker", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(markerName))
        {
            string cleanMarker = markerName
                .Replace("RestBench", string.Empty)
                .Replace("Respawn", string.Empty)
                .Trim('_', ' ', '-');
            if (cleanMarker.Length > 0)
            {
                display = $"{display} ({FormatIdentifier(cleanMarker)})";
            }
        }

        return (area, display);
    }

    private static string InferAreaFromScene(GameId game, string sceneName)
    {
        if (game == GameId.HollowKnight)
        {
            if (sceneName.StartsWith("Crossroads", StringComparison.OrdinalIgnoreCase)) return "Forgotten Crossroads";
            if (sceneName.StartsWith("Fungus1", StringComparison.OrdinalIgnoreCase)) return "Greenpath";
            if (sceneName.StartsWith("Fungus2", StringComparison.OrdinalIgnoreCase)) return "Fungal Wastes";
            if (sceneName.StartsWith("Fungus3", StringComparison.OrdinalIgnoreCase)) return "Queen's Gardens";
            if (sceneName.StartsWith("Ruins", StringComparison.OrdinalIgnoreCase)) return "City of Tears";
            if (sceneName.StartsWith("Mines", StringComparison.OrdinalIgnoreCase)) return "Crystal Peak";
            if (sceneName.StartsWith("Waterways", StringComparison.OrdinalIgnoreCase)) return "Royal Waterways";
            if (sceneName.StartsWith("Deepnest_East", StringComparison.OrdinalIgnoreCase)) return "Kingdom's Edge";
            if (sceneName.StartsWith("Deepnest", StringComparison.OrdinalIgnoreCase)) return "Deepnest";
            if (sceneName.StartsWith("Abyss", StringComparison.OrdinalIgnoreCase)) return "Ancient Basin";
            if (sceneName.StartsWith("RestingGrounds", StringComparison.OrdinalIgnoreCase)) return "Resting Grounds";
            if (sceneName.StartsWith("White_Palace", StringComparison.OrdinalIgnoreCase)) return "White Palace";
            if (sceneName.StartsWith("Hive", StringComparison.OrdinalIgnoreCase)) return "The Hive";
            if (sceneName.StartsWith("GG_", StringComparison.OrdinalIgnoreCase)) return "Godhome";
            if (sceneName.StartsWith("Cliffs", StringComparison.OrdinalIgnoreCase)) return "Howling Cliffs";
            return "Hallownest";
        }

        if (sceneName.StartsWith("Bone", StringComparison.OrdinalIgnoreCase)) return "The Marrow";
        if (sceneName.StartsWith("Dock", StringComparison.OrdinalIgnoreCase)) return "Deep Docks";
        if (sceneName.StartsWith("Ant", StringComparison.OrdinalIgnoreCase)) return "Hunter's March";
        if (sceneName.StartsWith("Greymoor", StringComparison.OrdinalIgnoreCase)) return "Greymoor";
        if (sceneName.StartsWith("Bell", StringComparison.OrdinalIgnoreCase)) return "Bellhart";
        if (sceneName.StartsWith("Shellwood", StringComparison.OrdinalIgnoreCase)) return "Shellwood";
        if (sceneName.StartsWith("Coral", StringComparison.OrdinalIgnoreCase)) return "Blasted Steps";
        if (sceneName.StartsWith("Under", StringComparison.OrdinalIgnoreCase)) return "Underworks";
        if (sceneName.StartsWith("Song", StringComparison.OrdinalIgnoreCase)
            || sceneName.StartsWith("Hang", StringComparison.OrdinalIgnoreCase)
            || sceneName.StartsWith("Library", StringComparison.OrdinalIgnoreCase)
            || sceneName.StartsWith("Cog", StringComparison.OrdinalIgnoreCase))
            return "Citadel";
        if (sceneName.StartsWith("Slab", StringComparison.OrdinalIgnoreCase)) return "The Slab";
        if (sceneName.StartsWith("Peak", StringComparison.OrdinalIgnoreCase)) return "Mount Fay";
        if (sceneName.StartsWith("Dust", StringComparison.OrdinalIgnoreCase)) return "Sinner's Road";
        if (sceneName.StartsWith("Shadow", StringComparison.OrdinalIgnoreCase)) return "Bilewater";
        if (sceneName.StartsWith("Crawl", StringComparison.OrdinalIgnoreCase)) return "Wormways";
        return "Pharloom";
    }

    public static string FormatIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string cleaned = raw.Replace('_', ' ').Trim();
        var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p.Length == 1)
                parts[i] = p.ToUpperInvariant();
            else if (p.All(char.IsUpper))
                parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
            else
                parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1);
        }
        return string.Join(" ", parts);
    }
}

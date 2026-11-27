using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

public enum ExtraCheckKind
{
    Lever = 0,
    BreakableWall = 1,
    Grass = 2,
}

public sealed class ExtraCheckEntry
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExtraCheckKind Kind { get; set; }
    public GameId Game { get; set; }
    public string Scene { get; set; } = string.Empty;
    public string MapRoom { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float SceneWidth { get; set; }
    public float SceneHeight { get; set; }
    public bool RequiresQuake { get; set; }
}

/// <summary>
/// Shared catalog of extra check types (levers, breakable walls, grass) for both Hollow Knight
/// and Silksong, loaded from the embedded <c>apworld/hkss/extra_checks_data.json</c>.
/// </summary>
public static class ExtraChecksCatalog
{
    private const string ResourceName = "HKSS.Core.Data.extra_checks_data.json";
    private const float MatchTolerance = 2.0f;

    private sealed class LoadedCatalog
    {
        public readonly List<ExtraCheckEntry> HollowKnight = new();
        public readonly List<ExtraCheckEntry> Silksong = new();
        public readonly Dictionary<long, ExtraCheckEntry> ById = new();
        public readonly Dictionary<string, ExtraCheckEntry> HkByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, ExtraCheckEntry> SsByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<ExtraCheckEntry>> HkByScene = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<ExtraCheckEntry>> SsByScene = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Lazy<LoadedCatalog> Catalog = new(Load);

    public static IReadOnlyList<ExtraCheckEntry> HollowKnightEntries => Catalog.Value.HollowKnight;
    public static IReadOnlyList<ExtraCheckEntry> SilksongEntries => Catalog.Value.Silksong;

    public static bool TryGetById(long combinedId, out ExtraCheckEntry? entry) =>
        Catalog.Value.ById.TryGetValue(combinedId, out entry);

    public static bool TryGetHkByName(string rawName, out ExtraCheckEntry? entry) =>
        Catalog.Value.HkByName.TryGetValue(IdSpace.StripPrefix(rawName), out entry);

    public static bool TryGetSsByName(string rawName, out ExtraCheckEntry? entry) =>
        Catalog.Value.SsByName.TryGetValue(IdSpace.StripPrefix(rawName), out entry);

    public static IReadOnlyList<ExtraCheckEntry> GetForScene(GameId game, string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return Array.Empty<ExtraCheckEntry>();

        var map = game == GameId.HollowKnight ? Catalog.Value.HkByScene : Catalog.Value.SsByScene;
        return map.TryGetValue(sceneName, out var list) ? list : Array.Empty<ExtraCheckEntry>();
    }

    /// <summary>
    /// Finds the grass check for a room, if the room has one.
    /// </summary>
    public static ExtraCheckEntry? FindGrassCheck(GameId game, string sceneName)
    {
        foreach (ExtraCheckEntry entry in GetForScene(game, sceneName))
        {
            if (entry.Kind == ExtraCheckKind.Grass)
                return entry;
        }
        return null;
    }

    /// <summary>
    /// Finds the best matching lever or breakable wall entry in <paramref name="sceneName"/>
    /// by object name and world position.
    /// </summary>
    public static ExtraCheckEntry? FindObjectCheck(
        GameId game,
        string sceneName,
        ExtraCheckKind kind,
        string objectName,
        float x,
        float y)
    {
        IReadOnlyList<ExtraCheckEntry> candidates = GetForScene(game, sceneName);
        if (candidates.Count == 0) return null;

        ExtraCheckEntry? bestExact = null;
        float bestExactDistSq = float.MaxValue;
        ExtraCheckEntry? bestPos = null;
        float bestPosDistSq = MatchTolerance * MatchTolerance;

        for (int i = 0; i < candidates.Count; i++)
        {
            ExtraCheckEntry c = candidates[i];
            if (c.Kind != kind) continue;

            float dx = x - c.X;
            float dy = y - c.Y;
            float d2 = dx * dx + dy * dy;

            bool nameMatch = string.Equals(c.ObjectName, objectName, StringComparison.OrdinalIgnoreCase);
            if (nameMatch && d2 < bestExactDistSq)
            {
                bestExact = c;
                bestExactDistSq = d2;
            }
            if (d2 <= bestPosDistSq)
            {
                bestPos = c;
                bestPosDistSq = d2;
            }
        }

        if (bestExact != null && bestExactDistSq <= 16f)
            return bestExact;
        if (bestPos != null)
            return bestPos;
        return bestExact;
    }

    private static LoadedCatalog Load()
    {
        var result = new LoadedCatalog();
        Assembly asm = typeof(ExtraChecksCatalog).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return result;

        using var reader = new StreamReader(stream);
        var root = JObject.Parse(reader.ReadToEnd());

        ParseGame(root["hollow_knight"] as JObject, GameId.HollowKnight, result);
        ParseGame(root["silksong"] as JObject, GameId.Silksong, result);
        return result;
    }

    private static void ParseGame(JObject? section, GameId game, LoadedCatalog target)
    {
        if (section == null) return;
        ParseKind(section["levers"] as JArray, ExtraCheckKind.Lever, game, target);
        ParseKind(section["breakable_walls"] as JArray, ExtraCheckKind.BreakableWall, game, target);
        ParseKind(section["grass"] as JArray, ExtraCheckKind.Grass, game, target);
    }

    private static void ParseKind(JArray? array, ExtraCheckKind kind, GameId game, LoadedCatalog target)
    {
        if (array == null) return;
        var list = game == GameId.HollowKnight ? target.HollowKnight : target.Silksong;
        var byName = game == GameId.HollowKnight ? target.HkByName : target.SsByName;
        var byScene = game == GameId.HollowKnight ? target.HkByScene : target.SsByScene;

        foreach (JToken token in array)
        {
            if (token is not JObject obj) continue;
            var entry = new ExtraCheckEntry
            {
                Id = obj.Value<long>("id"),
                Name = obj.Value<string>("name") ?? string.Empty,
                Kind = kind,
                Game = game,
                Scene = obj.Value<string>("scene") ?? string.Empty,
                MapRoom = obj.Value<string>("mapRoom") ?? (obj.Value<string>("scene") ?? string.Empty),
                Area = obj.Value<string>("area") ?? string.Empty,
                ObjectName = obj.Value<string>("objectName") ?? string.Empty,
                X = obj.Value<float?>("x") ?? 0f,
                Y = obj.Value<float?>("y") ?? 0f,
                SceneWidth = obj.Value<float?>("sceneWidth") ?? 1f,
                SceneHeight = obj.Value<float?>("sceneHeight") ?? 1f,
                RequiresQuake = obj.Value<bool?>("requiresQuake") ?? false,
            };

            list.Add(entry);
            target.ById[entry.Id] = entry;
            if (!string.IsNullOrEmpty(entry.Name))
                byName[entry.Name] = entry;
            if (!string.IsNullOrEmpty(entry.Scene))
            {
                if (!byScene.TryGetValue(entry.Scene, out var sceneList))
                {
                    sceneList = new List<ExtraCheckEntry>();
                    byScene[entry.Scene] = sceneList;
                }
                sceneList.Add(entry);
            }
        }
    }
}

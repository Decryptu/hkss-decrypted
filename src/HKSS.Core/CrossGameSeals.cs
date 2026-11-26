using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

/// <summary>
/// An area in one game that stays closed until a key item from the other game has been received.
/// Mirrored from <c>apworld/hkss/cross_game.py</c> (<c>slot_data["cross_game_seals"]</c>).
/// </summary>
public sealed class CrossGameSeal
{
    public GameId Game { get; }
    public string Area { get; }
    public IReadOnlyCollection<string> Scenes => _scenes;
    public string Key { get; }
    public long KeyId { get; }

    private readonly HashSet<string> _scenes;

    public CrossGameSeal(GameId game, string area, IEnumerable<string> scenes, string key, long keyId)
    {
        Game = game;
        Area = area ?? throw new ArgumentNullException(nameof(area));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        KeyId = keyId;
        _scenes = new HashSet<string>(scenes ?? throw new ArgumentNullException(nameof(scenes)), StringComparer.Ordinal);
    }

    public bool Contains(string? scene) => scene != null && _scenes.Contains(scene);

    /// <summary>The key's name without its game prefix, with Hollow Knight underscores shown as spaces.</summary>
    public string KeyDisplayName => IdSpace.StripPrefix(Key).Replace('_', ' ');

    /// <summary>The world the key comes from (the other game).</summary>
    public string KeyWorld => ItemRouter.WorldName(Game == GameId.HollowKnight ? GameId.Silksong : GameId.HollowKnight);

    public string Describe() => $"{Area} needs {KeyDisplayName} from {KeyWorld}";
}

public sealed class CrossGameSeals
{
    public const string SlotDataKey = "cross_game_seals";

    public static readonly CrossGameSeals None = new(Array.Empty<CrossGameSeal>());

    public IReadOnlyList<CrossGameSeal> All { get; }

    public CrossGameSeals(IEnumerable<CrossGameSeal> seals)
    {
        All = (seals ?? throw new ArgumentNullException(nameof(seals))).ToList();
    }

    public IEnumerable<CrossGameSeal> For(GameId game) => All.Where(seal => seal.Game == game);

    public IEnumerable<long> KeyIds => All.Select(seal => seal.KeyId).Distinct();

    /// <summary>Reads seals from the root slot data. Missing or malformed data means no seals.</summary>
    public static CrossGameSeals Parse(IReadOnlyDictionary<string, object>? slotData)
    {
        if (slotData == null || !slotData.TryGetValue(SlotDataKey, out object? raw) || raw == null)
            return None;

        JArray rows;
        try
        {
            rows = raw as JArray ?? JArray.FromObject(raw);
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidCastException)
        {
            return None;
        }

        var seals = new List<CrossGameSeal>();
        foreach (JToken token in rows)
        {
            if (token is not JObject row) continue;
            GameId? game = (string?)row["game"] switch
            {
                "hollow_knight" => GameId.HollowKnight,
                "silksong" => GameId.Silksong,
                _ => null,
            };
            string? area = (string?)row["area"];
            string? key = (string?)row["key"];
            long? keyId = row["key_id"]?.Type == JTokenType.Integer ? (long)row["key_id"]! : null;
            if (game == null || string.IsNullOrEmpty(area) || string.IsNullOrEmpty(key) || keyId == null) continue;
            if (row["scenes"] is not JArray sceneRows) continue;
            var scenes = sceneRows.Where(s => s.Type == JTokenType.String).Select(s => (string)s!).ToList();
            if (scenes.Count == 0) continue;
            seals.Add(new CrossGameSeal(game.Value, area!, scenes, key!, keyId.Value));
        }
        return seals.Count == 0 ? None : new CrossGameSeals(seals);
    }

    /// <summary>
    /// Returns the seal that stops a transition from <paramref name="fromScene"/> to
    /// <paramref name="toScene"/> in <paramref name="game"/>, or null when the move is allowed.
    /// Moving between scenes of the same sealed area is always allowed, so a player who got
    /// inside (by a warp, for example) is never trapped.
    /// </summary>
    public CrossGameSeal? Blocking(GameId game, string? fromScene, string? toScene, Func<long, bool> hasKey)
    {
        if (hasKey == null) throw new ArgumentNullException(nameof(hasKey));
        if (string.IsNullOrEmpty(toScene) || string.Equals(fromScene, toScene, StringComparison.Ordinal))
            return null;
        foreach (CrossGameSeal seal in For(game))
        {
            if (!seal.Contains(toScene) || seal.Contains(fromScene)) continue;
            if (!hasKey(seal.KeyId)) return seal;
        }
        return null;
    }
}

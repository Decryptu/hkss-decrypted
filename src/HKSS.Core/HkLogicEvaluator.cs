using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

public enum HkReachability
{
    Unknown = 0,
    Reachable = 1,
    Unreachable = 2,
}

public sealed class HkLogicInput
{
    public IEnumerable<string> OwnedItems { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int>? Options { get; set; }
    public IReadOnlyList<int>? NotchCosts { get; set; }
    public IReadOnlyDictionary<string, Dictionary<string, int>>? LocationCosts { get; set; }
    public IReadOnlyDictionary<string, string>? EntrancePairs { get; set; }
    public string? StartLocationName { get; set; }
    /// <summary>Scenes of cross-game seals that are still closed: nothing in them is reachable.</summary>
    public IReadOnlyCollection<string>? SealedScenes { get; set; }
    public int BaseNotches { get; set; } = 3;
    public int BaseMaskShards { get; set; } = 20;
}

public sealed class HkLogicState
{
    private readonly HkLogicGraph _graph;
    private readonly Dictionary<string, int> _counts;
    private readonly Dictionary<string, int> _options;
    private readonly IReadOnlyList<int> _notchCosts;
    private readonly IReadOnlyDictionary<string, Dictionary<string, int>>? _locationCosts;
    private readonly HashSet<string> _reachedNodes;

    internal HkLogicState(
        HkLogicGraph graph,
        Dictionary<string, int> counts,
        Dictionary<string, int> options,
        IReadOnlyList<int> notchCosts,
        IReadOnlyDictionary<string, Dictionary<string, int>>? locationCosts,
        HashSet<string> reachedNodes)
    {
        _graph = graph;
        _counts = counts;
        _options = options;
        _notchCosts = notchCosts;
        _locationCosts = locationCosts;
        _reachedNodes = reachedNodes;
    }

    public int Count(string term)
    {
        if (_counts.TryGetValue(term, out int c) && c > 0) return c;
        return _reachedNodes.Contains(term) ? 1 : 0;
    }

    public int Option(string name) => _options.TryGetValue(name, out int v) ? v : 0;

    public int NotchSum(int[] charmIndices)
    {
        int sum = 0;
        for (int i = 0; i < charmIndices.Length; i++)
        {
            int idx = charmIndices[i];
            if (idx >= 0 && idx < _notchCosts.Count)
                sum += _notchCosts[idx];
            else
                sum += 2;
        }
        return sum;
    }

    public bool IsNodeReached(string node) => _reachedNodes.Contains(node);

    internal IReadOnlyCollection<string> SealedScenes { get; set; } = Array.Empty<string>();

    /// <summary>True when the scene belongs to a cross-game seal that is still closed.</summary>
    public bool IsSceneSealed(string? scene) => scene != null && SealedScenes.Contains(scene);

    /// <summary>
    /// True when a logic node (a transition "Scene[gate]" or a waypoint named after a scene) is in
    /// a closed seal's scene. Mirrors <c>cross_game._hk_scene_of</c>.
    /// </summary>
    internal static bool IsNodeSealed(string node, IReadOnlyCollection<string> sealedScenes)
    {
        if (sealedScenes.Count == 0) return false;
        int bracket = node.IndexOf('[');
        string scene = bracket >= 0 ? node.Substring(0, bracket) : node;
        if (sealedScenes.Contains(scene)) return true;
        foreach (string candidate in sealedScenes)
        {
            if (node.Length > candidate.Length && node[candidate.Length] == '_' &&
                node.StartsWith(candidate, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public HkReachability EvaluateLocation(string rawLocationName)
    {
        if (string.IsNullOrWhiteSpace(rawLocationName)) return HkReachability.Unknown;
        string clean = IdSpace.StripPrefix(rawLocationName);

        if (!TryFindLocationRule(clean, out HkLogicExpr? rule))
            return HkReachability.Unknown;

        if (!rule!.Evaluate(this))
            return HkReachability.Unreachable;

        if (_locationCosts != null && _locationCosts.TryGetValue(clean, out Dictionary<string, int>? costs) && costs != null)
        {
            foreach (var kv in costs)
            {
                if (string.Equals(kv.Key, "GEO", StringComparison.OrdinalIgnoreCase)) continue;
                if (Count(kv.Key) < kv.Value)
                    return HkReachability.Unreachable;
            }
        }

        return HkReachability.Reachable;
    }

    public IReadOnlyList<string> ExplainBlocked(string rawLocationName)
    {
        var reasons = new List<string>();
        string clean = IdSpace.StripPrefix(rawLocationName);

        // 1. Report any unsatisfied non-Geo costs first (exact counts).
        if (_locationCosts != null && _locationCosts.TryGetValue(clean, out Dictionary<string, int>? costs) && costs != null)
        {
            foreach (var kv in costs)
            {
                if (string.Equals(kv.Key, "GEO", StringComparison.OrdinalIgnoreCase)) continue;
                int have = Count(kv.Key);
                if (have < kv.Value)
                {
                    reasons.Add($"Needs {kv.Value} {FormatTerm(kv.Key)} (have {have})");
                }
            }
        }

        if (TryFindLocationRule(clean, out HkLogicExpr? rule) && !rule!.Evaluate(this))
        {
            var missingAbilities = new HashSet<string>(StringComparer.Ordinal);
            bool missingRoom = false;
            CollectMissingRequirements(rule, missingAbilities, ref missingRoom);

            if (missingAbilities.Count > 0)
            {
                var sorted = missingAbilities.OrderBy(s => s, StringComparer.Ordinal).Take(4).ToList();
                string suffix = missingAbilities.Count > 4 ? ", ..." : string.Empty;
                reasons.Add("Missing: " + string.Join(", ", sorted) + suffix);
            }
            if (missingRoom)
            {
                reasons.Add("Room/entrance not yet reachable");
            }
        }

        if (reasons.Count == 0 && EvaluateLocation(clean) == HkReachability.Unreachable)
        {
            reasons.Add("Requirements not yet met");
        }

        return reasons;
    }

    private bool TryFindLocationRule(string clean, out HkLogicExpr? rule)
    {
        if (_graph.LocationRules.TryGetValue(clean, out rule))
            return true;

        // Multi-locations like "Sly_1", "Sly_(Key)_3", "Salubra_(Requires_Charms)_2", "Grubfather_10"
        int lastUnderscore = clean.LastIndexOf('_');
        if (lastUnderscore > 0 && int.TryParse(clean.Substring(lastUnderscore + 1), out _))
        {
            string baseName = clean.Substring(0, lastUnderscore);
            if (_graph.LocationRules.TryGetValue(baseName, out rule))
                return true;
        }

        // Also check node rules as fallback (for event-like checks)
        if (_graph.NodeRules.TryGetValue(clean, out rule))
            return true;

        rule = null;
        return false;
    }

    private void CollectMissingRequirements(HkLogicExpr expr, HashSet<string> missingAbilities, ref bool missingRoom)
    {
        switch (expr.Kind)
        {
            case HkLogicExprKind.And:
                foreach (HkLogicExpr child in expr.Children!)
                {
                    if (!child.Evaluate(this))
                        CollectMissingRequirements(child, missingAbilities, ref missingRoom);
                }
                break;

            case HkLogicExprKind.Or:
                // Only report from an OR if none of its branches are satisfied; pick the simplest branch with fewest missing items
                HkLogicExpr? bestBranch = null;
                int bestScore = int.MaxValue;
                foreach (HkLogicExpr branch in expr.Children!)
                {
                    if (branch.Evaluate(this)) return;
                    if (branch.ContainsUnsatisfiedOption(this)) continue;
                    int score = branch.EstimateLeafCount();
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestBranch = branch;
                    }
                }
                if (bestBranch != null)
                    CollectMissingRequirements(bestBranch, missingAbilities, ref missingRoom);
                break;

            case HkLogicExprKind.Has:
                if (Count(expr.Term!) < 1)
                {
                    if (IsGraphRoomOrTransition(expr.Term!))
                        missingRoom = true;
                    else
                        missingAbilities.Add(FormatTerm(expr.Term!));
                }
                break;

            case HkLogicExprKind.Gt:
                if (Count(expr.Term!) <= expr.Threshold)
                {
                    int needed = expr.Threshold + 1;
                    int have = Count(expr.Term!);
                    missingAbilities.Add($"{needed} {FormatTerm(expr.Term!)} (have {have})");
                }
                break;

            case HkLogicExprKind.Notches:
                int req = NotchSum(expr.CharmIndices!) + 1;
                int cur = Count("NOTCHES");
                if (cur < req)
                    missingAbilities.Add($"{req} Charm Notches (have {cur})");
                break;
        }
    }

    private bool IsGraphRoomOrTransition(string term)
    {
        if (term.IndexOf('[') >= 0) return true;
        return _graph.NodeRules.ContainsKey(term) && !term.StartsWith("Defeated_", StringComparison.Ordinal);
    }

    private static string FormatTerm(string term) => term switch
    {
        "LEFTCLAW" or "RIGHTCLAW" => "Mantis Claw",
        "LEFTDASH" => "Left Dash",
        "RIGHTDASH" => "Right Dash",
        "LEFTSUPERDASH" or "RIGHTSUPERDASH" => "Crystal Heart",
        "WINGS" => "Monarch Wings",
        "LANTERN" => "Lumafly Lantern",
        "DREAMNAIL" => "Dream Nail",
        "DREAMER" => "Dreamers",
        "SWIM" => "Isma's / Swim",
        "ACID" => "Isma's Tear",
        "QUAKE" => "Desolate Dive",
        "FIREBALL" => "Vengeful Spirit",
        "SCREAM" => "Howling Wraiths",
        "SPELLS" => "Spells",
        "FOCUS" => "Focus",
        "TRAM" => "Tram Pass",
        "SIMPLE" => "Simple Keys",
        "GRUBS" => "Grubs",
        "ESSENCE" => "Essence",
        "CHARMS" => "Charms",
        "RANCIDEGGS" => "Rancid Eggs",
        "MASKSHARDS" => "Mask Shards",
        "NOTCHES" => "Charm Notches",
        "FLAMES" => "Grimmkin Flames",
        "GRIMMCHILD" => "Grimmchild",
        "WHITEFRAGMENT" => "White Fragments",
        "LEFTSLASH" or "RIGHTSLASH" or "UPSLASH" => "Nail Slash",
        "CYCLONE" => "Cyclone Slash",
        _ => term.Replace('_', ' '),
    };
}

internal enum HkLogicExprKind
{
    Const,
    Has,
    Gt,
    Opt,
    Opt0,
    Notches,
    And,
    Or,
}

internal sealed class HkLogicExpr
{
    public HkLogicExprKind Kind { get; }
    public bool ConstValue { get; }
    public string? Term { get; }
    public int Threshold { get; }
    public int[]? CharmIndices { get; }
    public HkLogicExpr[]? Children { get; }

    public HkLogicExpr(bool value) { Kind = HkLogicExprKind.Const; ConstValue = value; }
    public HkLogicExpr(HkLogicExprKind kind, string term, int threshold = 0) { Kind = kind; Term = term; Threshold = threshold; }
    public HkLogicExpr(int[] charmIndices) { Kind = HkLogicExprKind.Notches; CharmIndices = charmIndices; }
    public HkLogicExpr(HkLogicExprKind kind, HkLogicExpr[] children) { Kind = kind; Children = children; }

    public bool Evaluate(HkLogicState state) => Kind switch
    {
        HkLogicExprKind.Const => ConstValue,
        HkLogicExprKind.Has => state.Count(Term!) >= 1,
        HkLogicExprKind.Gt => state.Count(Term!) > Threshold,
        HkLogicExprKind.Opt => state.Option(Term!) != 0,
        HkLogicExprKind.Opt0 => state.Option(Term!) == 0,
        HkLogicExprKind.Notches => state.Count("NOTCHES") > state.NotchSum(CharmIndices!),
        HkLogicExprKind.And => EvalAnd(state),
        HkLogicExprKind.Or => EvalOr(state),
        _ => false,
    };

    private bool EvalAnd(HkLogicState state)
    {
        for (int i = 0; i < Children!.Length; i++)
            if (!Children[i].Evaluate(state)) return false;
        return true;
    }

    private bool EvalOr(HkLogicState state)
    {
        for (int i = 0; i < Children!.Length; i++)
            if (Children[i].Evaluate(state)) return true;
        return false;
    }

    public bool ContainsUnsatisfiedOption(HkLogicState state) => Kind switch
    {
        HkLogicExprKind.Opt => state.Option(Term!) == 0,
        HkLogicExprKind.Opt0 => state.Option(Term!) != 0,
        HkLogicExprKind.And => Children!.Any(c => c.ContainsUnsatisfiedOption(state)),
        _ => false,
    };

    public int EstimateLeafCount() => Kind switch
    {
        HkLogicExprKind.And or HkLogicExprKind.Or => Children!.Sum(c => c.EstimateLeafCount()),
        _ => 1,
    };

    public static HkLogicExpr Parse(JToken token)
    {
        if (token.Type == JTokenType.Boolean)
            return new HkLogicExpr(token.Value<bool>());

        if (token.Type == JTokenType.String)
            return new HkLogicExpr(HkLogicExprKind.Has, token.Value<string>()!);

        if (token is JArray arr && arr.Count >= 2)
        {
            string op = arr[0].Value<string>() ?? string.Empty;
            switch (op)
            {
                case "&":
                {
                    var children = new HkLogicExpr[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++) children[i - 1] = Parse(arr[i]);
                    return new HkLogicExpr(HkLogicExprKind.And, children);
                }
                case "|":
                {
                    var children = new HkLogicExpr[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++) children[i - 1] = Parse(arr[i]);
                    return new HkLogicExpr(HkLogicExprKind.Or, children);
                }
                case "gt":
                    return new HkLogicExpr(HkLogicExprKind.Gt, arr[1].Value<string>()!, arr[2].Value<int>());
                case "opt":
                    return new HkLogicExpr(HkLogicExprKind.Opt, arr[1].Value<string>()!);
                case "opt0":
                    return new HkLogicExpr(HkLogicExprKind.Opt0, arr[1].Value<string>()!);
                case "notches":
                    return new HkLogicExpr(arr[1].Values<int>().ToArray());
            }
        }

        return new HkLogicExpr(false);
    }
}

internal sealed class HkLogicGraph
{
    public Dictionary<string, string> Starts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Connectors { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, int>> ItemEffects { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ItemTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HkLogicExpr> NodeRules { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HkLogicExpr> LocationRules { get; } = new(StringComparer.Ordinal);

    public static HkLogicGraph LoadEmbedded()
    {
        Assembly asm = typeof(HkLogicGraph).Assembly;
        using Stream stream = asm.GetManifestResourceStream("HKSS.Core.Data.hk_logic.json")
            ?? throw new InvalidOperationException("Embedded HK logic resource 'HKSS.Core.Data.hk_logic.json' not found.");
        using var reader = new StreamReader(stream);
        JObject root = JObject.Parse(reader.ReadToEnd());

        var graph = new HkLogicGraph();

        if (root["starts"] is JObject startsObj)
        {
            foreach (JProperty p in startsObj.Properties())
                graph.Starts[p.Name] = p.Value.Value<string>() ?? "Tutorial_01";
        }

        if (root["connectors"] is JObject connObj)
        {
            foreach (JProperty p in connObj.Properties())
            {
                string? dst = p.Value.Value<string>();
                if (!string.IsNullOrEmpty(dst))
                    graph.Connectors[p.Name] = dst!;
            }
        }

        if (root["itemEffects"] is JObject effObj)
        {
            foreach (JProperty p in effObj.Properties())
            {
                if (p.Value is not JObject sub) continue;
                var dict = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (JProperty ep in sub.Properties())
                    dict[ep.Name] = ep.Value.Value<int>();
                graph.ItemEffects[p.Name] = dict;
            }
        }

        if (root["itemTypes"] is JObject typesObj)
        {
            foreach (JProperty p in typesObj.Properties())
                graph.ItemTypes[p.Name] = p.Value.Value<string>() ?? string.Empty;
        }

        if (root["nodeRules"] is JObject nodeObj)
        {
            foreach (JProperty p in nodeObj.Properties())
                graph.NodeRules[p.Name] = HkLogicExpr.Parse(p.Value);
        }

        if (root["locationRules"] is JObject locObj)
        {
            foreach (JProperty p in locObj.Properties())
                graph.LocationRules[p.Name] = HkLogicExpr.Parse(p.Value);
        }

        return graph;
    }
}

/// <summary>
/// Clean-room Archipelago Hollow Knight reachability evaluator built directly from
/// MIT-licensed <c>worlds/hk</c> logic rules.
/// </summary>
public static class HkLogicEvaluator
{
    private static readonly Lazy<HkLogicGraph> GraphLazy = new(HkLogicGraph.LoadEmbedded);

    private static readonly Dictionary<string, string> AlternateStartNodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["King's Pass"] = "Tutorial_01",
        ["Kings Pass"] = "Tutorial_01",
        ["king's_pass"] = "Tutorial_01",
        ["Stag Nest"] = "Cliffs_03[right1]",
        ["West Crossroads"] = "Crossroads_36[right1]",
        ["East Crossroads"] = "Crossroads_03[top1]",
        ["Ancestral Mound"] = "Crossroads_ShamanTemple[left1]",
        ["West Fog Canyon"] = "Fungus3_30[bot1]",
        ["East Fog Canyon"] = "Fungus3_25[right1]",
        ["Queen's Station"] = "Fungus2_01[left1]",
        ["Fungal Wastes"] = "Fungus2_28[left1]",
        ["Greenpath"] = "Fungus1_32[left1]",
        ["Lower Greenpath"] = "Fungus1_13[right1]",
        ["West Blue Lake"] = "Crossroads_50[left1]",
        ["City Storerooms"] = "Ruins1_17[right1]",
        ["King's Station"] = "Ruins2_10b[right1]",
        ["Outside Colosseum"] = "Deepnest_East_09[right1]",
        ["Crystallized Mound"] = "Mines_35[left1]",
        ["Dirtmouth"] = "Town",
    };

    public static string? GetItemType(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string clean = IdSpace.StripPrefix(rawName);
        return GraphLazy.Value.ItemTypes.TryGetValue(clean, out string? t) ? t : null;
    }

    public static HkLogicState Evaluate(HkLogicInput input)
    {
        HkLogicGraph graph = GraphLazy.Value;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var options = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (input.Options != null)
        {
            foreach (var kv in input.Options)
                options[kv.Key] = kv.Value;
        }

        // Seed base Knight stats
        if (input.BaseNotches > 0) AddCount(counts, "NOTCHES", input.BaseNotches);
        if (input.BaseMaskShards > 0) AddCount(counts, "MASKSHARDS", input.BaseMaskShards);

        // Seed vanilla starting items unless randomized
        if (GetOption(options, "RandomizeFocus") == 0)
            ApplyItem(graph, counts, "Focus");
        if (GetOption(options, "RandomizeSwim") == 0)
            ApplyItem(graph, counts, "Swim");
        if (GetOption(options, "RandomizeNail") == 0)
        {
            ApplyItem(graph, counts, "Upslash");
            ApplyItem(graph, counts, "Leftslash");
            ApplyItem(graph, counts, "Rightslash");
        }

        // Apply all owned/received items
        foreach (string rawItem in input.OwnedItems)
        {
            if (string.IsNullOrWhiteSpace(rawItem)) continue;
            ApplyItem(graph, counts, IdSpace.StripPrefix(rawItem));
        }

        // Build active connectors map (supporting entrance randomizer overrides)
        Dictionary<string, string> connectors = graph.Connectors;
        if (input.EntrancePairs != null && input.EntrancePairs.Count > 0)
        {
            connectors = new Dictionary<string, string>(graph.Connectors, StringComparer.Ordinal);
            foreach (var kv in input.EntrancePairs)
            {
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    connectors[kv.Key] = kv.Value;
            }
        }

        // Seed start node (and follow its outgoing connector immediately when starting on a transition)
        IReadOnlyCollection<string> sealedScenes = input.SealedScenes != null
            ? new HashSet<string>(input.SealedScenes, StringComparer.Ordinal)
            : (IReadOnlyCollection<string>)Array.Empty<string>();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        string startNode = ResolveStartNode(graph, input.StartLocationName);
        reached.Add(startNode);
        if (connectors.TryGetValue(startNode, out string? startTarget)
            && !string.IsNullOrEmpty(startTarget)
            && !HkLogicState.IsNodeSealed(startTarget!, sealedScenes))
        {
            reached.Add(startTarget!);
        }

        IReadOnlyList<int> notchCosts = input.NotchCosts ?? Array.Empty<int>();
        var state = new HkLogicState(graph, counts, options, notchCosts, input.LocationCosts, reached);
        state.SealedScenes = sealedScenes;

        // Fixed-point propagation across rooms, transitions, connectors, and events
        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 64)
        {
            changed = false;
            foreach (var kv in graph.NodeRules)
            {
                if (reached.Contains(kv.Key)) continue;
                if (HkLogicState.IsNodeSealed(kv.Key, sealedScenes)) continue;
                if (kv.Value.Evaluate(state))
                {
                    reached.Add(kv.Key);
                    changed = true;
                    if (connectors.TryGetValue(kv.Key, out string? target) && !string.IsNullOrEmpty(target)
                        && !HkLogicState.IsNodeSealed(target!, sealedScenes))
                    {
                        if (reached.Add(target!))
                            changed = true;
                    }
                }
            }
        }

        return state;
    }

    private static string ResolveStartNode(HkLogicGraph graph, string? startLocationName)
    {
        if (string.IsNullOrWhiteSpace(startLocationName))
            return "Tutorial_01";

        string normalized = startLocationName!.Trim().ToLowerInvariant().Replace(' ', '_');
        if (graph.Starts.TryGetValue(normalized, out string? fromNormalized))
            return fromNormalized;

        if (graph.Starts.TryGetValue(startLocationName!, out string? fromGraph))
            return fromGraph;

        if (AlternateStartNodes.TryGetValue(startLocationName!, out string? mapped))
            return mapped;

        return "Tutorial_01";
    }

    private static int GetOption(Dictionary<string, int> options, string key) =>
        options.TryGetValue(key, out int v) ? v : 0;

    private static void ApplyItem(HkLogicGraph graph, Dictionary<string, int> counts, string itemName)
    {
        AddCount(counts, itemName, 1);

        if (graph.ItemEffects.TryGetValue(itemName, out Dictionary<string, int>? effects))
        {
            foreach (var kv in effects)
                AddCount(counts, kv.Key, kv.Value);
        }

        // Split Mothwing Cloak + Shade Cloak sync (matches HKWorld.collect_effects)
        if (itemName == "Left_Mothwing_Cloak" || itemName == "Right_Mothwing_Cloak")
        {
            counts.TryGetValue("LEFTDASH", out int left);
            counts.TryGetValue("RIGHTDASH", out int right);
            if (left > 0 && right > 0)
            {
                int max = Math.Max(left, right);
                counts["LEFTDASH"] = max;
                counts["RIGHTDASH"] = max;
            }
        }
    }

    private static void AddCount(Dictionary<string, int> counts, string key, int delta)
    {
        counts[key] = counts.TryGetValue(key, out int cur) ? cur + delta : delta;
    }
}

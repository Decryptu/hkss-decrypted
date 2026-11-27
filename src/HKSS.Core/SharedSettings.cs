using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

/// <summary>
/// Cross-game QoL and HUD preferences shared two-way between the Launcher (<c>launcher.json</c>)
/// and both games' in-game F6 Links &amp; QoL panel.
/// </summary>
public sealed class SharedSettings
{
    public const string EnvVariable = "HKSS_SETTINGS_FILE";

    public FastEndingsMode FastEndings { get; set; } = FastEndingsMode.Credits;
    public bool BulkRelicTurnIn { get; set; } = true;
    public bool AutoQuill { get; set; }
    public bool ShowMapLogic { get; set; } = true;
    public bool ShowRecentItems { get; set; }
    public bool DamageNumbers { get; set; }
    public bool EnemyHealthBars { get; set; }
    public bool BossHealthBar { get; set; }

    public static SharedSettings Load(string? path, string? fastEndingsFallbackEnv = null)
    {
        var result = new SharedSettings();
        if (!string.IsNullOrWhiteSpace(fastEndingsFallbackEnv))
            result.FastEndings = FastEndingsPolicy.Parse(fastEndingsFallbackEnv);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return result;

        try
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            if (root.TryGetValue("fast_endings", StringComparison.OrdinalIgnoreCase, out JToken? fe) && fe.Type == JTokenType.String)
                result.FastEndings = FastEndingsPolicy.Parse(fe.Value<string>());
            if (TryReadBool(root, "bulk_relic_turn_in", out bool bulk)) result.BulkRelicTurnIn = bulk;
            if (TryReadBool(root, "auto_quill", out bool quill)) result.AutoQuill = quill;
            if (TryReadBool(root, "show_map_logic", out bool logic)) result.ShowMapLogic = logic;
            if (TryReadBool(root, "show_recent_items", out bool recent)) result.ShowRecentItems = recent;
            if (TryReadBool(root, "damage_numbers", out bool dmg)) result.DamageNumbers = dmg;
            if (TryReadBool(root, "enemy_health_bars", out bool ehb)) result.EnemyHealthBars = ehb;
            if (TryReadBool(root, "boss_health_bar", out bool bhb)) result.BossHealthBar = bhb;
        }
        catch
        {
            // Missing or unreadable files fall back to defaults and never block a run.
        }

        return result;
    }

    /// <summary>
    /// Merges the shared gameplay/HUD settings into <paramref name="path"/> while preserving
    /// launcher-only fields such as <c>instant_travel</c>.
    /// </summary>
    public static bool Save(string? path, SharedSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path) || settings == null) return false;
        try
        {
            JObject root;
            if (File.Exists(path))
            {
                try { root = JObject.Parse(File.ReadAllText(path)); }
                catch { root = new JObject(); }
            }
            else
            {
                root = new JObject();
            }

            root["fast_endings"] = FastEndingsPolicy.ToKey(settings.FastEndings);
            root["bulk_relic_turn_in"] = settings.BulkRelicTurnIn;
            root["auto_quill"] = settings.AutoQuill;
            root["show_map_logic"] = settings.ShowMapLogic;
            root["show_recent_items"] = settings.ShowRecentItems;
            root["damage_numbers"] = settings.DamageNumbers;
            root["enemy_health_bars"] = settings.EnemyHealthBars;
            root["boss_health_bar"] = settings.BossHealthBar;

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".new";
            File.WriteAllText(temp, root.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadBool(JObject root, string key, out bool value)
    {
        if (root.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out JToken? token) && token.Type == JTokenType.Boolean)
        {
            value = token.Value<bool>();
            return true;
        }
        value = false;
        return false;
    }
}

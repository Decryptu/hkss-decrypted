using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace HKSS.Unity;

internal enum Shortcut
{
    TownWarp,
    ApPanel,
    Benchwarp,
    LastBench,
    RecentItems,
    MapLogic,
}

internal enum DeathRecovery
{
    RoomDefault,
    Vanilla,
    NoNewMarker,
    PreserveExisting,
}

internal enum CheckPinFilter
{
    GameDefault,
    Unchecked,
    Reachable,
    Blocked,
    Checked,
}

internal static class PlayerOptions
{
    private static readonly Dictionary<Shortcut, ConfigEntry<Key>> Bindings = new();
    internal static ConfigEntry<bool> AutoQuill { get; private set; } = null!;
    internal static ConfigEntry<bool> ShowMapLogic { get; private set; } = null!;
    internal static ConfigEntry<bool> ShowRecentItems { get; private set; } = null!;
    internal static ConfigEntry<bool> BulkRelicTurnIn { get; private set; } = null!;
    internal static ConfigEntry<bool> BenchRecovery { get; private set; } = null!;
    internal static ConfigEntry<DeathRecovery> DeathLink { get; private set; } = null!;
    internal static ConfigEntry<CheckPinFilter> MapFilter { get; private set; } = null!;
    internal static ConfigEntry<bool> DamageNumbers { get; private set; } = null!;
    internal static ConfigEntry<bool> EnemyHealthBars { get; private set; } = null!;
    internal static ConfigEntry<bool> BossHealthBar { get; private set; } = null!;
    /// <summary>This game's skin: a folder name under <c>skins/&lt;game&gt;</c>, empty for the game's own look.</summary>
    internal static ConfigEntry<string> Skin { get; private set; } = null!;
    internal static ConfigEntry<bool> RandomSkinAtBench { get; private set; } = null!;
    internal static Shortcut? Capturing { get; set; }
    internal static bool CapturedThisFrame => _capturedFrame == UnityEngine.Time.frameCount;
    private static int _capturedFrame = -1;

    internal static void Load(string runDir)
    {
        var config = new ConfigFile(Path.Combine(runDir, "player-options.cfg"), false);
        AutoQuill = config.Bind("Map", "Auto quill", false);
        ShowMapLogic = config.Bind("Map", "Show map logic", true);
        MapFilter = config.Bind("Map", "Check pin filter", CheckPinFilter.GameDefault);
        ShowRecentItems = config.Bind("HUD", "Show recent items", false);
        BulkRelicTurnIn = config.Bind("QoL", "Bulk relic turn-in", true);
        DeathLink = config.Bind("DeathLink", "Shade or cocoon", DeathRecovery.RoomDefault);
        BenchRecovery = config.Bind("Recovery", "Allow bench recovery", false);
        DamageNumbers = config.Bind("Combat", "Damage numbers", false);
        EnemyHealthBars = config.Bind("Combat", "Enemy health bars", false);
        BossHealthBar = config.Bind("Combat", "Boss health bar", false);
        Skin = config.Bind("Skins", HkssPlugin.ThisGame == HKSS.Core.GameId.HollowKnight ? "Hollow Knight skin" : "Silksong skin", string.Empty);
        RandomSkinAtBench = config.Bind("Skins", "Random skin at each bench", false);
        Bindings.Add(Shortcut.TownWarp, config.Bind("Hotkeys", "Town Warp", Key.F4));
        Bindings.Add(Shortcut.ApPanel, config.Bind("Hotkeys", "Archipelago Panel", Key.F6));
        Bindings.Add(Shortcut.Benchwarp, config.Bind("Hotkeys", "Benchwarp Panel", Key.F7));
        Bindings.Add(Shortcut.LastBench, config.Bind("Hotkeys", "Last Bench", Key.F8));
        Bindings.Add(Shortcut.RecentItems, config.Bind("Hotkeys", "Recent Items", Key.F9));
        Bindings.Add(Shortcut.MapLogic, config.Bind("Hotkeys", "Map Logic", Key.F10));
    }

    internal static string GetLabel(Shortcut shortcut) => Bindings[shortcut].Value.ToString();

    internal static bool Pressed(Shortcut shortcut)
    {
        Keyboard? keyboard = Keyboard.current;
        Key key = Bindings[shortcut].Value;
        return Capturing == null && _capturedFrame != UnityEngine.Time.frameCount
            && keyboard != null && key > Key.None && (int)key <= keyboard.allKeys.Count
            && keyboard[key].wasPressedThisFrame;
    }

    internal static string CaptureKey()
    {
        if (Capturing is not Shortcut shortcut || Keyboard.current is not Keyboard keyboard)
            return string.Empty;

        foreach (var control in keyboard.allKeys)
        {
            if (!control.wasPressedThisFrame) continue;
            _capturedFrame = UnityEngine.Time.frameCount;
            if (control.keyCode == Key.Escape)
            {
                Capturing = null;
                return string.Empty;
            }

            foreach (var binding in Bindings)
            {
                if (binding.Key != shortcut && binding.Value.Value == control.keyCode)
                    return $"That key is assigned to {binding.Value.Definition.Key}.";
            }

            if (control.keyCode is Key.F11 or Key.F12)
                return "That key is reserved for map controls.";

            Bindings[shortcut].Value = control.keyCode;
            Capturing = null;
            return string.Empty;
        }

        return string.Empty;
    }

    internal static IEnumerable<Shortcut> Shortcuts => Bindings.Keys;
    internal static string Name(Shortcut shortcut) => Bindings[shortcut].Definition.Key;
}

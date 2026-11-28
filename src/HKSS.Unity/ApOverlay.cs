using System;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Shared Archipelago panel (<c>F6</c>) and toast feed compiled into both games.
/// </summary>
internal sealed partial class ApOverlay : MonoBehaviour
{
    private enum OverlayTab
    {
        Connection,
        Benchwarp,
        LinksAndQol,
        Skins,
        RunStats,
        Inventory,
        Hints,
    }

    internal static bool IsPanelOpen { get; private set; }
    private static ApOverlay? _instance;
    private static bool _showRunStatsRequested;

    /// <summary>Closes the Archipelago panel if it is currently open.</summary>
    internal static void ClosePanel() => _instance?.SetPanelOpen(false);

    /// <summary>Opens the panel on the Run Stats tab (used when the combo goal is completed).</summary>
    internal static void ShowRunStats() => _showRunStatsRequested = true;

#if DEBUG
    private static bool _showSkinsRequested;

    /// <summary>Development only: opens the panel on the Skins tab.</summary>
    internal static void ShowSkins() => _showSkinsRequested = true;
#endif

    private bool _panelOpen;
    private OverlayTab _activeTab = OverlayTab.Connection;
    private OverlayTab _requestedTab = OverlayTab.Connection;
    private string _addressInput = $"{ApSettings.DefaultHost}:{ApSettings.DefaultPort}";
    private string _slotInput = string.Empty;
    private string _passwordInput = string.Empty;
    private bool _autoConnectInput = true;
    private bool _inputsInitialized;
    private Vector2 _qolScroll;
    private string _hotkeyFeedback = string.Empty;
    private int _selectedGiftRecipientIdx;

    private GUIStyle? _badgeStyle;
    private GUIStyle? _panelStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _sectionStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _toastStyle;
    private Texture2D? _darkBg;
    private Texture2D? _toastBg;

    private string _cachedBadgeText = string.Empty;
    private bool _cachedBadgeDefeated;
    private ApConnectionState _cachedBadgeState = (ApConnectionState)(-1);
    private string _cachedBadgeSlot = string.Empty;
    private string _cachedBadgeKey = string.Empty;

    private void Awake() => _instance = this;

    private void Update()
    {
        if (DoubleSteelSoul.IsOverlayVisible)
        {
            if (_panelOpen) SetPanelOpen(false);
            return;
        }

        string captureError = PlayerOptions.CaptureKey();
        if (captureError.Length > 0) _hotkeyFeedback = captureError;
        if (PlayerOptions.Capturing == null) _hotkeyFeedback = string.Empty;

        if (PlayerOptions.Pressed(Shortcut.ApPanel))
        {
            SetPanelOpen(!_panelOpen);
        }
        else if (_panelOpen && PlayerOptions.Capturing == null && !PlayerOptions.CapturedThisFrame
            && UnityEngine.InputSystem.Keyboard.current is { } kb && kb.escapeKey.wasPressedThisFrame)
        {
            SetPanelOpen(false);
        }

        if (_showRunStatsRequested)
        {
            _showRunStatsRequested = false;
            _requestedTab = OverlayTab.RunStats;
            SetPanelOpen(true);
        }

#if DEBUG
        if (_showSkinsRequested)
        {
            _showSkinsRequested = false;
            _requestedTab = OverlayTab.Skins;
            SetPanelOpen(true);
        }
#endif

        if (_panelOpen)
        {
            if (_activeTab != _requestedTab)
            {
                PlayerOptions.Capturing = null;
                _nextProgressRefresh = 0;
            }
            _activeTab = _requestedTab;
            UpdateProgress();
        }
    }

    private void SetPanelOpen(bool open)
    {
        if (_panelOpen == open) return;
        _panelOpen = open;
        if (!open) PlayerOptions.Capturing = null;
        IsPanelOpen = open;
        OverlayCursor.SetOpen(OverlayCursor.Panel.Archipelago, open);
        if (open) SyncInputsFromSettings();
    }

    private void SyncInputsFromSettings()
    {
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;
        _addressInput = ap.Settings.FormatAddress();
        _slotInput = ap.Settings.Slot;
        _passwordInput = ap.Settings.Password;
        _autoConnectInput = ap.Settings.AutoConnect;
        _inputsInitialized = true;
    }

    private void OnDestroy()
    {
        SetPanelOpen(false);
        if (_instance == this) _instance = null;
        if (_darkBg != null) Destroy(_darkBg);
        if (_toastBg != null) Destroy(_toastBg);
        DestroyRunStatsTextures();
    }

    private void EnsureStyles()
    {
        if (_darkBg == null)
        {
            _darkBg = UiTheme.MakeSolidTexture(new Color(0.06f, 0.07f, 0.11f, 0.92f));
            _toastBg = UiTheme.MakeSolidTexture(new Color(0.05f, 0.06f, 0.09f, 0.84f));
        }

        _badgeStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(10, 10, 4, 4),
            normal = { background = _toastBg, textColor = Color.white },
        };

        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(14, 14, 12, 12),
            normal = { background = _darkBg, textColor = Color.white },
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = AccentColor() },
        };

        _sectionStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.95f, 0.85f, 0.45f, 1f) },
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = new Color(0.90f, 0.92f, 0.96f, 1f) },
        };

        _toastStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            padding = new RectOffset(12, 12, 6, 6),
            normal = { background = _toastBg, textColor = Color.white },
        };
    }

    private void OnGUI()
    {
        if (DoubleSteelSoul.IsOverlayVisible) return;
        ApManager? ap = ApManager.Instance;
        if (ap == null) return;

        EnsureStyles();
        DrawStatusBadge(ap);
        DrawToasts(ap);
        if (_panelOpen)
        {
            if (!_inputsInitialized) SyncInputsFromSettings();
            DrawMainPanel(ap);
        }
    }

    private void DrawStatusBadge(ApManager ap)
    {
        bool defeated = DoubleSteelSoul.IsDefeated;
        ApConnectionState state = ap.Client.State;
        string slot = ap.Client.ConnectedSlot;
        string keyLabel = PlayerOptions.GetLabel(Shortcut.ApPanel);
        if (_cachedBadgeText.Length == 0
            || _cachedBadgeDefeated != defeated
            || _cachedBadgeState != state
            || !string.Equals(_cachedBadgeSlot, slot, StringComparison.Ordinal)
            || !string.Equals(_cachedBadgeKey, keyLabel, StringComparison.Ordinal))
        {
            _cachedBadgeDefeated = defeated;
            _cachedBadgeState = state;
            _cachedBadgeSlot = slot;
            _cachedBadgeKey = keyLabel;
            _cachedBadgeText = defeated
                ? $"STEEL SOUL DEFEATED [{keyLabel}]"
                : state switch
                {
                    ApConnectionState.Connected => $"AP: {slot} [{keyLabel}]",
                    ApConnectionState.Connecting => $"AP: Connecting... [{keyLabel}]",
                    ApConnectionState.Failed => $"AP: Error [{keyLabel}]",
                    _ => $"AP: Offline [{keyLabel}]",
                };
        }

        Color color = defeated
            ? new Color(1.0f, 0.35f, 0.35f, 0.98f)
            : state switch
            {
                ApConnectionState.Connected => new Color(0.55f, 0.95f, 0.65f, 0.95f),
                ApConnectionState.Connecting => new Color(0.95f, 0.85f, 0.45f, 0.95f),
                ApConnectionState.Failed => new Color(1.0f, 0.45f, 0.45f, 0.95f),
                _ => new Color(0.75f, 0.78f, 0.85f, 0.85f),
            };

        Color prev = GUI.contentColor;
        GUI.contentColor = color;
        float w = 220f;
        float h = 28f;
        var rect = new Rect(Screen.width - w - 14f, 14f, w, h);
        if (GUI.Button(rect, _cachedBadgeText, _badgeStyle))
        {
            SetPanelOpen(!_panelOpen);
        }
        GUI.contentColor = prev;
    }

    private void DrawToasts(ApManager ap)
    {
        var toasts = ap.Toasts;
        if (toasts.Count == 0) return;

        float width = 390f;
        float y = Screen.height - 24f;
        float now = Time.unscaledTime;

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            ToastEntry toast = toasts[i];
            float age = now - toast.CreatedAt;
            float alpha = age > ApManager.ToastDurationSeconds - 1f
                ? Mathf.Clamp01(ApManager.ToastDurationSeconds - age)
                : 1f;

            var content = new GUIContent(toast.Text);
            float height = Mathf.Max(30f, _toastStyle!.CalcHeight(content, width));
            y -= height + 6f;

            Color prevColor = GUI.color;
            Color prevContent = GUI.contentColor;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.contentColor = ToastColor(toast.Kind);
            GUI.Box(new Rect(18f, y, width, height), content, _toastStyle);
            GUI.contentColor = prevContent;
            GUI.color = prevColor;
        }
    }

    private void DrawMainPanel(ApManager ap)
    {
        float w = 390f;
        float h = Mathf.Min(500f, Screen.height - 70f);
        var rect = new Rect(Screen.width - w - 14f, 48f, w, h);

        GUILayout.BeginArea(rect, _panelStyle);

        GUILayout.Label($"Hollow Knight x Silksong — {ItemRouter.WorldName(HkssPlugin.ThisGame)}", _titleStyle);
        GUILayout.Space(4f);

        // Four tabs to a row, so every tab stays inside the panel.
        for (int i = 0; i < Tabs.Length; i += TabsPerRow)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < Math.Min(i + TabsPerRow, Tabs.Length); j++)
            {
                if (DrawTabButton(Tabs[j].Label, _activeTab == Tabs[j].Tab)) _requestedTab = Tabs[j].Tab;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8f);

        switch (_activeTab)
        {
            case OverlayTab.Connection:
                DrawConnectionTab(ap);
                break;
            case OverlayTab.Benchwarp:
                Benchwarp.Instance?.DrawBenchwarpContents(showHeader: false);
                break;
            case OverlayTab.LinksAndQol:
                DrawLinksAndQolTab();
                break;
            case OverlayTab.Skins:
                Skins.Instance?.DrawTab(_sectionStyle!, _labelStyle!, AccentColor());
                break;
            case OverlayTab.RunStats:
                DrawRunStatsTab();
                break;
            case OverlayTab.Inventory:
                DrawInventoryTab(ap.Client);
                break;
            case OverlayTab.Hints:
                DrawHintsTab(ap.Client);
                break;
        }

        GUILayout.EndArea();
    }

    private const int TabsPerRow = 4;

    private static readonly (string Label, OverlayTab Tab)[] Tabs =
    {
        ("Connection", OverlayTab.Connection),
        ("Benchwarp", OverlayTab.Benchwarp),
        ("Links & QoL", OverlayTab.LinksAndQol),
        ("Skins", OverlayTab.Skins),
        ("Stats", OverlayTab.RunStats),
        ("Inventory", OverlayTab.Inventory),
        ("Hints", OverlayTab.Hints),
    };

    private static bool DrawTabButton(string label, bool active)
    {
        Color prev = GUI.backgroundColor;
        if (active) GUI.backgroundColor = AccentColor();
        bool clicked = GUILayout.Button(label, GUILayout.Height(26f));
        GUI.backgroundColor = prev;
        return clicked;
    }

    private void DrawConnectionTab(ApManager ap)
    {
        GUILayout.Label("Server (host:port)", _labelStyle);
        _addressInput = GUILayout.TextField(_addressInput ?? string.Empty);

        GUILayout.Label("Slot name", _labelStyle);
        _slotInput = GUILayout.TextField(_slotInput ?? string.Empty);

        GUILayout.Label("Password (optional)", _labelStyle);
        _passwordInput = GUILayout.PasswordField(_passwordInput ?? string.Empty, '*');

        _autoConnectInput = GUILayout.Toggle(_autoConnectInput, "Auto-connect on launch");
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        bool busy = ap.Client.State == ApConnectionState.Connecting;
        GUI.enabled = !busy;
        if (GUILayout.Button(ap.Client.IsConnected ? "Reconnect" : "Connect", GUILayout.Height(28f)))
        {
            if (ApSettings.TryParseAddress(_addressInput, out string host, out int port))
            {
                ap.SaveAndConnect(new ApSettings
                {
                    Host = host,
                    Port = port,
                    Slot = (_slotInput ?? string.Empty).Trim(),
                    Password = _passwordInput ?? string.Empty,
                    AutoConnect = _autoConnectInput,
                });
            }
            else
            {
                ap.PushToast("Invalid server address (use host:port).", ToastKind.Error);
            }
        }
        GUI.enabled = ap.Client.IsConnected || busy;
        if (GUILayout.Button("Disconnect", GUILayout.Height(28f)))
        {
            ap.Disconnect();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label($"Status: {ap.Client.StatusDetail}", _labelStyle);
        if (DoubleSteelSoul.IsDefeated)
        {
            string where = string.IsNullOrWhiteSpace(DoubleSteelSoul.DefeatedScene)
                ? DoubleSteelSoul.DefeatedWorldName
                : $"{DoubleSteelSoul.DefeatedWorldName} ({DoubleSteelSoul.DefeatedScene})";
            GUILayout.Label($"Mode: Double Steel Soul — Defeated in {where}", _sectionStyle);
        }
        else if (DoubleSteelSoul.IsEnabled)
        {
            GUILayout.Label("Mode: Double Steel Soul (Active)", _sectionStyle);
        }

        CrossGameSeals seals = SealGuard.Current;
        if (seals.All.Count > 0)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Cross-game seals", _labelStyle);
            foreach (CrossGameSeal seal in seals.All)
            {
                string state = SealGuard.HasKeyForDisplay(seal.KeyId)
                    ? "open"
                    : $"needs {seal.KeyDisplayName} from {seal.KeyWorld}";
                GUILayout.Label($"• {seal.Area} ({ItemRouter.WorldName(seal.Game)}): {state}", _labelStyle);
            }
        }

        var logs = ap.RecentLogs;
        if (logs.Count > 0)
        {
            GUILayout.Space(4f);
            int start = Math.Max(0, logs.Count - 5);
            for (int i = start; i < logs.Count; i++)
                GUILayout.Label("• " + logs[i], _labelStyle);
        }
    }

    private void DrawLinksAndQolTab()
    {
        _qolScroll = GUILayout.BeginScrollView(_qolScroll);

#if HOLLOW_KNIGHT
        GUILayout.Label("Archipelago Links & Gifting", _sectionStyle);
        bool soulLink = GUILayout.Toggle(HKSS.HollowKnight.HkLinksManager.SoulLinkEnabled, "SoulLink (share base 99 Soul)");
        if (soulLink != HKSS.HollowKnight.HkLinksManager.SoulLinkEnabled)
            HKSS.HollowKnight.HkLinksManager.SoulLinkEnabled = soulLink;

        bool geoLink = GUILayout.Toggle(HKSS.HollowKnight.HkLinksManager.GeoLinkEnabled, "GeoLink (share Geo with team)");
        if (geoLink != HKSS.HollowKnight.HkLinksManager.GeoLinkEnabled)
            HKSS.HollowKnight.HkLinksManager.GeoLinkEnabled = geoLink;

        bool kbLink = GUILayout.Toggle(HKSS.HollowKnight.HkLinksManager.KnockbackLinkEnabled, "KnockbackLink (cross-game recoil)");
        if (kbLink != HKSS.HollowKnight.HkLinksManager.KnockbackLinkEnabled)
            HKSS.HollowKnight.HkLinksManager.KnockbackLinkEnabled = kbLink;

        bool gifting = GUILayout.Toggle(HKSS.HollowKnight.HkGiftingModule.IsEnabled, "Gifting (open AP GiftBox)");
        if (gifting != HKSS.HollowKnight.HkGiftingModule.IsEnabled)
            HKSS.HollowKnight.HkGiftingModule.IsEnabled = gifting;

        HKSS.HollowKnight.HkGiftingModule? giftMod = HKSS.HollowKnight.HkGiftingModule.Instance;
        if (gifting && giftMod != null)
        {
            var recipients = giftMod.OpenRecipients;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh GiftBoxes", GUILayout.Height(22f)))
                giftMod.RefreshOpenRecipientsAsync();
            GUILayout.EndHorizontal();

            if (recipients.Count > 0)
            {
                if (_selectedGiftRecipientIdx >= recipients.Count) _selectedGiftRecipientIdx = 0;
                var target = recipients[_selectedGiftRecipientIdx];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"To: {target.Name}", _labelStyle);
                if (recipients.Count > 1 && GUILayout.Button("Next", GUILayout.Width(50f)))
                    _selectedGiftRecipientIdx = (_selectedGiftRecipientIdx + 1) % recipients.Count;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Gift 100 Geo", GUILayout.Height(24f)))
                    giftMod.TrySendGeoGift(target.Slot, 100);
                if (GUILayout.Button("Gift Rancid Egg", GUILayout.Height(24f)))
                    giftMod.TrySendRancidEggGift(target.Slot);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("No other open GiftBoxes found yet.", _labelStyle);
            }
        }

        GUILayout.Space(8f);
        GUILayout.Label("Quality of Life", _sectionStyle);

        bool skipCutscenes = GUILayout.Toggle(HKSS.HollowKnight.HkQolPatches.SkipCutscenes, "Instant cutscene skip");
        if (skipCutscenes != HKSS.HollowKnight.HkQolPatches.SkipCutscenes)
            HKSS.HollowKnight.HkQolPatches.SkipCutscenes = skipCutscenes;

        bool skipBossIntros = GUILayout.Toggle(HKSS.HollowKnight.HkQolPatches.SkipBossIntros, "Skip boss roar locks");
        if (skipBossIntros != HKSS.HollowKnight.HkQolPatches.SkipBossIntros)
            HKSS.HollowKnight.HkQolPatches.SkipBossIntros = skipBossIntros;

        GUILayout.Space(4f);
        bool seedGeoMultiplier = HKSS.HollowKnight.HkQolPatches.SeedEnemyGeoMultiplier > 0f;
        GUILayout.Label(
            $"Enemy Geo Multiplier: {HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier:0.#}x" + (seedGeoMultiplier ? " (set by the seed)" : ""),
            _labelStyle);
        if (!seedGeoMultiplier)
        {
            GUILayout.BeginHorizontal();
            foreach (float mult in HKSS.HollowKnight.HkQolPatches.GeoMultiplierSteps)
            {
                bool selected = Mathf.Approximately(HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier, mult);
                Color prev = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = UiTheme.AccentColor();
                if (GUILayout.Button($"{mult:0.#}x", GUILayout.Height(24f)))
                {
                    HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier = mult;
                }
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();
        }
#elif SILKSONG
        GUILayout.Label("Quality of Life", _sectionStyle);
        GUILayout.Label("Silksong link & currency options are configured in your Archipelago YAML / save settings.", _labelStyle);
#endif

        PlayerOptions.BulkRelicTurnIn.Value = GUILayout.Toggle(
            PlayerOptions.BulkRelicTurnIn.Value,
            "Bulk relic turn-in (Lemm, Jinn, Scrounge & Cardinius)");
        if (GUILayout.Button($"Endings & credits · {FastEndingsPolicy.Label(FastEndings.Mode)}"))
            FastEndings.SetModeAndSave(FastEndingsPolicy.Next(FastEndings.Mode));

        GUILayout.Space(8f);
        GUILayout.Label("Map & HUD", _sectionStyle);
        PlayerOptions.AutoQuill.Value = GUILayout.Toggle(PlayerOptions.AutoQuill.Value,
            "Auto-quill for visited rooms with an owned map");
        bool showRecent = GUILayout.Toggle(PlayerOptions.ShowRecentItems.Value,
            $"Show recent items feed [{PlayerOptions.GetLabel(Shortcut.RecentItems)}]");
        if (showRecent != PlayerOptions.ShowRecentItems.Value)
            PlayerOptions.ShowRecentItems.Value = showRecent;
        bool showLogic = GUILayout.Toggle(PlayerOptions.ShowMapLogic.Value,
            $"Show map logic [{PlayerOptions.GetLabel(Shortcut.MapLogic)}]");
        if (showLogic != PlayerOptions.ShowMapLogic.Value)
        {
            PlayerOptions.ShowMapLogic.Value = showLogic;
#if HOLLOW_KNIGHT
            HKSS.HollowKnight.HkCheckMap.MarkDirty();
            HKSS.HollowKnight.HkCheckMap.RefreshIfOpen();
#elif SILKSONG
            SilksongRandomizer.Patches.CheckMapMarkerManager.RequestCurrentMarkerStateRefresh();
#endif
        }
        var filter = PlayerOptions.MapFilter.Value;
        if (GUILayout.Button($"Check pins · {filter switch
        {
            CheckPinFilter.GameDefault => "Game setting",
            CheckPinFilter.Blocked => "Out of logic",
            _ => filter.ToString(),
        }}"))
        {
            PlayerOptions.MapFilter.Value = (CheckPinFilter)(((int)filter + 1) % 5);
#if HOLLOW_KNIGHT
            HKSS.HollowKnight.HkCheckMap.MarkDirty();
            HKSS.HollowKnight.HkCheckMap.RefreshIfOpen();
#elif SILKSONG
            SilksongRandomizer.RandomizerPlugin.Instance?.QueueMapMarkerSettingsRefresh();
#endif
        }

        GUILayout.Space(8f);
        GUILayout.Label("Combat", _sectionStyle);
        PlayerOptions.DamageNumbers.Value = GUILayout.Toggle(PlayerOptions.DamageNumbers.Value,
            "Damage numbers on hits");
        PlayerOptions.EnemyHealthBars.Value = GUILayout.Toggle(PlayerOptions.EnemyHealthBars.Value,
            "Health bars over enemies you hit");
        PlayerOptions.BossHealthBar.Value = GUILayout.Toggle(PlayerOptions.BossHealthBar.Value,
            "Boss health bar");

        GUILayout.Space(8f);
        GUILayout.Label("Death & Recovery", _sectionStyle);
        var deathMode = PlayerOptions.DeathLink.Value;
        if (GUILayout.Button($"DeathLink shade / cocoon · {deathMode switch
        {
            DeathRecovery.RoomDefault => "Room setting",
            DeathRecovery.NoNewMarker => "Never create a new marker",
            DeathRecovery.PreserveExisting => "Preserve an existing marker",
            _ => "Vanilla",
        }}"))
            PlayerOptions.DeathLink.Value = (DeathRecovery)(((int)deathMode + 1) % 4);

        PlayerOptions.BenchRecovery.Value = GUILayout.Toggle(PlayerOptions.BenchRecovery.Value,
            "Allow bench recovery if player input is stuck");
        if (PlayerOptions.BenchRecovery.Value && GUILayout.Button("Recover to last discovered bench"))
        {
            if (Benchwarp.Instance?.TryRecoverToBench() == true) SetPanelOpen(false);
        }

        GUILayout.Space(8f);
        GUILayout.Label("Hotkeys", _sectionStyle);
        foreach (Shortcut shortcut in PlayerOptions.Shortcuts)
        {
            string key = PlayerOptions.Capturing == shortcut ? "Press a key (Esc cancels)" : PlayerOptions.GetLabel(shortcut);
            if (GUILayout.Button($"{PlayerOptions.Name(shortcut)} · {key}"))
            {
                PlayerOptions.Capturing = shortcut;
                _hotkeyFeedback = string.Empty;
            }
        }
        if (_hotkeyFeedback.Length > 0) GUILayout.Label(_hotkeyFeedback, _labelStyle);

        GUILayout.EndScrollView();
    }

    private static Color AccentColor() => UiTheme.AccentColor();

    private static Color ToastColor(ToastKind kind) => kind switch
    {
        ToastKind.LocalItem => UiTheme.AccentColor(),
        ToastKind.CrossGameItem => UiTheme.CrossGameAccentColor(),
        ToastKind.CheckSent => new Color(0.65f, 0.95f, 0.72f, 1f),
        ToastKind.Error => new Color(1.0f, 0.42f, 0.42f, 1f),
        _ => new Color(0.92f, 0.94f, 0.98f, 1f),
    };
}

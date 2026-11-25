using System;
using HKSS.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HKSS.Unity;

/// <summary>
/// Shared Archipelago panel (<c>F6</c>) with Connection, Benchwarp, and Links/QoL tabs
/// plus toast feed compiled into both games.
/// </summary>
internal sealed class ApOverlay : MonoBehaviour
{
    private enum OverlayTab
    {
        Connection,
        Benchwarp,
        LinksAndQol,
    }

    internal static bool IsPanelOpen { get; private set; }

    private bool _panelOpen;
    private OverlayTab _activeTab = OverlayTab.Connection;
    private bool _savedCursorVisible;
    private CursorLockMode _savedCursorLockState = CursorLockMode.None;
    private bool _hasSavedCursorState;
    private string _addressInput = $"{ApSettings.DefaultHost}:{ApSettings.DefaultPort}";
    private string _slotInput = string.Empty;
    private string _passwordInput = string.Empty;
    private bool _autoConnectInput = true;
    private bool _inputsInitialized;
    private Vector2 _qolScroll;
    private int _selectedGiftRecipientIdx;

    private GUIStyle? _badgeStyle;
    private GUIStyle? _panelStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _sectionStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _toastStyle;
    private Texture2D? _darkBg;
    private Texture2D? _toastBg;

    private void Update()
    {
        Keyboard? kb = Keyboard.current;
        if (kb != null && kb.f6Key.wasPressedThisFrame)
        {
            SetPanelOpen(!_panelOpen);
        }

        if (_panelOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void SetPanelOpen(bool open)
    {
        if (_panelOpen == open) return;
        _panelOpen = open;
        IsPanelOpen = open;
        if (open)
        {
            _savedCursorVisible = Cursor.visible;
            _savedCursorLockState = Cursor.lockState;
            _hasSavedCursorState = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            SyncInputsFromSettings();
        }
        else if (_hasSavedCursorState)
        {
            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLockState;
            _hasSavedCursorState = false;
        }
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
        if (_darkBg != null) Destroy(_darkBg);
        if (_toastBg != null) Destroy(_toastBg);
    }

    private void EnsureStyles()
    {
        if (_darkBg == null)
        {
            _darkBg = MakeSolidTexture(new Color(0.06f, 0.07f, 0.11f, 0.92f));
            _toastBg = MakeSolidTexture(new Color(0.05f, 0.06f, 0.09f, 0.84f));
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
        ApConnectionState state = ap.Client.State;
        string text = state switch
        {
            ApConnectionState.Connected => $"AP: {ap.Client.ConnectedSlot} [F6]",
            ApConnectionState.Connecting => "AP: Connecting... [F6]",
            ApConnectionState.Failed => "AP: Error [F6]",
            _ => "AP: Offline [F6]",
        };

        Color color = state switch
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
        if (GUI.Button(rect, text, _badgeStyle))
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

        GUILayout.BeginHorizontal();
        if (DrawTabButton("Connection", _activeTab == OverlayTab.Connection)) _activeTab = OverlayTab.Connection;
        if (DrawTabButton("Benchwarp", _activeTab == OverlayTab.Benchwarp)) _activeTab = OverlayTab.Benchwarp;
        if (DrawTabButton("Links & QoL", _activeTab == OverlayTab.LinksAndQol)) _activeTab = OverlayTab.LinksAndQol;
        GUILayout.EndHorizontal();

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
        }

        GUILayout.EndArea();
    }

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

        bool bulkSell = GUILayout.Toggle(HKSS.HollowKnight.HkQolPatches.BulkSellRelicsAndEggs, "Bulk sell relics (Lemm) & eggs (Jinn)");
        if (bulkSell != HKSS.HollowKnight.HkQolPatches.BulkSellRelicsAndEggs)
            HKSS.HollowKnight.HkQolPatches.BulkSellRelicsAndEggs = bulkSell;

        GUILayout.Space(4f);
        GUILayout.Label($"Enemy Geo Multiplier: {HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier:0.#}x", _labelStyle);
        GUILayout.BeginHorizontal();
        foreach (float mult in HKSS.HollowKnight.HkQolPatches.GeoMultiplierSteps)
        {
            bool selected = Mathf.Approximately(HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier, mult);
            Color prev = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = AccentColor();
            if (GUILayout.Button($"{mult:0.#}x", GUILayout.Height(24f)))
            {
                HKSS.HollowKnight.HkQolPatches.EnemyGeoMultiplier = mult;
            }
            GUI.backgroundColor = prev;
        }
        GUILayout.EndHorizontal();
#elif SILKSONG
        GUILayout.Label("Quality of Life", _sectionStyle);
        SilksongRandomizer.Patches.ScroungeRelicTurnInPatches.BulkScroungeTurnIn =
            GUILayout.Toggle(
                SilksongRandomizer.Patches.ScroungeRelicTurnInPatches.BulkScroungeTurnIn,
                "Instant bulk relic turn-in (Scrounge & Cardinius)");
        GUILayout.Label("Silksong link & currency options are configured in your Archipelago YAML / save settings.", _labelStyle);
#endif

        GUILayout.Space(8f);
        GUILayout.Label("Hotkeys", _sectionStyle);
        GUILayout.Label("• F4 — Warp to Town Gate\n• F6 — Archipelago / Settings Panel\n• F7 — Benchwarp Panel\n• F8 — Warp to Last Bench\n• F9 — Recent Items Feed\n• F10 — Map Logic Overlay", _labelStyle);

        GUILayout.EndScrollView();
    }

    private static Color AccentColor()
    {
#if HOLLOW_KNIGHT
        return new Color(0.55f, 0.80f, 1.0f, 1f);
#elif SILKSONG
        return new Color(1.0f, 0.45f, 0.42f, 1f);
#endif
    }

    private static Color ToastColor(ToastKind kind) => kind switch
    {
        ToastKind.LocalItem => AccentColor(),
        ToastKind.CrossGameItem =>
#if HOLLOW_KNIGHT
            new Color(1.0f, 0.48f, 0.44f, 1f),
#elif SILKSONG
            new Color(0.55f, 0.82f, 1.0f, 1f),
#endif
        ToastKind.CheckSent => new Color(0.65f, 0.95f, 0.72f, 1f),
        ToastKind.Error => new Color(1.0f, 0.42f, 0.42f, 1f),
        _ => new Color(0.92f, 0.94f, 0.98f, 1f),
    };

    private static Texture2D MakeSolidTexture(Color color)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}

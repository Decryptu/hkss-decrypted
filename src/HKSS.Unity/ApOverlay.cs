using System;
using HKSS.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HKSS.Unity;

/// <summary>
/// Shared Archipelago connection panel (<c>F6</c>) and toast feed compiled into both games.
/// </summary>
internal sealed class ApOverlay : MonoBehaviour
{
    internal static bool IsPanelOpen { get; private set; }

    private bool _panelOpen;
    private bool _savedCursorVisible;
    private CursorLockMode _savedCursorLockState = CursorLockMode.None;
    private bool _hasSavedCursorState;
    private string _addressInput = $"{ApSettings.DefaultHost}:{ApSettings.DefaultPort}";
    private string _slotInput = string.Empty;
    private string _passwordInput = string.Empty;
    private bool _autoConnectInput = true;
    private bool _inputsInitialized;

    private GUIStyle? _badgeStyle;
    private GUIStyle? _panelStyle;
    private GUIStyle? _titleStyle;
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
            _darkBg = MakeSolidTexture(new Color(0.06f, 0.07f, 0.11f, 0.90f));
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
            DrawConnectionPanel(ap);
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

    private void DrawConnectionPanel(ApManager ap)
    {
        float w = 360f;
        float h = 340f;
        var rect = new Rect(Screen.width - w - 14f, 48f, w, h);

        GUILayout.BeginArea(rect, _panelStyle);

        GUILayout.Label($"Hollow Knight x Silksong — {ItemRouter.WorldName(HkssPlugin.ThisGame)}", _titleStyle);
        GUILayout.Space(6f);

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
            int start = Math.Max(0, logs.Count - 4);
            for (int i = start; i < logs.Count; i++)
                GUILayout.Label("• " + logs[i], _labelStyle);
        }

        GUILayout.EndArea();
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

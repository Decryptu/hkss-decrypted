using System;
using System.Collections.Generic;
using HKSS.Core;
using HKSS.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HKSS.HollowKnight;

/// <summary>
/// In-game overlay for the Hollow Knight check map:
/// - Hotkeys: F10 (toggle live logic), F11 (cycle check filter), Shift+F11 (cycle visibility), F12 (toggle category icons)
/// - Hover tooltips (works with both controller center-crosshair panning and mouse hover)
/// - Compact bottom-left filter &amp; status bar while the World Map is open
/// </summary>
internal sealed class HkCheckMapOverlay : MonoBehaviour
{
    private const int MaxChecksInTooltip = 12;

    private readonly struct TooltipLine
    {
        public readonly string Text;
        public readonly Color Color;
        public readonly bool Bold;

        public TooltipLine(string text, Color color, bool bold = false)
        {
            Text = text;
            Color = color;
            Bold = bold;
        }
    }

    private bool _usingMousePointer;
    private Vector2 _lastMousePos;
    private float _lastMouseMoveTime;

    private Texture2D? _bgTex;
    private Texture2D? _btnTex;
    private Texture2D? _btnHoverTex;
    private GUIStyle? _panelStyle;
    private GUIStyle? _labelStyle;
    private GUIStyle? _boldLabelStyle;
    private GUIStyle? _buttonStyle;

    private void OnDestroy()
    {
        if (_bgTex != null) Destroy(_bgTex);
        if (_btnTex != null) Destroy(_btnTex);
        if (_btnHoverTex != null) Destroy(_btnHoverTex);
    }

    private void Update()
    {
        Keyboard? kb = Keyboard.current;
        if (kb != null && !ApOverlay.IsPanelOpen && !Benchwarp.IsPanelOpen)
        {
            if (PlayerOptions.Pressed(Shortcut.MapLogic))
            {
                bool next = !HkCheckMap.ShowLogic;
                HkCheckMap.ShowLogic = next;
                ApManager.Instance?.PushToast(
                    $"Map Logic: {(next ? "ON" : "OFF")} [{PlayerOptions.GetLabel(Shortcut.MapLogic)}]",
                    ToastKind.Info);
            }

            if (kb.f11Key.wasPressedThisFrame)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (shift)
                {
                    CycleVisibilityMode();
                }
                else
                {
                    CycleCheckFilter();
                }
            }

            if (kb.f12Key.wasPressedThisFrame)
            {
                bool next = !HkCheckMap.UseCategoryIcons;
                HkCheckMap.UseCategoryIcons = next;
                ApManager.Instance?.PushToast(
                    next ? "Map Icons: Category [F12]" : "Map Icons: Generic [F12]",
                    ToastKind.Info);
            }
        }

        if (HkCheckMap.IsWorldMapOpen)
        {
            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                if ((pos - _lastMousePos).sqrMagnitude > 9f)
                {
                    _lastMousePos = pos;
                    _lastMouseMoveTime = Time.unscaledTime;
                    _usingMousePointer = true;
                }
            }

            // Switch back to center crosshair if mouse has been idle and map is panned via controller/keyboard
            if (_usingMousePointer && Time.unscaledTime - _lastMouseMoveTime > 2.5f)
            {
                _usingMousePointer = false;
            }
        }
        else
        {
            _usingMousePointer = false;
        }
    }

    private static void CycleCheckFilter()
    {
        int next = ((int)HkCheckMap.CheckFilter + 1) % 5;
        PlayerOptions.MapFilter.Value = CheckPinFilter.GameDefault;
        HkCheckMap.CheckFilter = (HkMapCheckFilter)next;
        ApManager.Instance?.PushToast($"Map Filter: {HkCheckMap.CheckFilter} [F11]", ToastKind.Info);
    }

    private static void CycleVisibilityMode()
    {
        int next = ((int)HkCheckMap.VisibilityMode + 1) % 4;
        HkCheckMap.VisibilityMode = (HkMapVisibilityMode)next;
        ApManager.Instance?.PushToast($"Map Visibility: {HkCheckMap.VisibilityMode} [Shift+F11]", ToastKind.Info);
    }

    private void EnsureStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = UiTheme.MakeSolidTexture(new Color(0.05f, 0.06f, 0.10f, 0.88f));
        }
        if (_btnTex == null)
        {
            _btnTex = UiTheme.MakeSolidTexture(new Color(0.12f, 0.15f, 0.22f, 0.92f));
        }
        if (_btnHoverTex == null)
        {
            _btnHoverTex = UiTheme.MakeSolidTexture(new Color(0.20f, 0.26f, 0.38f, 0.96f));
        }

        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 7, 7),
            normal = { background = _bgTex, textColor = Color.white },
        };

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = false,
            normal = { textColor = Color.white },
        };

        _boldLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            normal = { textColor = new Color(0.75f, 0.88f, 1.0f, 1f) },
        };

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            padding = new RectOffset(8, 8, 4, 4),
            normal = { background = _btnTex, textColor = Color.white },
            hover = { background = _btnHoverTex, textColor = Color.white },
            active = { background = _btnHoverTex, textColor = new Color(0.75f, 0.92f, 1f, 1f) },
        };
    }

    private void OnGUI()
    {
        if (!HkCheckMap.IsWorldMapOpen) return;
        if (ApOverlay.IsPanelOpen) return;

        Camera? hudCam = GameCameras.instance?.hudCamera;
        if (hudCam == null || !hudCam.isActiveAndEnabled) return;

        EnsureStyles();
        DrawFilterBar();

        Vector2 pointer = _usingMousePointer
            ? _lastMousePos
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (!_usingMousePointer)
        {
            DrawCrosshair(pointer);
        }

        HkMapPinRecord? hovered = HkCheckMap.FindHoveredPin(hudCam, pointer, 26f);
        HkCheckMap.SetHoveredPin(hovered);

        if (hovered != null && hovered.Checks.Count > 0)
        {
            DrawTooltip(hovered, pointer);
        }
    }

    private void DrawFilterBar()
    {
        const float barHeight = 32f;
        float y = Screen.height - barHeight - 14f;
        float x = 14f;

        string summary = HkCheckMap.ShowLogic
            ? $"Checks: {HkCheckMap.TotalReachable} reachable / {HkCheckMap.TotalUnchecked} left"
            : $"Checks: {HkCheckMap.TotalUnchecked} left";

        GUILayout.BeginArea(new Rect(x, y, Math.Min(Screen.width - 28f, 760f), barHeight));
        GUILayout.BeginHorizontal(_panelStyle);

        GUILayout.Label(summary, _boldLabelStyle, GUILayout.ExpandWidth(false));
        GUILayout.Space(8f);

        string logicLabel = $"[{PlayerOptions.GetLabel(Shortcut.MapLogic)}] Logic: {(HkCheckMap.ShowLogic ? "ON" : "OFF")}";
        if (GUILayout.Button(logicLabel, _buttonStyle, GUILayout.ExpandWidth(false)))
        {
            HkCheckMap.ShowLogic = !HkCheckMap.ShowLogic;
        }

        if (GUILayout.Button($"[F11] Filter: {HkCheckMap.CheckFilter}", _buttonStyle, GUILayout.ExpandWidth(false)))
        {
            CycleCheckFilter();
        }

        if (GUILayout.Button($"[Shift+F11] Map: {HkCheckMap.VisibilityMode}", _buttonStyle, GUILayout.ExpandWidth(false)))
        {
            CycleVisibilityMode();
        }

        string iconsLabel = HkCheckMap.UseCategoryIcons ? "[F12] Icons: Category" : "[F12] Icons: Simple";
        if (GUILayout.Button(iconsLabel, _buttonStyle, GUILayout.ExpandWidth(false)))
        {
            HkCheckMap.UseCategoryIcons = !HkCheckMap.UseCategoryIcons;
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawTooltip(HkMapPinRecord pin, Vector2 screenPointer)
    {
        var lines = new List<TooltipLine>();
        bool showLogic = HkCheckMap.ShowLogic;
        HkLogicState? logic = HkCheckMap.LastLogicState;

        lines.Add(new TooltipLine($"{pin.Title} ({pin.Checks.Count})", new Color(0.75f, 0.88f, 1.0f, 1f), bold: true));

        int shown = 0;
        foreach (HkCheckEntry check in pin.Checks)
        {
            if (shown >= MaxChecksInTooltip)
            {
                int remaining = pin.Checks.Count - shown;
                lines.Add(new TooltipLine($"... and {remaining} more", new Color(0.72f, 0.72f, 0.78f, 1f)));
                break;
            }
            shown++;

            string nameText = string.IsNullOrEmpty(check.CostSummary)
                ? check.DisplayName
                : $"{check.DisplayName} [{check.CostSummary}]";

            Color nameColor = check.IsChecked
                ? new Color(0.55f, 0.88f, 0.62f, 1f)
                : (showLogic && check.Reachability == HkReachability.Unreachable
                    ? new Color(0.62f, 0.62f, 0.68f, 1f)
                    : Color.white);

            lines.Add(new TooltipLine(nameText, nameColor));

            if (check.IsChecked)
            {
                lines.Add(new TooltipLine("  Checked", new Color(0.55f, 0.88f, 0.62f, 1f)));
            }
            else if (showLogic)
            {
                switch (check.Reachability)
                {
                    case HkReachability.Reachable:
                        lines.Add(new TooltipLine("  Reachable", new Color(0.42f, 0.94f, 0.68f, 1f)));
                        break;
                    case HkReachability.Unreachable:
                        lines.Add(new TooltipLine("  Blocked", new Color(0.96f, 0.45f, 0.42f, 1f)));
                        if (logic != null)
                        {
                            foreach (string reason in logic.ExplainBlocked(check.RawLocationName))
                            {
                                string clipped = reason.Length > 72 ? reason.Substring(0, 72) + "..." : reason;
                                lines.Add(new TooltipLine($"    {clipped}", new Color(0.95f, 0.78f, 0.48f, 1f)));
                            }
                        }
                        break;
                    default:
                        lines.Add(new TooltipLine("  Logic unknown", new Color(0.72f, 0.72f, 0.78f, 1f)));
                        break;
                }
            }

            if (!string.IsNullOrEmpty(check.HintText))
            {
                lines.Add(new TooltipLine($"  {check.HintText}", new Color(1.0f, 0.88f, 0.45f, 1f)));
            }
        }

        float maxWidth = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            GUIStyle st = lines[i].Bold ? _boldLabelStyle! : _labelStyle!;
            float w = st.CalcSize(new GUIContent(lines[i].Text)).x;
            if (w > maxWidth) maxWidth = w;
        }

        const float lineHeight = 18f;
        float boxW = maxWidth + 22f;
        float boxH = lines.Count * lineHeight + 14f;

        float guiX = screenPointer.x + 18f;
        float guiY = (Screen.height - screenPointer.y) + 18f;
        if (guiX + boxW > Screen.width - 8f) guiX = screenPointer.x - boxW - 18f;
        if (guiY + boxH > Screen.height - 54f) guiY = Screen.height - 54f - boxH;
        guiX = Mathf.Clamp(guiX, 8f, Math.Max(8f, Screen.width - boxW - 8f));
        guiY = Mathf.Clamp(guiY, 8f, Math.Max(8f, Screen.height - boxH - 8f));

        var rect = new Rect(guiX, guiY, boxW, boxH);
        GUI.Box(rect, GUIContent.none, _panelStyle);

        for (int i = 0; i < lines.Count; i++)
        {
            GUIStyle st = lines[i].Bold ? _boldLabelStyle! : _labelStyle!;
            Color prev = st.normal.textColor;
            st.normal.textColor = lines[i].Color;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 7f + i * lineHeight, maxWidth + 4f, lineHeight), lines[i].Text, st);
            st.normal.textColor = prev;
        }
    }

    private static void DrawCrosshair(Vector2 screenPointer)
    {
        float x = Mathf.Round(screenPointer.x);
        float y = Mathf.Round(Screen.height - screenPointer.y);
        Color prev = GUI.color;
        try
        {
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(x - 11f, y - 2f, 8f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 3f, y - 2f, 8f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 2f, y - 11f, 4f, 8f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 2f, y + 3f, 4f, 8f), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x - 10f, y - 1f, 6f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 4f, y - 1f, 6f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y - 10f, 2f, 6f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y + 4f, 2f, 6f), Texture2D.whiteTexture);
        }
        finally
        {
            GUI.color = prev;
        }
    }
}

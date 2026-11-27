using System;
using System.Collections.Generic;
using HKSS.Unity;
using ItemChanger;
using UnityEngine;

namespace HKSS.HollowKnight;

/// <summary>
/// Recent items history overlay (<c>F9</c>) for Hollow Knight, matching Silksong's recent items feed.
/// Shows the latest received and found items in the top-right corner during active gameplay.
/// </summary>
internal sealed class HkRecentItemsDisplay : MonoBehaviour
{
    private const int MaxDisplayedItems = 5;

    private readonly struct RecentEntry
    {
        public readonly string Text;
        public readonly ToastKind Kind;

        public RecentEntry(string text, ToastKind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    private readonly List<RecentEntry> _entries = new();

    private GUIStyle? _panelStyle;
    private GUIStyle? _headerStyle;
    private GUIStyle? _itemStyle;
    private Texture2D? _bgTexture;

    private void Awake()
    {
        ApManager.OnItemNotice += OnApItemNotice;
        AbstractItem.AfterGiveGlobal += OnItemGiven;
    }

    private void OnDestroy()
    {
        ApManager.OnItemNotice -= OnApItemNotice;
        AbstractItem.AfterGiveGlobal -= OnItemGiven;
        if (_bgTexture != null) Destroy(_bgTexture);
    }

    private void Update()
    {
        if (!ApOverlay.IsPanelOpen && !Benchwarp.IsPanelOpen && PlayerOptions.Pressed(Shortcut.RecentItems))
        {
            bool next = !PlayerOptions.ShowRecentItems.Value;
            PlayerOptions.ShowRecentItems.Value = next;
            ApManager.Instance?.PushToast(
                $"Recent Items feed {(next ? "shown" : "hidden")} [{PlayerOptions.GetLabel(Shortcut.RecentItems)}]",
                ToastKind.Info);
        }
    }

    private void OnApItemNotice(string text, ToastKind kind)
    {
        AddEntry(text, kind);
    }

    private void OnItemGiven(ReadOnlyGiveEventArgs args)
    {
        if (args.Item == null) return;
        if (args.Placement is HkRemotePlacement) return;
        if (args.MessageType == MessageType.None) return;

        string name = args.Item.UIDef?.GetPostviewName() ?? args.Item.name;
        if (string.IsNullOrWhiteSpace(name)) return;

        ToastKind kind = ToastKind.LocalItem;
        if (args.Item is HkRemoteItem && name.Contains("(Pharloom)", StringComparison.Ordinal))
        {
            kind = ToastKind.CrossGameItem;
        }
        else if (args.Item is HkRemoteItem || args.Item is HkDummyItem)
        {
            kind = ToastKind.CheckSent;
        }

        AddEntry(name.Replace("_", " "), kind);
    }

    private void AddEntry(string text, ToastKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _entries.Insert(0, new RecentEntry(text.Trim(), kind));
        while (_entries.Count > MaxDisplayedItems)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }
    }

    private void EnsureStyles()
    {
        if (_bgTexture == null)
        {
            _bgTexture = UiTheme.MakeSolidTexture(new Color(0.05f, 0.06f, 0.10f, 0.78f));
        }

        _panelStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 8),
            normal = { background = _bgTexture, textColor = Color.white },
        };

        _headerStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.70f, 0.85f, 1.0f, 0.95f) },
        };

        _itemStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white },
        };
    }

    private void OnGUI()
    {
        if (!PlayerOptions.ShowRecentItems.Value || _entries.Count == 0 || ApOverlay.IsPanelOpen) return;

        GameManager? gm = GameManager.UnsafeInstance;
        if (gm == null || !gm.IsGameplayScene() || gm.IsInSceneTransition || gm.isPaused
            || gm.gameState != GlobalEnums.GameState.PLAYING) return;
        if (PlayerData.instance == null) return;
        if (gm.inventoryFSM != null && !string.Equals(gm.inventoryFSM.ActiveStateName, "Closed", StringComparison.Ordinal))
            return;

        EnsureStyles();

        float width = 300f;
        float contentHeight = 26f;
        for (int i = 0; i < _entries.Count; i++)
        {
            var content = new GUIContent("• " + _entries[i].Text);
            contentHeight += Mathf.Max(20f, _itemStyle!.CalcHeight(content, width - 20f)) + 3f;
        }
        float height = contentHeight + 14f;
        var rect = new Rect(Screen.width - width - 14f, 48f, width, height);

        GUILayout.BeginArea(rect, _panelStyle);
        GUILayout.Label($"Recent Items [{PlayerOptions.GetLabel(Shortcut.RecentItems)}]", _headerStyle);

        Color prev = GUI.contentColor;
        for (int i = 0; i < _entries.Count; i++)
        {
            GUI.contentColor = EntryColor(_entries[i].Kind);
            GUILayout.Label("• " + _entries[i].Text, _itemStyle);
        }
        GUI.contentColor = prev;
        GUILayout.EndArea();
    }

    private static Color EntryColor(ToastKind kind) => kind switch
    {
        ToastKind.LocalItem => new Color(0.65f, 0.86f, 1.0f, 1f),
        ToastKind.CrossGameItem => new Color(1.0f, 0.52f, 0.48f, 1f),
        ToastKind.CheckSent => new Color(0.68f, 0.95f, 0.74f, 1f),
        _ => new Color(0.92f, 0.94f, 0.98f, 1f),
    };
}

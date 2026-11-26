using System;
using HarmonyLib;
using UnityEngine;

namespace HKSS.Unity;

internal static class OverlayCursor
{
    [Flags]
    internal enum Panel
    {
        Archipelago = 1,
        Benchwarp = 2,
        SteelSoul = 4,
    }

    private static Panel _openPanels;
    private static bool _savedVisible;
    private static CursorLockMode _savedLockState;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(InputHandler), "SetCursorEnabled", new[] { typeof(bool) }),
            prefix: new HarmonyMethod(typeof(OverlayCursor), nameof(BeforeGameCursorUpdate)));
    }

    internal static void SetOpen(Panel panel, bool open)
    {
        Panel previous = _openPanels;
        _openPanels = open ? _openPanels | panel : _openPanels & ~panel;
        if (previous == _openPanels) return;

        if (previous == 0)
        {
            _savedVisible = Cursor.visible;
            _savedLockState = Cursor.lockState;
        }

        if (_openPanels != 0)
        {
            ShowCursor();
        }
        else
        {
            Cursor.lockState = _savedLockState;
            Cursor.visible = _savedVisible;
        }
    }

    private static bool BeforeGameCursorUpdate()
    {
        // Hollow Knight updates this in OnGUI and Silksong updates it in Update.
        if (_openPanels == 0) return true;
        ShowCursor();
        return false;
    }

    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Shared colour tokens and 1×1 solid texture helper used by in-game overlays in both games.
/// </summary>
internal static class UiTheme
{
    internal static readonly Color HkColor = new(0.55f, 0.78f, 1.0f, 1f);
    internal static readonly Color SsColor = new(1.0f, 0.45f, 0.42f, 1f);
    internal static readonly Color GoldColor = new(0.95f, 0.85f, 0.45f, 1f);

    internal static Color AccentColor()
    {
#if HOLLOW_KNIGHT
        return HkColor;
#elif SILKSONG
        return SsColor;
#endif
    }

    internal static Color CrossGameAccentColor()
    {
#if HOLLOW_KNIGHT
        return SsColor;
#elif SILKSONG
        return HkColor;
#endif
    }

    internal static Texture2D MakeSolidTexture(Color color)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}

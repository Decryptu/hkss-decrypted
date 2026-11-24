using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Modding
{
    /// <summary>
    /// Minimal UI canvas helper matching the subset of Modding.CanvasUtil used by ItemChanger.
    /// </summary>
    public static class CanvasUtil
    {
        private static readonly Dictionary<string, Font> Fonts = new();
        private static Font? _trajanBold;
        private static Font? _trajanNormal;
        private static Sprite? _nullSprite;

        public static Font? TrajanBold
        {
            get
            {
                if (_trajanBold == null) LoadFonts();
                return _trajanBold;
            }
        }

        public static Font? TrajanNormal
        {
            get
            {
                if (_trajanNormal == null) LoadFonts();
                return _trajanNormal;
            }
        }

        private static void LoadFonts()
        {
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null) continue;
                if (f.name == "TrajanPro-Bold") _trajanBold = f;
                else if (f.name == "TrajanPro-Regular") _trajanNormal = f;
            }
        }

        public static Font? GetFont(string fontName)
        {
            if (Fonts.TryGetValue(fontName, out Font? cached) && cached != null) return cached;
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f != null && f.name == fontName)
                {
                    Fonts[fontName] = f;
                    return f;
                }
            }
            return null;
        }

        public static Sprite NullSprite(byte[]? data = null)
        {
            if (data == null && _nullSprite != null) return _nullSprite;
            var tex = new Texture2D(1, 1);
            data ??= new byte[] { 0, 0, 0, 0 };
            tex.LoadRawTextureData(data);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            if (_nullSprite == null) _nullSprite = sprite;
            return sprite;
        }

        public class RectData
        {
            public Vector2 RectSizeDelta;
            public Vector2 AnchorPosition;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 AnchorPivot;

            public RectData(Vector2 sizeDelta, Vector2 anchorPosition)
                : this(sizeDelta, anchorPosition, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)) { }

            public RectData(Vector2 sizeDelta, Vector2 anchorPosition, Vector2 min, Vector2 max)
                : this(sizeDelta, anchorPosition, min, max, new Vector2(0.5f, 0.5f)) { }

            public RectData(Vector2 sizeDelta, Vector2 anchorPosition, Vector2 min, Vector2 max, Vector2 pivot)
            {
                RectSizeDelta = sizeDelta;
                AnchorPosition = anchorPosition;
                AnchorMin = min;
                AnchorMax = max;
                AnchorPivot = pivot;
            }
        }

        public static GameObject CreateCanvas(RenderMode renderMode, Vector2 referencePixelsPerUnit)
        {
            var go = CreateCanvas(renderMode);
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referencePixelsPerUnit;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return go;
        }

        public static GameObject CreateCanvas(RenderMode renderMode, int sortingOrder = 0)
        {
            var go = new GameObject("Canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = renderMode;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        public static GameObject CreateImagePanel(GameObject parent, Sprite? img, RectData rect)
        {
            var panel = new GameObject("CanvasImage");
            panel.AddComponent<CanvasRenderer>();
            var rt = panel.AddComponent<RectTransform>();
            rt.sizeDelta = rect.RectSizeDelta;
            var image = panel.AddComponent<Image>();
            image.sprite = img;
            image.preserveAspect = true;
            panel.transform.SetParent(parent.transform, false);
            rt.anchorMin = rect.AnchorMin;
            rt.anchorMax = rect.AnchorMax;
            rt.pivot = rect.AnchorPivot;
            rt.anchoredPosition = rect.AnchorPosition;
            return panel;
        }

        public static GameObject CreateTextPanel(
            GameObject parent,
            string text,
            int fontSize,
            TextAnchor textAnchor,
            RectData rect,
            Font? font = null)
        {
            var panel = new GameObject("CanvasText");
            panel.AddComponent<CanvasRenderer>();
            var rt = panel.AddComponent<RectTransform>();
            rt.sizeDelta = rect.RectSizeDelta;
            var textComp = panel.AddComponent<Text>();
            textComp.font = font ?? TrajanBold ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComp.text = text;
            textComp.supportRichText = true;
            textComp.fontSize = fontSize;
            textComp.alignment = textAnchor;
            panel.transform.SetParent(parent.transform, false);
            rt.anchorMin = rect.AnchorMin;
            rt.anchorMax = rect.AnchorMax;
            rt.pivot = rect.AnchorPivot;
            rt.anchoredPosition = rect.AnchorPosition;
            return panel;
        }
    }
}

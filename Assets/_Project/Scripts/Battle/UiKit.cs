using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieSLG.Battle
{
    /// <summary>Shared helpers for the code-built uGUI screens (font, sprites, layout shortcuts).</summary>
    public static class UiKit
    {
        private static Font _font;
        private static Sprite _circle;
        private static Sprite _rounded;

        public static readonly Color Panel = new Color(0.05f, 0.06f, 0.08f, 0.86f);
        public static readonly Color PanelLight = new Color(0.12f, 0.13f, 0.16f, 0.95f);
        public static readonly Color Accent = new Color(1f, 0.55f, 0.15f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.38f, 0.32f);
        public static readonly Color Muted = new Color(0.62f, 0.66f, 0.72f);

        /// <summary>OS font with Hangul (Malgun Gothic on Windows), falling back to Unity's built-in.</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                try
                {
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial" }, 40);
                }
                catch (Exception)
                {
                    // fall through
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeRounded(128, 64));

        /// <summary>9-sliced rounded rectangle (radius 24px).</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(96, 24, true));

        private static Sprite MakeRounded(int size, int radius, bool sliced = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01(radius - d) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Image(string name, Transform parent, Color color, bool rounded = false, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return img;
        }

        public static Text Text(string name, Transform parent, string value, int size, Color color, TextAnchor anchor, bool bold = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, int fontSize, Color color, Action onClick, out Text labelText)
        {
            var img = Image(name, parent, color, rounded: true, raycast: true);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());
            labelText = Text("Label", img.rectTransform, label, fontSize, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(labelText.rectTransform);
            return btn;
        }

        public static void Stretch(RectTransform rt) => Anchor(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>Fixed-size box anchored to a point of its parent.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static string Duration(double seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            if (s >= 3600) return $"{s / 3600}:{(s / 60) % 60:00}:{s % 60:00}";
            return $"{s / 60}:{s % 60:00}";
        }

        public static string LongDuration(double seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
            if (s >= 86400) return $"{s / 86400}일 {(s / 3600) % 24}시간";
            if (s >= 3600) return $"{s / 3600}시간 {(s / 60) % 60}분";
            if (s >= 60) return $"{s / 60}분";
            return $"{s}초";
        }
    }
}

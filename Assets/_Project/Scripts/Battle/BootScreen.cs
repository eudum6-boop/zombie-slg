using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Shown from the first frame until the world is built, so connecting never looks like a frozen black
    /// screen. If startup throws, the error stays on screen (a phone has no Console).
    /// </summary>
    public sealed class BootScreen
    {
        private readonly Transform _parent;
        private Canvas _canvas;
        private Text _status;
        private Text _detail;

        public BootScreen(Transform parent)
        {
            _parent = parent;
            Build();
        }

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("BootUI", _parent, 100);
            var root = (RectTransform)_canvas.transform;
            var bg = UiKit.Image("Background", root, new Color(0.05f, 0.06f, 0.08f, 1f), raycast: true);
            UiKit.Stretch(bg.rectTransform);

            var title = UiKit.Text("Title", root, "좀비 생존", 84, UiKit.Accent, TextAnchor.MiddleCenter, true);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(1000f, 120f));

            _status = UiKit.Text("Status", root, "", 38, Color.white, TextAnchor.MiddleCenter);
            UiKit.Place(_status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1000f, 80f));

            _detail = UiKit.Text("Detail", root, "", 26, new Color(1f, 0.55f, 0.45f), TextAnchor.UpperLeft);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.verticalOverflow = VerticalWrapMode.Truncate;
            UiKit.Place(_detail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -420f), new Vector2(980f, 700f));
        }

        public void SetStatus(string text)
        {
            if (_canvas == null) return;
            _status.text = text;
            Debug.Log("[ZombieSLG][시작] " + text);
        }

        public void ShowError(string message, Exception e = null)
        {
            if (_canvas == null) Build();
            _canvas.transform.SetAsLastSibling();
            _status.text = "<color=#ff8a70>시작 오류</color>\n" + message;
            if (e != null) _detail.text = e.GetType().Name + ": " + e.Message + "\n\n" + e.StackTrace;
        }

        public void Close()
        {
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Coach panel + bouncing arrow for the first-session tutorial. Sits on its own canvas above both HUDs.
    /// Only the panel catches taps, so the player can still tap the thing the arrow points at.
    /// </summary>
    public sealed class TutorialUi
    {
        /// <summary>Where the arrow should point this frame.</summary>
        public readonly struct Target
        {
            public readonly RectTransform Ui;
            public readonly Vector3? World;

            private Target(RectTransform ui, Vector3? world)
            {
                Ui = ui;
                World = world;
            }

            public static Target None => new Target(null, null);
            public static Target At(RectTransform ui) => new Target(ui, null);
            public static Target At(Vector3 world) => new Target(null, world);
        }

        private const int TotalSteps = (int)TutorialStep.Done;

        private readonly Canvas _canvas;
        private readonly RectTransform _root;
        private readonly GameObject _panel;
        private readonly Text _counter;
        private readonly Text _body;
        private readonly GameObject _nextButton;
        private readonly Text _nextLabel;
        private readonly RectTransform _arrow;
        private readonly Text _arrowText;
        private float _time;

        public TutorialUi(Transform parent, Action onNext, Action onSkip)
        {
            _canvas = UiKit.CreateCanvas("TutorialUI", parent, 30);
            _root = (RectTransform)_canvas.transform;

            var panel = UiKit.Image("Coach", _root, new Color(0.06f, 0.08f, 0.12f, 0.92f), rounded: true, raycast: true);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(1000f, 250f));
            _panel = panel.gameObject;
            var p = panel.rectTransform;

            var accent = UiKit.Image("Accent", p, UiKit.Accent, rounded: true);
            UiKit.Anchor(accent.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(12f, 0f));

            _counter = UiKit.Text("Counter", p, "", 26, UiKit.Accent, TextAnchor.UpperLeft, true);
            UiKit.Anchor(_counter.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -56f), new Vector2(-200f, -16f));

            _body = UiKit.Text("Body", p, "", 32, Color.white, TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_body.rectTransform, Vector2.zero, Vector2.one, new Vector2(36f, 80f), new Vector2(-30f, -58f));

            var skip = UiKit.Button("Skip", p, "건너뛰기", 26, new Color(0.22f, 0.24f, 0.28f), onSkip, out _);
            UiKit.Place((RectTransform)skip.transform, Vector2.one, Vector2.one, new Vector2(-18f, -14f), new Vector2(170f, 56f));

            var next = UiKit.Button("Next", p, "다음", 32, new Color(0.75f, 0.32f, 0.08f), onNext, out _nextLabel);
            UiKit.Place((RectTransform)next.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 16f), new Vector2(220f, 64f));
            _nextButton = next.gameObject;

            _arrowText = UiKit.Text("Arrow", _root, "▼", 96, UiKit.Accent, TextAnchor.LowerCenter, true);
            _arrowText.raycastTarget = false;
            var outline = _arrowText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);
            _arrow = _arrowText.rectTransform;
            _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
            _arrow.pivot = new Vector2(0.5f, 0f);
            _arrow.sizeDelta = new Vector2(160f, 120f);

            Hide();
        }

        public void Hide()
        {
            _panel.SetActive(false);
            _arrow.gameObject.SetActive(false);
        }

        /// <summary>Call every frame while a tutorial step should be on screen.</summary>
        public void Show(TutorialStep step, string text, string nextLabel, Target target, Camera cam, float udt)
        {
            _time += udt;
            _panel.SetActive(true);
            _counter.text = $"튜토리얼  {(int)step + 1} / {TotalSteps}";
            _body.text = text;
            _nextButton.SetActive(nextLabel != null);
            if (nextLabel != null) _nextLabel.text = nextLabel;
            PlaceArrow(target, cam);
        }

        private void PlaceArrow(Target target, Camera cam)
        {
            Vector2 screen;
            if (target.Ui != null)
            {
                if (!target.Ui.gameObject.activeInHierarchy) { _arrow.gameObject.SetActive(false); return; }
                // Top-centre of the target, in screen pixels (overlay canvases: no camera).
                var corners = new Vector3[4];
                target.Ui.GetWorldCorners(corners);
                screen = RectTransformUtility.WorldToScreenPoint(null, (corners[1] + corners[2]) * 0.5f);
            }
            else if (target.World.HasValue && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(target.World.Value);
                if (sp.z < 0f) { _arrow.gameObject.SetActive(false); return; }
                screen = sp;
            }
            else
            {
                _arrow.gameObject.SetActive(false);
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local))
            {
                _arrow.gameObject.SetActive(false);
                return;
            }
            _arrow.gameObject.SetActive(true);
            float bounce = Mathf.Abs(Mathf.Sin(_time * 5f)) * 28f;
            _arrow.anchoredPosition = local + new Vector2(0f, 6f + bounce);
            float pulse = 0.8f + 0.2f * Mathf.Sin(_time * 10f);
            var c = UiKit.Accent;
            _arrowText.color = new Color(c.r, c.g, c.b, pulse);
        }
    }
}

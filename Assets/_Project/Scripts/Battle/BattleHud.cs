using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Battle HUD built in code (no prefab dependencies for M0). Portrait-first: 1080x1920 reference.
    /// M4 replaces this with designed UI using the MVP pattern from the architecture doc.
    /// </summary>
    public sealed class BattleHud
    {
        private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.08f, 0.82f);
        private static readonly Color Accent = new Color(1f, 0.55f, 0.15f);
        private static readonly Color HqColor = new Color(0.9f, 0.25f, 0.2f);

        private readonly BattleContext _ctx;
        private readonly Font _font;
        private readonly Sprite _circle;
        private readonly RectTransform _canvasRect;

        private Text _nightText;
        private Text _timerText;
        private Text _killText;
        private Image _hqFill;
        private Text _hqText;
        private Text _debugText;
        private Text _bannerText;
        private float _bannerTimer;
        private RectTransform _heroBar;
        private Image _heroFill;
        private Text _heroRespawnText;
        private Image _skillCooldown;
        private Text _skillLabel;
        private Image _skillBg;
        private Text _hintText;
        private GameObject _startPanel;
        private Text _startTitle;
        private Text _startBody;
        private GameObject _resultPanel;
        private Text _resultTitle;
        private Text _resultBody;
        private Text _resultButtonLabel;
        private RectTransform _damageLayer;

        private float _fps = 60f;
        private float _frameMs = 16.7f;

        // ---- damage numbers ----
        private sealed class DamageNumber
        {
            public Text Text;
            public RectTransform Rect;
            public Vector3 World;
            public float Age;
            public float Life;
            public float Scale;
            public Color Color;
        }

        private sealed class SquadBar
        {
            public RectTransform Rect;
            public Image Fill;
            public Text Label;
        }

        private readonly List<SquadBar> _squadBars = new List<SquadBar>();
        private readonly List<GameObject> _battleOnly = new List<GameObject>();
        private readonly List<DamageNumber> _numbers = new List<DamageNumber>();
        private int _numberCursor;

        public BattleHud(BattleContext ctx, Transform parent, Action onStart, Action onSkill, Action onStress, Action onResultButton)
        {
            _ctx = ctx;
            _font = UiKit.Font;
            _circle = UiKit.Circle;

            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)canvasGo.transform;

            _damageLayer = NewRect("DamageNumbers", _canvasRect);
            Stretch(_damageLayer);
            for (int i = 0; i < 80; i++) _numbers.Add(CreateNumber());

            BuildTopBar();
            BuildHeroBar();
            BuildSquadBars();
            BuildBottom(onSkill, onStress);
            BuildBanner();
            BuildStartPanel(onStart);
            BuildResultPanel(onResultButton);
            _startPanel.SetActive(false);
            SetBattleVisible(false);
        }

        /// <summary>Battle-only widgets (timer bar, skill, hero bar…) are hidden during the day.</summary>
        public void SetBattleVisible(bool visible)
        {
            foreach (var go in _battleOnly) go.SetActive(visible);
            if (!visible)
            {
                foreach (var n in _numbers) n.Text.gameObject.SetActive(false);
                _bannerText.gameObject.SetActive(false);
                _bannerTimer = 0f;
            }
        }

        // ------------------------------------------------------------------ build

        private void BuildTopBar()
        {
            var bar = NewImage("TopBar", _canvasRect, PanelColor);
            Anchor(bar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -230f), Vector2.zero);
            bar.raycastTarget = false;

            _nightText = NewText("Night", bar.rectTransform, "NIGHT 1", 44, Accent, TextAnchor.MiddleLeft);
            Anchor(_nightText.rectTransform, new Vector2(0f, 1f), new Vector2(0.33f, 1f), new Vector2(40f, -110f), new Vector2(0f, -20f));

            _timerText = NewText("Timer", bar.rectTransform, "3:00", 76, Color.white, TextAnchor.MiddleCenter);
            _timerText.fontStyle = FontStyle.Bold;
            Anchor(_timerText.rectTransform, new Vector2(0.33f, 1f), new Vector2(0.67f, 1f), new Vector2(0f, -120f), new Vector2(0f, -10f));

            _killText = NewText("Kills", bar.rectTransform, "처치 0", 40, Color.white, TextAnchor.MiddleRight);
            Anchor(_killText.rectTransform, new Vector2(0.67f, 1f), new Vector2(1f, 1f), new Vector2(0f, -110f), new Vector2(-40f, -20f));

            var hqBg = NewImage("HqBg", bar.rectTransform, new Color(0.2f, 0.05f, 0.05f, 1f));
            hqBg.raycastTarget = false;
            Anchor(hqBg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 30f), new Vector2(-40f, 90f));
            _hqFill = NewImage("HqFill", hqBg.rectTransform, HqColor);
            _hqFill.raycastTarget = false;
            _hqFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            Anchor(_hqFill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _hqText = NewText("HqText", hqBg.rectTransform, "본부", 34, Color.white, TextAnchor.MiddleCenter);
            Stretch(_hqText.rectTransform);

            _debugText = NewText("Debug", _canvasRect, "", 28, new Color(0.7f, 1f, 0.7f, 0.9f), TextAnchor.UpperRight);
            Anchor(_debugText.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(0f, -330f), new Vector2(-24f, -240f));
            _battleOnly.Add(bar.gameObject);
            _battleOnly.Add(_debugText.gameObject);
        }

        private void BuildHeroBar()
        {
            var bg = NewImage("HeroBar", _canvasRect, new Color(0f, 0f, 0f, 0.7f));
            bg.raycastTarget = false;
            _heroBar = bg.rectTransform;
            _battleOnly.Add(bg.gameObject);
            _heroBar.anchorMin = _heroBar.anchorMax = new Vector2(0.5f, 0.5f);
            _heroBar.sizeDelta = new Vector2(130f, 18f);
            _heroFill = NewImage("Fill", _heroBar, new Color(0.3f, 0.85f, 1f));
            _heroFill.raycastTarget = false;
            _heroFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            Anchor(_heroFill.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            _heroRespawnText = NewText("Respawn", _heroBar, "", 34, new Color(0.6f, 0.9f, 1f), TextAnchor.MiddleCenter);
            Anchor(_heroRespawnText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200f, 10f), new Vector2(200f, 60f));
        }

        private void BuildSquadBars()
        {
            foreach (var _ in _ctx.Squads)
            {
                var bg = NewImage("SquadBar", _canvasRect, new Color(0f, 0f, 0f, 0.65f));
                bg.raycastTarget = false;
                var rt = bg.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(110f, 14f);
                var fill = NewImage("Fill", rt, new Color(0.55f, 0.85f, 0.4f));
                fill.raycastTarget = false;
                fill.rectTransform.pivot = new Vector2(0f, 0.5f);
                Anchor(fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
                var label = NewText("Count", rt, "", 26, new Color(0.85f, 1f, 0.75f), TextAnchor.MiddleCenter);
                label.raycastTarget = false;
                Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, 2f), new Vector2(120f, 36f));
                _battleOnly.Add(bg.gameObject);
                _squadBars.Add(new SquadBar { Rect = rt, Fill = fill, Label = label });
            }
        }

        private void TickSquadBars(Camera cam)
        {
            for (int i = 0; i < _squadBars.Count && i < _ctx.Squads.Count; i++)
            {
                var sq = _ctx.Squads[i];
                var bar = _squadBars[i];
                bool show = sq.StartTotal > 0;
                if (bar.Rect.gameObject.activeSelf != show) bar.Rect.gameObject.SetActive(show);
                if (!show) continue;
                Vector3 screen = cam.WorldToScreenPoint(sq.Position + new Vector3(0f, 2.4f, 0f));
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local))
                    bar.Rect.anchoredPosition = local;
                bar.Fill.rectTransform.localScale = new Vector3(Mathf.Clamp01(sq.HealthRatio), 1f, 1f);
                bool hit = Time.time - sq.LastHitTime < 0.12f;
                bar.Fill.color = hit ? Color.white : (sq.IsAlive ? new Color(0.55f, 0.85f, 0.4f) : new Color(0.4f, 0.4f, 0.4f));
                bar.Label.text = sq.IsAlive ? $"{sq.Name} {sq.TotalAlive}" : $"{sq.Name} 붕괴";
            }
        }

        private void BuildBottom(Action onSkill, Action onStress)
        {
            // Skill button (bottom-right).
            _skillBg = NewImage("SkillButton", _canvasRect, new Color(0.55f, 0.18f, 0.06f, 1f));
            _skillBg.sprite = _circle;
            var rt = _skillBg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-50f, 70f);
            rt.sizeDelta = new Vector2(250f, 250f);
            var btn = _skillBg.gameObject.AddComponent<Button>();
            _battleOnly.Add(_skillBg.gameObject);
            btn.targetGraphic = _skillBg;
            btn.onClick.AddListener(() => onSkill?.Invoke());

            _skillCooldown = NewImage("Cooldown", rt, new Color(0f, 0f, 0f, 0.65f));
            _skillCooldown.sprite = _circle;
            _skillCooldown.type = Image.Type.Filled;
            _skillCooldown.fillMethod = Image.FillMethod.Radial360;
            _skillCooldown.fillOrigin = (int)Image.Origin360.Top;
            _skillCooldown.fillClockwise = false;
            _skillCooldown.raycastTarget = false;
            Stretch(_skillCooldown.rectTransform);

            _skillLabel = NewText("Label", rt, "화염\n폭격", 50, Color.white, TextAnchor.MiddleCenter);
            _skillLabel.fontStyle = FontStyle.Bold;
            Stretch(_skillLabel.rectTransform);

            var key = NewText("Key", rt, "[Q]", 26, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerCenter);
            Anchor(key.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -40f), new Vector2(0f, 0f));

            // Stress test (bottom-left, debug).
            var stress = NewImage("StressButton", _canvasRect, new Color(0.15f, 0.25f, 0.15f, 0.9f));
            var srt = stress.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0f);
            srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(40f, 70f);
            srt.sizeDelta = new Vector2(230f, 90f);
            var sbtn = stress.gameObject.AddComponent<Button>();
            _battleOnly.Add(stress.gameObject);
            sbtn.targetGraphic = stress;
            sbtn.onClick.AddListener(() => onStress?.Invoke());
            var stxt = NewText("Label", srt, "+" + _ctx.Config.StressTestCount + " 좀비 [T]", 30, new Color(0.7f, 1f, 0.7f), TextAnchor.MiddleCenter);
            Stretch(stxt.rectTransform);

            _hintText = NewText("Hint", _canvasRect, "", 32, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
            Anchor(_hintText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(290f, 330f), new Vector2(-320f, 400f));
            _battleOnly.Add(_hintText.gameObject);
        }

        private void BuildBanner()
        {
            _bannerText = NewText("Banner", _canvasRect, "", 64, Accent, TextAnchor.MiddleCenter);
            _bannerText.fontStyle = FontStyle.Bold;
            Anchor(_bannerText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 280f), new Vector2(0f, 460f));
            _bannerText.gameObject.SetActive(false);
        }

        private void BuildStartPanel(Action onStart)
        {
            var panel = NewImage("StartPanel", _canvasRect, new Color(0f, 0f, 0f, 0.72f));
            Stretch(panel.rectTransform);
            _startPanel = panel.gameObject;

            _startTitle = NewText("Title", panel.rectTransform, "NIGHT 1", 120, Accent, TextAnchor.MiddleCenter);
            _startTitle.fontStyle = FontStyle.Bold;
            Anchor(_startTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 220f), new Vector2(0f, 420f));

            _startBody = NewText("Body", panel.rectTransform, "", 40, Color.white, TextAnchor.UpperCenter);
            Anchor(_startBody.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(80f, -160f), new Vector2(-80f, 200f));

            var btn = MakeButton("StartButton", panel.rectTransform, "습격 시작  [Space]", new Color(0.75f, 0.3f, 0.08f), onStart, out _);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -300f);
            rt.sizeDelta = new Vector2(600f, 150f);
        }

        private void BuildResultPanel(Action onButton)
        {
            var panel = NewImage("ResultPanel", _canvasRect, new Color(0f, 0f, 0f, 0.78f));
            Stretch(panel.rectTransform);
            _resultPanel = panel.gameObject;

            _resultTitle = NewText("Title", panel.rectTransform, "", 110, Accent, TextAnchor.MiddleCenter);
            _resultTitle.fontStyle = FontStyle.Bold;
            Anchor(_resultTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 360f), new Vector2(0f, 560f));

            _resultBody = NewText("Body", panel.rectTransform, "", 42, Color.white, TextAnchor.UpperCenter);
            Anchor(_resultBody.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(80f, -220f), new Vector2(-80f, 330f));

            var btn = MakeButton("ResultButton", panel.rectTransform, "", new Color(0.75f, 0.3f, 0.08f), onButton, out _resultButtonLabel);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -360f);
            rt.sizeDelta = new Vector2(600f, 150f);
            _resultPanel.SetActive(false);
        }

        // ------------------------------------------------------------------ API

        public void ShowStart(int night, string body)
        {
            _startTitle.text = "NIGHT " + night;
            _startBody.text = body;
            _startPanel.SetActive(true);
            _resultPanel.SetActive(false);
        }

        public void ShowResult(bool victory, string body, string buttonLabel)
        {
            _resultTitle.text = victory ? "생존 성공" : "본부 함락";
            _resultTitle.color = victory ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.3f, 0.25f);
            _resultBody.text = body;
            _resultButtonLabel.text = buttonLabel;
            _resultPanel.SetActive(true);
            _startPanel.SetActive(false);
        }

        public void HideOverlays()
        {
            _startPanel.SetActive(false);
            _resultPanel.SetActive(false);
        }

        public void ShowBanner(string text, float seconds, Color color)
        {
            _bannerText.text = text;
            _bannerText.color = color;
            _bannerTimer = seconds;
            _bannerText.gameObject.SetActive(true);
        }

        public void SetHint(string text) => _hintText.text = text;

        public void ShowDamage(Vector3 world, float amount, bool crit, DamageStyle style)
        {
            var n = _numbers[_numberCursor];
            _numberCursor = (_numberCursor + 1) % _numbers.Count;

            int value = Mathf.Max(1, Mathf.RoundToInt(amount));
            n.Text.text = crit ? value + "!" : value.ToString();
            n.World = world + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, UnityEngine.Random.Range(-0.2f, 0.2f));
            n.Age = 0f;

            switch (style)
            {
                case DamageStyle.Skill:
                    n.Color = new Color(1f, 0.55f, 0.15f); n.Scale = crit ? 1.6f : 1.25f; n.Life = 0.9f; break;
                case DamageStyle.Burn:
                    n.Color = new Color(1f, 0.45f, 0.2f); n.Scale = 0.7f; n.Life = 0.5f; break;
                case DamageStyle.Troop:
                    n.Color = crit ? new Color(0.6f, 1f, 0.5f) : new Color(0.8f, 0.9f, 0.75f); n.Scale = crit ? 1.05f : 0.75f; n.Life = 0.5f; break;
                case DamageStyle.Tower:
                    n.Color = new Color(0.85f, 0.85f, 0.85f); n.Scale = crit ? 1.0f : 0.7f; n.Life = 0.5f; break;
                default:
                    n.Color = crit ? new Color(1f, 0.85f, 0.2f) : Color.white; n.Scale = crit ? 1.5f : 0.95f; n.Life = 0.7f; break;
            }
            n.Text.color = n.Color;
            n.Text.gameObject.SetActive(true);
        }

        public void Tick(float remainingSeconds, int night)
        {
            float udt = Time.unscaledDeltaTime;
            if (udt > 0f)
            {
                _fps = Mathf.Lerp(_fps, 1f / udt, 0.05f);
                _frameMs = Mathf.Lerp(_frameMs, udt * 1000f, 0.05f);
            }

            var zombies = _ctx.Zombies;
            var hq = _ctx.Hq;
            var hero = _ctx.Hero;
            var skill = _ctx.Skill;

            _nightText.text = "NIGHT " + night;
            int secs = Mathf.CeilToInt(remainingSeconds);
            _timerText.text = (secs / 60) + ":" + (secs % 60).ToString("00");
            _timerText.color = secs <= 10 ? new Color(1f, 0.85f, 0.4f) : Color.white;
            _killText.text = "처치 " + zombies.Kills;

            _hqFill.rectTransform.localScale = new Vector3(hq.HpRatio, 1f, 1f);
            bool recentlyHit = Time.time - hq.LastHitTime < 0.15f;
            _hqFill.color = recentlyHit ? Color.white : HqColor;
            _hqText.text = "본부  " + Mathf.CeilToInt(hq.Hp) + " / " + Mathf.CeilToInt(hq.MaxHp);

            _debugText.text = string.Format("FPS {0:0}  ({1:0.0} ms)\n좀비 {2}  /  활성 {3}", _fps, _frameMs, zombies.AliveCount, zombies.ActiveCount);
            _debugText.color = _fps >= 55f ? new Color(0.7f, 1f, 0.7f, 0.9f) : new Color(1f, 0.6f, 0.5f, 0.95f);

            // Hero bar follows the hero.
            var cam = _ctx.CameraRig.Camera;
            Vector3 screen = cam.WorldToScreenPoint(hero.Position + new Vector3(0f, 2.3f, 0f));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out var local))
            {
                _heroBar.anchoredPosition = local;
            }
            _heroFill.rectTransform.localScale = new Vector3(hero.IsAlive ? Mathf.Clamp01(hero.Hp / hero.MaxHp) : 0f, 1f, 1f);
            _heroRespawnText.text = hero.IsAlive ? "" : "부활 " + Mathf.CeilToInt(hero.RespawnRemaining);
            TickSquadBars(cam);

            // Skill button state.
            float cd = skill.Cooldown <= 0f ? 0f : skill.CooldownRemaining / skill.Cooldown;
            _skillCooldown.fillAmount = cd;
            if (skill.IsTargeting)
            {
                _skillLabel.text = "취소";
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);
                _skillBg.color = Color.Lerp(new Color(0.9f, 0.35f, 0.08f), new Color(1f, 0.6f, 0.2f), pulse);
            }
            else
            {
                _skillLabel.text = skill.IsReady ? "화염\n폭격" : Mathf.CeilToInt(skill.CooldownRemaining).ToString();
                _skillBg.color = skill.IsReady ? new Color(0.75f, 0.25f, 0.06f) : new Color(0.3f, 0.12f, 0.06f);
            }

            if (_bannerTimer > 0f)
            {
                _bannerTimer -= udt;
                float a = Mathf.Clamp01(_bannerTimer * 2f);
                var c = _bannerText.color;
                c.a = a * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f));
                _bannerText.color = c;
                if (_bannerTimer <= 0f) _bannerText.gameObject.SetActive(false);
            }

            TickNumbers(udt, cam);
        }

        private void TickNumbers(float udt, Camera cam)
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                var n = _numbers[i];
                if (!n.Text.gameObject.activeSelf) continue;
                n.Age += udt;
                float t = n.Age / n.Life;
                if (t >= 1f) { n.Text.gameObject.SetActive(false); continue; }

                Vector3 screen = cam.WorldToScreenPoint(n.World + new Vector3(0f, 0.4f + t * 1.4f, 0f));
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_damageLayer, screen, null, out var local))
                    n.Rect.anchoredPosition = local;

                float pop = t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f;
                n.Rect.localScale = Vector3.one * (n.Scale * pop);
                var c = n.Color;
                c.a = t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
                n.Text.color = c;
            }
        }

        // ------------------------------------------------------------------ helpers

        private DamageNumber CreateNumber()
        {
            var text = NewText("Dmg", _damageLayer, "", 40, Color.white, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 70f);
            text.gameObject.SetActive(false);
            return new DamageNumber { Text = text, Rect = rt };
        }

        private Button MakeButton(string name, RectTransform parent, string label, Color color, Action onClick, out Text labelText)
        {
            var img = NewImage(name, parent, color);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            labelText = NewText("Label", img.rectTransform, label, 48, Color.white, TextAnchor.MiddleCenter);
            labelText.fontStyle = FontStyle.Bold;
            Stretch(labelText.rectTransform);
            return btn;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private Text NewText(string name, Transform parent, string value, int size, Color color, TextAnchor anchor)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        private static void Stretch(RectTransform rt) => Anchor(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        public static string SideName(SpawnSide side)
        {
            switch (side)
            {
                case SpawnSide.East: return "동쪽";
                case SpawnSide.West: return "서쪽";
                default: return "북쪽";
            }
        }
    }
}

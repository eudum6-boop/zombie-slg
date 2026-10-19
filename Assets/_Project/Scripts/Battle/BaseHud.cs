using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Day-time HUD: resource bar, building panel (upgrade, workers), night button, toasts,
    /// offline-progress popup and per-building world labels. View only: actions go out through callbacks.
    /// </summary>
    public sealed class BaseHud
    {
        private static readonly string[] ResourceNames = { "식량", "연료", "자재" };
        private static readonly Color[] ResourceColors =
        {
            new Color(0.75f, 0.92f, 0.45f),
            new Color(1f, 0.68f, 0.3f),
            new Color(0.7f, 0.82f, 1f),
        };

        private readonly BattleContext _ctx;
        private readonly BaseView _view;
        private readonly Canvas _canvas;
        private readonly RectTransform _root;

        // top bar
        private Text _nightText;
        private Text _peopleText;
        private readonly Text[] _resAmount = new Text[3];
        private readonly Text[] _resRate = new Text[3];

        // bottom
        private GameObject _nightButton;
        private Text _nightLabel;
        private Text _hintText;

        // building panel
        private GameObject _panel;
        private Text _panelTitle;
        private Text _panelDesc;
        private Text _panelStats;
        private Text _panelNext;
        private Text _panelCost;
        private GameObject _workerRow;
        private Text _workerText;
        private Button _workerMinus;
        private Button _workerPlus;
        private Button _upgradeButton;
        private Text _upgradeLabel;
        private Text _upgradeReason;
        private GameObject _progressRow;
        private Image _progressFill;
        private Text _progressText;

        // popups
        private GameObject _offlinePanel;
        private Text _offlineBody;
        private readonly List<(Text text, Image bg, float time)> _toasts = new List<(Text, Image, float)>();

        // world labels
        private sealed class WorldLabel
        {
            public RectTransform Rect;
            public Text Text;
            public GameObject Bar;
            public Image Fill;
        }

        private readonly WorldLabel[] _labels;

        private sealed class Floater
        {
            public Text Text;
            public Vector3 World;
            public float Age;
        }

        private readonly List<Floater> _floaters = new List<Floater>();
        private int _floaterCursor;

        private readonly Action<int> _onUpgrade;
        private readonly Action<int, int> _onWorkers;
        private int _selected = -1;

        public bool Visible => _canvas.gameObject.activeSelf;

        public BaseHud(BattleContext ctx, BaseView view, Transform parent,
            Action onNight, Action<int> onUpgrade, Action<int, int> onWorkers, Action onClosePanel)
        {
            _ctx = ctx;
            _view = view;
            _onUpgrade = onUpgrade;
            _onWorkers = onWorkers;

            _canvas = UiKit.CreateCanvas("BaseHUD", parent, 5);
            _root = (RectTransform)_canvas.transform;

            var labelLayer = UiKit.Rect("WorldLabels", _root);
            UiKit.Stretch(labelLayer);
            _labels = new WorldLabel[MapLayout.Plots.Length];
            for (int i = 0; i < _labels.Length; i++) _labels[i] = CreateLabel(labelLayer);
            for (int i = 0; i < 24; i++) _floaters.Add(CreateFloater(labelLayer));

            BuildTopBar();
            BuildBottom(onNight);
            BuildPanel(onClosePanel);
            BuildOfflinePopup();
        }

        // ------------------------------------------------------------------ build

        private void BuildTopBar()
        {
            var bar = UiKit.Image("TopBar", _root, UiKit.Panel);
            UiKit.Anchor(bar.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -270f), Vector2.zero);

            _nightText = UiKit.Text("Night", bar.rectTransform, "NIGHT 1", 46, UiKit.Accent, TextAnchor.MiddleLeft, true);
            UiKit.Anchor(_nightText.rectTransform, new Vector2(0f, 1f), new Vector2(0.4f, 1f), new Vector2(40f, -100f), new Vector2(0f, -20f));

            _peopleText = UiKit.Text("People", bar.rectTransform, "", 32, Color.white, TextAnchor.MiddleRight);
            UiKit.Anchor(_peopleText.rectTransform, new Vector2(0.4f, 1f), Vector2.one, new Vector2(0f, -100f), new Vector2(-40f, -20f));

            for (int i = 0; i < 3; i++)
            {
                float x0 = i / 3f, x1 = (i + 1) / 3f;
                var chip = UiKit.Image("Res" + i, bar.rectTransform, UiKit.PanelLight, rounded: true);
                UiKit.Anchor(chip.rectTransform, new Vector2(x0, 0f), new Vector2(x1, 0f), new Vector2(i == 0 ? 30f : 10f, 20f), new Vector2(i == 2 ? -30f : -10f, 160f));

                var name = UiKit.Text("Name", chip.rectTransform, ResourceNames[i], 28, ResourceColors[i], TextAnchor.UpperLeft, true);
                UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, 0f), new Vector2(-10f, -12f));

                _resAmount[i] = UiKit.Text("Amount", chip.rectTransform, "0", 38, Color.white, TextAnchor.MiddleLeft, true);
                UiKit.Anchor(_resAmount[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, -6f), new Vector2(-10f, -6f));

                _resRate[i] = UiKit.Text("Rate", chip.rectTransform, "", 26, UiKit.Good, TextAnchor.LowerLeft);
                UiKit.Anchor(_resRate[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, 12f), new Vector2(-10f, 0f));
            }
        }

        private void BuildBottom(Action onNight)
        {
            var btn = UiKit.Button("NightButton", _root, "", 50, new Color(0.62f, 0.2f, 0.07f), onNight, out _nightLabel);
            UiKit.Place((RectTransform)btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(760f, 180f));
            _nightButton = btn.gameObject;

            _hintText = UiKit.Text("Hint", _root, "건물을 탭해서 짓거나 업그레이드하세요", 32, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
            UiKit.Place(_hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 280f), new Vector2(1000f, 60f));
        }

        private void BuildPanel(Action onClose)
        {
            var panel = UiKit.Image("BuildingPanel", _root, UiKit.Panel, rounded: true, raycast: true);
            UiKit.Anchor(panel.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 760f));
            _panel = panel.gameObject;
            var p = panel.rectTransform;

            _panelTitle = UiKit.Text("Title", p, "", 50, UiKit.Accent, TextAnchor.UpperLeft, true);
            UiKit.Anchor(_panelTitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -100f), new Vector2(-140f, -36f));

            var close = UiKit.Button("Close", p, "✕", 44, new Color(0.25f, 0.27f, 0.3f), onClose, out _);
            UiKit.Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-30f, -30f), new Vector2(90f, 90f));

            _panelDesc = UiKit.Text("Desc", p, "", 30, UiKit.Muted, TextAnchor.UpperLeft);
            _panelDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_panelDesc.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(44f, -190f), new Vector2(-44f, -110f));

            _panelStats = UiKit.Text("Stats", p, "", 34, Color.white, TextAnchor.UpperLeft);
            UiKit.Anchor(_panelStats.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -300f), new Vector2(-44f, -205f));

            _panelNext = UiKit.Text("Next", p, "", 30, new Color(0.7f, 0.9f, 1f), TextAnchor.UpperLeft);
            UiKit.Anchor(_panelNext.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -370f), new Vector2(-44f, -315f));

            // Workers row.
            _workerRow = UiKit.Rect("Workers", p).gameObject;
            var wr = (RectTransform)_workerRow.transform;
            UiKit.Anchor(wr, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -470f), new Vector2(-44f, -380f));
            _workerText = UiKit.Text("Text", wr, "", 34, Color.white, TextAnchor.MiddleLeft);
            UiKit.Anchor(_workerText.rectTransform, Vector2.zero, new Vector2(0.6f, 1f), Vector2.zero, Vector2.zero);
            _workerMinus = UiKit.Button("Minus", wr, "−", 54, new Color(0.25f, 0.27f, 0.3f), () => Workers(-1), out _);
            UiKit.Place((RectTransform)_workerMinus.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-130f, 0f), new Vector2(110f, 86f));
            _workerPlus = UiKit.Button("Plus", wr, "+", 54, new Color(0.2f, 0.45f, 0.25f), () => Workers(1), out _);
            UiKit.Place((RectTransform)_workerPlus.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(110f, 86f));

            _panelCost = UiKit.Text("Cost", p, "", 34, Color.white, TextAnchor.MiddleLeft);
            UiKit.Anchor(_panelCost.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(44f, 200f), new Vector2(-44f, 260f));

            _upgradeButton = UiKit.Button("Upgrade", p, "", 46, new Color(0.75f, 0.32f, 0.08f), () => { if (_selected >= 0) _onUpgrade?.Invoke(_selected); }, out _upgradeLabel);
            UiKit.Anchor((RectTransform)_upgradeButton.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(44f, 50f), new Vector2(-44f, 180f));

            _upgradeReason = UiKit.Text("Reason", p, "", 30, UiKit.Bad, TextAnchor.MiddleCenter);
            UiKit.Anchor(_upgradeReason.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(44f, 0f), new Vector2(-44f, 50f));

            // Construction progress (replaces cost + button while building).
            _progressRow = UiKit.Rect("Progress", p).gameObject;
            var pr = (RectTransform)_progressRow.transform;
            UiKit.Anchor(pr, Vector2.zero, new Vector2(1f, 0f), new Vector2(44f, 60f), new Vector2(-44f, 240f));
            var bg = UiKit.Image("Bg", pr, new Color(0.15f, 0.16f, 0.18f), rounded: true);
            UiKit.Anchor(bg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 10f), new Vector2(0f, 80f));
            _progressFill = UiKit.Image("Fill", bg.rectTransform, new Color(0.95f, 0.7f, 0.15f), rounded: true);
            _progressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            UiKit.Anchor(_progressFill.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _progressText = UiKit.Text("Text", pr, "", 36, Color.white, TextAnchor.UpperLeft, true);
            UiKit.Anchor(_progressText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 90f), Vector2.zero);

            _panel.SetActive(false);
        }

        private void BuildOfflinePopup()
        {
            var dim = UiKit.Image("OfflineDim", _root, new Color(0f, 0f, 0f, 0.6f), raycast: true);
            UiKit.Stretch(dim.rectTransform);
            _offlinePanel = dim.gameObject;

            var box = UiKit.Image("Box", dim.rectTransform, UiKit.Panel, rounded: true, raycast: true);
            UiKit.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 720f));

            var title = UiKit.Text("Title", box.rectTransform, "다시 오셨군요", 56, UiKit.Accent, TextAnchor.UpperCenter, true);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -130f), new Vector2(0f, -50f));

            _offlineBody = UiKit.Text("Body", box.rectTransform, "", 38, Color.white, TextAnchor.UpperCenter);
            UiKit.Anchor(_offlineBody.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 200f), new Vector2(-40f, -160f));

            var ok = UiKit.Button("Ok", box.rectTransform, "확인", 46, new Color(0.75f, 0.32f, 0.08f), () => _offlinePanel.SetActive(false), out _);
            UiKit.Place((RectTransform)ok.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(420f, 130f));
            _offlinePanel.SetActive(false);
        }

        private WorldLabel CreateLabel(RectTransform parent)
        {
            var bg = UiKit.Image("Label", parent, new Color(0f, 0f, 0f, 0.65f), rounded: true);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(150f, 50f);
            var text = UiKit.Text("Text", rt, "", 30, Color.white, TextAnchor.MiddleCenter, true);
            UiKit.Stretch(text.rectTransform);

            var bar = UiKit.Image("Bar", rt, new Color(0.15f, 0.15f, 0.15f, 0.9f));
            UiKit.Anchor(bar.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(8f, -16f), new Vector2(-8f, -4f));
            var fill = UiKit.Image("Fill", bar.rectTransform, new Color(0.95f, 0.7f, 0.15f));
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            UiKit.Stretch(fill.rectTransform);

            return new WorldLabel { Rect = rt, Text = text, Bar = bar.gameObject, Fill = fill };
        }

        private Floater CreateFloater(RectTransform parent)
        {
            var t = UiKit.Text("Float", parent, "", 34, Color.white, TextAnchor.MiddleCenter, true);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(300f, 60f);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.8f);
            o.effectDistance = new Vector2(2f, -2f);
            t.gameObject.SetActive(false);
            return new Floater { Text = t };
        }

        // ------------------------------------------------------------------ API

        public void SetVisible(bool visible) => _canvas.gameObject.SetActive(visible);

        public void Select(int id)
        {
            _selected = id;
            _panel.SetActive(id >= 0);
            _nightButton.SetActive(id < 0);
            _hintText.gameObject.SetActive(id < 0);
        }

        public void Toast(string message, Color color)
        {
            var bg = UiKit.Image("Toast", _root, new Color(0f, 0f, 0f, 0.75f), rounded: true);
            var text = UiKit.Text("Text", bg.rectTransform, message, 36, color, TextAnchor.MiddleCenter, true);
            UiKit.Stretch(text.rectTransform);
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            bg.rectTransform.pivot = new Vector2(0.5f, 1f);
            bg.rectTransform.sizeDelta = new Vector2(Mathf.Min(1000f, 120f + message.Length * 36f), 84f);
            _toasts.Add((text, bg, 2.8f));
            while (_toasts.Count > 3)
            {
                UnityEngine.Object.Destroy(_toasts[0].bg.gameObject);
                _toasts.RemoveAt(0);
            }
        }

        public void ShowOffline(OfflineProgressReport r)
        {
            _offlineBody.text =
                $"자리를 비운 <b>{UiKit.LongDuration(r.Seconds)}</b> 동안\n생존자들이 일했습니다.\n\n" +
                Line(0, r.Food) + Line(1, r.Fuel) + Line(2, r.Materials) +
                (r.UpgradesCompleted > 0 ? $"\n완료된 공사  {r.UpgradesCompleted}건" : "");
            _offlinePanel.SetActive(true);

            string Line(int i, double v)
            {
                string c = ColorUtility.ToHtmlStringRGB(ResourceColors[i]);
                string sign = v >= 0 ? "+" : "";
                return $"<color=#{c}>{ResourceNames[i]}</color>  {sign}{Math.Round(v):N0}\n";
            }
        }

        public void Float(Vector3 world, string text, Color color)
        {
            var f = _floaters[_floaterCursor];
            _floaterCursor = (_floaterCursor + 1) % _floaters.Count;
            f.World = world;
            f.Age = 0f;
            f.Text.text = text;
            f.Text.color = color;
            f.Text.gameObject.SetActive(true);
        }

        public static Color ResourceColor(ResourceType t) => ResourceColors[(int)t];
        public static string ResourceName(ResourceType t) => ResourceNames[(int)t];

        public void Tick(BaseState s, double now)
        {
            float udt = Time.unscaledDeltaTime;
            var cam = _ctx.CameraRig.Camera;
            int cap = s.StorageCap;

            _nightText.text = "NIGHT " + s.Night;
            string morale = s.IsStarving
                ? $"<color=#ff6a5a>굶주림! 사기 {s.Morale:0}%</color>"
                : $"사기 {s.Morale:0}%";
            _peopleText.text = $"생존자 {s.Survivors}/{s.SurvivorCapacity} (대기 {s.IdleSurvivors})   {morale}";

            for (int i = 0; i < 3; i++)
            {
                var type = (ResourceType)i;
                double amount = s.GetAmount(type);
                bool full = amount >= cap;
                _resAmount[i].text = $"{Math.Floor(amount):N0}<size=26><color=#8a93a0> / {cap:N0}</color></size>";
                double rate = s.NetRatePerHour(type);
                _resRate[i].text = full ? "창고 가득" : (rate >= 0 ? $"+{rate:N0}/시간" : $"{rate:N0}/시간");
                _resRate[i].color = full ? new Color(1f, 0.8f, 0.3f) : (rate >= 0 ? UiKit.Good : UiKit.Bad);
            }

            _nightLabel.text = $"밤 맞이하기  ▶  NIGHT {s.Night}";

            TickLabels(s, now, cam);
            TickFloaters(udt, cam);
            TickToasts(udt);
            if (_selected >= 0) RefreshPanel(s, now);
        }

        // ------------------------------------------------------------------ panel

        private void Workers(int delta)
        {
            if (_selected >= 0) _onWorkers?.Invoke(_selected, delta);
        }

        private void RefreshPanel(BaseState s, double now)
        {
            var b = s.Get(_selected);
            if (b == null) return;
            var def = BuildingCatalog.Get(b.Type);

            _panelTitle.text = b.Level > 0 ? $"{def.Name}  <size=40>Lv.{b.Level}</size>" : $"{def.Name}  <size=36><color=#9aa4b0>(미건설)</color></size>";
            _panelDesc.text = def.Description;
            _panelStats.text = b.Level > 0 ? StatLine(s, b, b.Level) : "<color=#9aa4b0>빈 부지입니다. 건설하면 기능이 열립니다.</color>";
            _panelNext.text = b.Level < def.MaxLevel ? $"Lv.{b.Level + 1}  →  {StatLine(s, b, b.Level + 1, preview: true)}" : "최대 레벨";

            bool producer = def.IsProducer && b.Level > 0;
            _workerRow.SetActive(producer);
            if (producer)
            {
                int slots = def.WorkerSlots(b.Level);
                _workerText.text = $"근무 {b.Workers}/{slots}   <color=#9aa4b0>대기 {s.IdleSurvivors}명</color>";
                _workerMinus.interactable = b.Workers > 0;
                _workerPlus.interactable = b.Workers < slots && s.IdleSurvivors > 0;
            }

            if (b.Upgrading)
            {
                _progressRow.SetActive(true);
                _panelCost.gameObject.SetActive(false);
                _upgradeButton.gameObject.SetActive(false);
                _upgradeReason.gameObject.SetActive(false);
                double total = def.BuildSecondsFor(b.Level + 1);
                double left = s.RemainingSeconds(b, now);
                float progress = total <= 0 ? 1f : Mathf.Clamp01(1f - (float)(left / total));
                _progressFill.rectTransform.localScale = new Vector3(progress, 1f, 1f);
                _progressText.text = $"공사 중  Lv.{b.Level + 1}   {UiKit.Duration(left)} 남음";
                return;
            }

            _progressRow.SetActive(false);
            _panelCost.gameObject.SetActive(true);
            _upgradeButton.gameObject.SetActive(true);
            _upgradeReason.gameObject.SetActive(true);

            var block = s.CanUpgrade(b.Id);
            if (block == UpgradeBlock.MaxLevel)
            {
                _panelCost.text = "";
                _upgradeLabel.text = "최대 레벨";
                _upgradeButton.interactable = false;
                _upgradeReason.text = "";
                return;
            }

            var cost = def.CostFor(b.Level + 1);
            _panelCost.text = CostText(s, cost) + $"    <color=#9aa4b0>시간</color> {UiKit.Duration(def.BuildSecondsFor(b.Level + 1))}";
            _upgradeLabel.text = b.Level == 0 ? "건설" : "업그레이드";
            _upgradeButton.interactable = block == UpgradeBlock.None;
            _upgradeReason.text = ReasonText(block, def, b);
        }

        private static string StatLine(BaseState s, BuildingSave b, int level, bool preview = false)
        {
            var def = BuildingCatalog.Get(b.Type);
            switch (b.Type)
            {
                case BuildingType.Headquarters:
                    return $"창고 {BuildingCatalog.StorageCap(level):N0}  ·  건물 최대 Lv.{level}  ·  밤 본부 체력 x{DefenseStats.HqHpMultiplier(level):0.0#}";
                case BuildingType.Shelter:
                    return $"수용 인원 {BuildingCatalog.ShelterCapacity(level)}명";
                case BuildingType.Clinic:
                    return $"영웅 회복 x{DefenseStats.HeroRegenMultiplier(level):0.0#}  ·  부활 대기 x{DefenseStats.HeroRespawnMultiplier(level):0.0#}";
                case BuildingType.GunTower:
                    return $"공격력 x{DefenseStats.TowerDamageMultiplier(level):0.0#}  ·  사거리 +{DefenseStats.TowerRangeBonus(level):0.0}  ·  연사 x{1f / DefenseStats.TowerIntervalMultiplier(level):0.0#}";
                default:
                    if (!def.IsProducer) return "";
                    string res = ResourceNames[(int)def.Produces];
                    if (preview) return $"최대 {res} {def.RatePerHour(level):N0}/시간  ·  인력 {def.WorkerSlots(level)}명";
                    double eff = s.Staffing(b) * s.MoraleMultiplier;
                    return $"{res} {s.EffectiveRatePerHour(b):N0}/시간   <color=#9aa4b0>(효율 {eff * 100:0}%)</color>";
            }
        }

        private static string CostText(BaseState s, Cost c)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var t = (ResourceType)i;
                int need = c.Get(t);
                if (need <= 0) continue;
                bool ok = s.GetAmount(t) >= need;
                string col = ok ? "ffffff" : "ff6a5a";
                parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(ResourceColors[i])}>{ResourceNames[i]}</color> <color=#{col}>{need:N0}</color>");
            }
            return string.Join("   ", parts);
        }

        private static string ReasonText(UpgradeBlock block, BuildingDef def, BuildingSave b)
        {
            switch (block)
            {
                case UpgradeBlock.HqTooLow:
                    return $"본부 Lv.{Math.Max(def.RequiredHqLevel, b.Level + 1)} 필요";
                case UpgradeBlock.QueueBusy: return "다른 건물이 공사 중입니다 (건설 대기열 1)";
                case UpgradeBlock.NotEnoughResources: return "자원이 부족합니다";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ world labels / floaters / toasts

        private void TickLabels(BaseState s, double now, Camera cam)
        {
            for (int id = 0; id < _labels.Length; id++)
            {
                var label = _labels[id];
                var b = s.Get(id);
                if (b == null) { label.Rect.gameObject.SetActive(false); continue; }

                Vector3 screen = cam.WorldToScreenPoint(_view.LabelAnchor(id));
                if (screen.z < 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local))
                {
                    label.Rect.gameObject.SetActive(false);
                    continue;
                }
                label.Rect.gameObject.SetActive(true);
                label.Rect.anchoredPosition = local;

                if (b.Upgrading)
                {
                    var def = BuildingCatalog.Get(b.Type);
                    double total = def.BuildSecondsFor(b.Level + 1);
                    double left = s.RemainingSeconds(b, now);
                    label.Text.text = UiKit.Duration(left);
                    label.Text.color = new Color(1f, 0.85f, 0.4f);
                    label.Bar.SetActive(true);
                    label.Fill.rectTransform.localScale = new Vector3(total <= 0 ? 1f : Mathf.Clamp01(1f - (float)(left / total)), 1f, 1f);
                    label.Rect.sizeDelta = new Vector2(150f, 50f);
                }
                else
                {
                    label.Bar.SetActive(false);
                    bool canUp = s.CanUpgrade(id) == UpgradeBlock.None;
                    if (b.Level == 0)
                    {
                        label.Text.text = canUp ? "건설 가능" : "빈 부지";
                        label.Text.color = canUp ? UiKit.Good : UiKit.Muted;
                        label.Rect.sizeDelta = new Vector2(170f, 50f);
                    }
                    else
                    {
                        label.Text.text = canUp ? $"Lv.{b.Level} ▲" : $"Lv.{b.Level}";
                        label.Text.color = canUp ? UiKit.Good : Color.white;
                        label.Rect.sizeDelta = new Vector2(canUp ? 130f : 100f, 50f);
                    }
                }
            }
        }

        private void TickFloaters(float udt, Camera cam)
        {
            foreach (var f in _floaters)
            {
                if (!f.Text.gameObject.activeSelf) continue;
                f.Age += udt;
                float t = f.Age / 1.6f;
                if (t >= 1f) { f.Text.gameObject.SetActive(false); continue; }
                Vector3 screen = cam.WorldToScreenPoint(f.World + new Vector3(0f, t * 1.5f, 0f));
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local))
                    f.Text.rectTransform.anchoredPosition = local;
                var c = f.Text.color;
                c.a = t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
                f.Text.color = c;
            }
        }

        private void TickToasts(float udt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (text, bg, time) = _toasts[i];
                time -= udt;
                if (time <= 0f)
                {
                    UnityEngine.Object.Destroy(bg.gameObject);
                    _toasts.RemoveAt(i);
                    continue;
                }
                _toasts[i] = (text, bg, time);
            }
            for (int i = 0; i < _toasts.Count; i++)
            {
                var (text, bg, time) = _toasts[i];
                int slot = _toasts.Count - 1 - i;
                bg.rectTransform.anchoredPosition = new Vector2(0f, -290f - slot * 96f);
                float a = Mathf.Clamp01(time * 2f);
                bg.color = new Color(0f, 0f, 0f, 0.75f * a);
                var c = text.color;
                c.a = a;
                text.color = c;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Research screen: three branch columns of node cards (tier order) and a detail panel with the
    /// start button. One project at a time. View only; actions go out through a callback.
    /// </summary>
    public sealed class ResearchUi
    {
        private static readonly string[] BranchNames = { "경제", "군사", "방어" };
        private static readonly Color[] BranchColors =
        {
            new Color(0.55f, 0.85f, 0.4f),
            new Color(1f, 0.55f, 0.35f),
            new Color(0.45f, 0.7f, 1f),
        };

        private readonly BattleContext _ctx;
        private readonly Action<string> _onStart;
        private readonly GameObject _screen;
        private readonly Text _status;
        private readonly Image _statusFill;
        private readonly List<Node> _nodes = new List<Node>();
        private readonly Text _detailTitle;
        private readonly Text _detailBody;
        private readonly Text _detailCost;
        private readonly Button _startButton;
        private readonly Text _startLabel;
        private string _selected;

        private sealed class Node
        {
            public ResearchDef Def;
            public Image Bg;
            public Text Name;
            public Text Level;
        }

        public bool IsOpen => _screen.activeSelf;

        public ResearchUi(BattleContext ctx, RectTransform root, Action<string> onStart)
        {
            _ctx = ctx;
            _onStart = onStart;

            var (screen, box) = HeroUi.Modal(root, "ResearchScreen", "연구");
            _screen = screen;

            _status = UiKit.Text("Status", box, "", 32, Color.white, TextAnchor.MiddleLeft);
            UiKit.Anchor(_status.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -200f), new Vector2(-40f, -130f));
            var barBg = UiKit.Image("Bar", box, new Color(0.15f, 0.16f, 0.18f), rounded: true);
            UiKit.Anchor(barBg.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -232f), new Vector2(-40f, -208f));
            _statusFill = UiKit.Image("Fill", barBg.rectTransform, new Color(0.4f, 0.8f, 1f), rounded: true);
            _statusFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            UiKit.Stretch(_statusFill.rectTransform);

            // Columns.
            const float colTop = -255f;
            for (int b = 0; b < 3; b++)
            {
                float x0 = b / 3f, x1 = (b + 1) / 3f;
                var head = UiKit.Text("Branch" + b, box, BranchNames[b], 38, BranchColors[b], TextAnchor.MiddleCenter, true);
                UiKit.Anchor(head.rectTransform, new Vector2(x0, 1f), new Vector2(x1, 1f), new Vector2(10f, colTop - 60f), new Vector2(-10f, colTop));

                int row = 0;
                foreach (var def in ctx.Data.ResearchNodes)
                {
                    if ((int)def.Branch != b) continue;
                    _nodes.Add(CreateNode(box, def, x0, x1, colTop - 70f - row * 168f));
                    row++;
                }
            }

            var detail = UiKit.Image("Detail", box, UiKit.PanelLight, rounded: true);
            UiKit.Anchor(detail.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 520f));
            var d = detail.rectTransform;
            _detailTitle = UiKit.Text("Title", d, "", 44, Color.white, TextAnchor.UpperLeft, true);
            UiKit.Anchor(_detailTitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -80f), new Vector2(-30f, -20f));
            _detailBody = UiKit.Text("Body", d, "", 30, new Color(0.85f, 0.87f, 0.9f), TextAnchor.UpperLeft);
            _detailBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_detailBody.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -250f), new Vector2(-30f, -90f));
            _detailCost = UiKit.Text("Cost", d, "", 30, Color.white, TextAnchor.MiddleLeft);
            UiKit.Anchor(_detailCost.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 150f), new Vector2(-30f, 210f));
            _startButton = UiKit.Button("Start", d, "", 40, new Color(0.2f, 0.45f, 0.6f), () => { if (_selected != null) _onStart?.Invoke(_selected); }, out _startLabel);
            UiKit.Anchor((RectTransform)_startButton.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 140f));

            _screen.SetActive(false);
        }

        private Node CreateNode(RectTransform box, ResearchDef def, float x0, float x1, float top)
        {
            var bg = UiKit.Image("Node_" + def.Id, box, UiKit.PanelLight, rounded: true, raycast: true);
            UiKit.Anchor(bg.rectTransform, new Vector2(x0, 1f), new Vector2(x1, 1f), new Vector2(12f, top - 150f), new Vector2(-12f, top));
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            string id = def.Id;
            btn.onClick.AddListener(() => { _selected = id; _ctx.Feedback.Sfx.Play(SfxId.Click, 0f); });

            var name = UiKit.Text("Name", bg.rectTransform, def.Name, 32, Color.white, TextAnchor.UpperLeft, true);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 50f), new Vector2(-12f, -14f));
            var level = UiKit.Text("Level", bg.rectTransform, "", 28, UiKit.Muted, TextAnchor.LowerLeft);
            UiKit.Anchor(level.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 12f), new Vector2(-12f, 0f));
            return new Node { Def = def, Bg = bg, Name = name, Level = level };
        }

        public void Open(BaseState s, double now)
        {
            _screen.SetActive(true);
            if (_selected == null)
            {
                foreach (var n in _nodes)
                    if (s.CanResearch(n.Def.Id) == ResearchBlock.None) { _selected = n.Def.Id; break; }
                if (_selected == null && _nodes.Count > 0) _selected = _nodes[0].Def.Id;
            }
            Refresh(s, now);
        }

        public void Close() => _screen.SetActive(false);

        public void Refresh(BaseState s, double now)
        {
            string active = s.ActiveResearch;
            if (active != null)
            {
                var a = _ctx.Data.ResearchNode(active);
                int target = s.ResearchLevel(active) + 1;
                double total = a.SecondsFor(target);
                double left = s.ResearchRemaining(now);
                _status.text = $"연구 중: <b>{a.Name} Lv.{target}</b>   <color=#ffd070>{UiKit.Duration(left)}</color>";
                _statusFill.rectTransform.localScale = new Vector3(total <= 0 ? 1f : Mathf.Clamp01(1f - (float)(left / total)), 1f, 1f);
            }
            else
            {
                _status.text = s.LabLevel > 0
                    ? $"연구소 Lv.{s.LabLevel}  ·  <color=#9aa4b0>단계 {s.LabLevel}까지 연구 가능  ·  한 번에 하나</color>"
                    : "<color=#ff9a80>연구소를 건설하면 연구할 수 있습니다</color>";
                _statusFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
            }

            foreach (var n in _nodes)
            {
                var def = n.Def;
                int lv = s.ResearchLevel(def.Id);
                var block = s.CanResearch(def.Id);
                bool locked = block == ResearchBlock.NoLab || block == ResearchBlock.LabTooLow || block == ResearchBlock.RequiresOther;
                bool maxed = lv >= def.MaxLevel;
                bool running = active == def.Id;
                bool selected = _selected == def.Id;

                var bc = BranchColors[(int)def.Branch];
                n.Bg.color = selected ? Color.Lerp(UiKit.PanelLight, bc, 0.45f)
                    : running ? Color.Lerp(UiKit.PanelLight, new Color(0.4f, 0.8f, 1f), 0.3f)
                    : UiKit.PanelLight;
                n.Name.color = locked ? new Color(0.5f, 0.52f, 0.56f) : Color.white;
                n.Level.text = running ? "<color=#7fd8ff>연구 중</color>"
                    : maxed ? $"<color=#ffd070>MAX {lv}/{def.MaxLevel}</color>"
                    : locked ? $"<color=#7a808a>잠김 {lv}/{def.MaxLevel}</color>"
                    : block == ResearchBlock.None ? $"<color=#8fe08a>{lv}/{def.MaxLevel} ▲</color>"
                    : $"{lv}/{def.MaxLevel}";
            }

            RefreshDetail(s);
        }

        private void RefreshDetail(BaseState s)
        {
            if (_selected == null || !_ctx.Data.HasResearch(_selected)) return;
            var def = _ctx.Data.ResearchNode(_selected);
            int lv = s.ResearchLevel(def.Id);
            int target = lv + 1;
            bool maxed = lv >= def.MaxLevel;

            _detailTitle.text = $"{def.Name}  <size=32><color=#9aa4b0>{BranchNames[(int)def.Branch]} · 단계 {def.Tier} · Lv.{lv}/{def.MaxLevel}</color></size>";
            string effect = EffectName(def.Effect);
            string sign = ResearchDef.IsReduction(def.Effect) ? "−" : "+";
            string now = $"{sign}{def.PerLevel * lv * 100:0.#}%";
            string next = maxed ? "" : $"  →  <color=#8fe08a>{sign}{def.PerLevel * target * 100:0.#}%</color>";
            string req = def.Requires != null ? $"\n<color=#9aa4b0>선행: {_ctx.Data.ResearchNode(def.Requires).Name} Lv.1</color>" : "";
            _detailBody.text = $"{def.Description}\n\n{effect}  {now}{next}{req}";

            var block = s.CanResearch(def.Id);
            if (maxed)
            {
                _detailCost.text = "";
                _startLabel.text = "최대 레벨";
                _startButton.interactable = false;
                return;
            }
            _detailCost.text = $"{ArmyUi.CostText(s, def.CostFor(target))}   <color=#9aa4b0>시간</color> {UiKit.Duration(def.SecondsFor(target))}";
            _startButton.interactable = block == ResearchBlock.None;
            _startLabel.text = block == ResearchBlock.None ? $"연구 시작 (Lv.{target})" : BlockText(block, def);
        }

        private string BlockText(ResearchBlock block, ResearchDef def)
        {
            switch (block)
            {
                case ResearchBlock.NoLab: return "연구소 필요";
                case ResearchBlock.LabTooLow: return $"연구소 Lv.{def.Tier} 필요";
                case ResearchBlock.RequiresOther: return $"선행 연구 필요: {_ctx.Data.ResearchNode(def.Requires).Name}";
                case ResearchBlock.Busy: return "다른 연구 진행 중";
                case ResearchBlock.NotEnoughResources: return "자원 부족";
                default: return "연구 불가";
            }
        }

        public static string EffectName(ResearchEffect e)
        {
            switch (e)
            {
                case ResearchEffect.ProductionFood: return "식량 생산";
                case ResearchEffect.ProductionFuel: return "연료 생산";
                case ResearchEffect.ProductionMaterials: return "자재 생산";
                case ResearchEffect.Storage: return "창고 용량";
                case ResearchEffect.BuildSpeed: return "건설 시간";
                case ResearchEffect.TroopDamage: return "병력 공격력";
                case ResearchEffect.TroopHp: return "병력 체력";
                case ResearchEffect.TrainSpeed: return "훈련 시간";
                case ResearchEffect.HealCost: return "치료 비용";
                case ResearchEffect.TowerDamage: return "포탑 공격력";
                case ResearchEffect.HqHp: return "본부 체력";
                case ResearchEffect.HeroDamage: return "영웅 공격력";
                case ResearchEffect.SkillDamage: return "스킬 피해";
                default: return e.ToString();
            }
        }
    }
}

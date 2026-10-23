using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Hero roster screen (list, details, promote, assign) and recruit screen (1/10 pulls, results,
    /// probability disclosure). View only; actions go out through callbacks to GameFlow.
    /// </summary>
    public sealed class HeroUi
    {
        public static readonly string[] RarityNames = { "일반", "희귀", "영웅", "전설" };
        public static readonly string[] RoleNames = { "전투", "내정", "정찰" };
        public static readonly Color[] RarityColors =
        {
            new Color(0.62f, 0.65f, 0.70f),
            new Color(0.36f, 0.62f, 1.00f),
            new Color(0.72f, 0.46f, 1.00f),
            new Color(1.00f, 0.76f, 0.25f),
        };

        private readonly BattleContext _ctx;
        private readonly Action<string> _onPromote;
        private readonly Action<HeroSlot, string> _onAssign;
        private readonly Action<int> _onRecruit;

        // hero screen
        private readonly GameObject _heroScreen;
        private readonly Text _slotsText;
        private readonly List<HeroCard> _cards = new List<HeroCard>();
        private readonly Text _detailTitle;
        private readonly Text _detailMeta;
        private readonly Text _detailDesc;
        private readonly Text _detailStats;
        private readonly Button _promoteButton;
        private readonly Text _promoteLabel;
        private readonly Button _assignButton;
        private readonly Text _assignLabel;
        private string _selectedHero;

        // recruit screen
        private readonly GameObject _gachaScreen;
        private readonly Text _ticketsText;
        private readonly Text _pityText;
        private readonly Button _pull1;
        private readonly Button _pull10;
        private readonly List<ResultCard> _results = new List<ResultCard>();
        private readonly GameObject _ratesOverlay;
        private readonly Text _ratesText;
        private float _revealTime = -1f;

        private sealed class HeroCard
        {
            public string Id;
            public Image Bg;
            public Image Strip;
            public Text Name;
            public Text Meta;
            public Text Stars;
            public Text State;
        }

        private sealed class ResultCard
        {
            public RectTransform Rect;
            public Image Bg;
            public Text Name;
            public Text Rarity;
            public Text Tag;
            public float Delay;
            public bool Legendary;
        }

        public bool IsOpen => _heroScreen.activeSelf || _gachaScreen.activeSelf;

        public HeroUi(BattleContext ctx, RectTransform root, Action<string> onPromote, Action<HeroSlot, string> onAssign, Action<int> onRecruit)
        {
            _ctx = ctx;
            _onPromote = onPromote;
            _onAssign = onAssign;
            _onRecruit = onRecruit;

            // ---------------- hero screen ----------------
            var (heroScreen, heroBox) = Modal(root, "HeroScreen", "영웅");
            _heroScreen = heroScreen;

            _slotsText = UiKit.Text("Slots", heroBox, "", 30, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(_slotsText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -190f), new Vector2(-40f, -130f));

            var heroes = ctx.Data.Heroes;
            for (int i = 0; i < heroes.Count; i++)
            {
                int col = i % 2, row = i / 2;
                _cards.Add(CreateHeroCard(heroBox, heroes[i].Id, col, row));
            }

            float detailTop = -210f - ((heroes.Count + 1) / 2) * 152f - 20f;
            var detail = UiKit.Image("Detail", heroBox, UiKit.PanelLight, rounded: true);
            UiKit.Anchor(detail.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 30f), new Vector2(-30f, detailTop));
            var d = detail.rectTransform;

            _detailTitle = UiKit.Text("Title", d, "", 46, Color.white, TextAnchor.UpperLeft, true);
            UiKit.Anchor(_detailTitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -80f), new Vector2(-30f, -20f));
            _detailMeta = UiKit.Text("Meta", d, "", 30, UiKit.Muted, TextAnchor.UpperLeft);
            UiKit.Anchor(_detailMeta.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -125f), new Vector2(-30f, -85f));
            _detailDesc = UiKit.Text("Desc", d, "", 30, new Color(0.85f, 0.87f, 0.9f), TextAnchor.UpperLeft);
            _detailDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_detailDesc.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -215f), new Vector2(-30f, -135f));
            _detailStats = UiKit.Text("Stats", d, "", 31, Color.white, TextAnchor.UpperLeft);
            _detailStats.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_detailStats.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, -330f), new Vector2(-30f, -225f));

            _promoteButton = UiKit.Button("Promote", d, "", 36, new Color(0.55f, 0.35f, 0.75f), () => { if (_selectedHero != null) _onPromote?.Invoke(_selectedHero); }, out _promoteLabel);
            UiKit.Anchor((RectTransform)_promoteButton.transform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(30f, 30f), new Vector2(-10f, 140f));
            _assignButton = UiKit.Button("Assign", d, "", 36, new Color(0.75f, 0.32f, 0.08f), Assign, out _assignLabel);
            UiKit.Anchor((RectTransform)_assignButton.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(10f, 30f), new Vector2(-30f, 140f));

            _heroScreen.SetActive(false);

            // ---------------- recruit screen ----------------
            var (gachaScreen, gachaBox) = Modal(root, "RecruitScreen", "생존자 모집");
            _gachaScreen = gachaScreen;

            _ticketsText = UiKit.Text("Tickets", gachaBox, "", 48, Color.white, TextAnchor.MiddleLeft, true);
            UiKit.Anchor(_ticketsText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -210f), new Vector2(-40f, -130f));
            _pityText = UiKit.Text("Pity", gachaBox, "", 30, RarityColors[3], TextAnchor.MiddleLeft);
            UiKit.Anchor(_pityText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -260f), new Vector2(-40f, -210f));

            var g = ctx.Data.Gacha;
            var ratesLine = UiKit.Text("RatesLine", gachaBox,
                $"전설 {g.RarityRates[3] * 100:0.##}%  ·  영웅 {g.RarityRates[2] * 100:0.##}%  ·  희귀 {g.RarityRates[1] * 100:0.##}%  ·  일반 {g.RarityRates[0] * 100:0.##}%",
                28, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(ratesLine.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -305f), new Vector2(-260f, -265f));

            var ratesButton = UiKit.Button("RatesButton", gachaBox, "확률 보기", 30, new Color(0.25f, 0.27f, 0.3f), ToggleRates, out _);
            UiKit.Place((RectTransform)ratesButton.transform, Vector2.one, Vector2.one, new Vector2(-40f, -255f), new Vector2(210f, 70f));

            for (int i = 0; i < 10; i++) _results.Add(CreateResultCard(gachaBox, i));

            _pull1 = UiKit.Button("Pull1", gachaBox, "1회 모집\n<size=28>모집권 1장</size>", 40, new Color(0.3f, 0.45f, 0.75f), () => _onRecruit?.Invoke(1), out _);
            UiKit.Anchor((RectTransform)_pull1.transform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(40f, 40f), new Vector2(-15f, 220f));
            string tenSub = g.TenPullGuaranteeRare ? "모집권 10장 · 희귀 이상 1명 확정" : "모집권 10장";
            _pull10 = UiKit.Button("Pull10", gachaBox, $"10회 모집\n<size=26>{tenSub}</size>", 40, new Color(0.75f, 0.32f, 0.08f), () => _onRecruit?.Invoke(10), out _);
            UiKit.Anchor((RectTransform)_pull10.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(15f, 40f), new Vector2(-40f, 220f));

            var overlay = UiKit.Image("Rates", gachaBox, new Color(0.04f, 0.05f, 0.07f, 0.97f), rounded: true, raycast: true);
            UiKit.Anchor(overlay.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 30f), new Vector2(-30f, -130f));
            _ratesText = UiKit.Text("Text", overlay.rectTransform, BuildDisclosure(), 30, Color.white, TextAnchor.UpperLeft);
            _ratesText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_ratesText.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 150f), new Vector2(-40f, -40f));
            var closeRates = UiKit.Button("Close", overlay.rectTransform, "닫기", 36, new Color(0.25f, 0.27f, 0.3f), ToggleRates, out _);
            UiKit.Place((RectTransform)closeRates.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(360f, 100f));
            _ratesOverlay = overlay.gameObject;
            _ratesOverlay.SetActive(false);

            _gachaScreen.SetActive(false);
        }

        // ------------------------------------------------------------------ API

        public void OpenHeroes(HeroRoster roster)
        {
            _gachaScreen.SetActive(false);
            _heroScreen.SetActive(true);
            if (_selectedHero == null) _selectedHero = roster.Leader?.Id;
            Refresh(roster);
        }

        public void OpenRecruit(HeroRoster roster)
        {
            _heroScreen.SetActive(false);
            _gachaScreen.SetActive(true);
            _ratesOverlay.SetActive(false);
            foreach (var c in _results) c.Rect.gameObject.SetActive(false);
            Refresh(roster);
        }

        public void Close()
        {
            _heroScreen.SetActive(false);
            _gachaScreen.SetActive(false);
        }

        public void ShowResults(IReadOnlyList<GachaResult> results)
        {
            for (int i = 0; i < _results.Count; i++)
            {
                var card = _results[i];
                bool show = i < results.Count;
                card.Rect.gameObject.SetActive(show);
                if (!show) continue;

                var r = results[i];
                var def = _ctx.Data.Hero(r.HeroId);
                var color = RarityColors[(int)r.Rarity];
                card.Bg.color = Color.Lerp(new Color(0.1f, 0.11f, 0.13f), color, 0.55f);
                card.Name.text = def.Name;
                card.Rarity.text = RarityNames[(int)r.Rarity];
                card.Rarity.color = color;
                card.Tag.text = r.IsNew ? "<color=#7dff9a>NEW</color>" : $"+{r.ShardsGained} 조각";
                if (r.FromPity) card.Tag.text += "\n<color=#ffd060>천장</color>";
                else if (r.FromGuarantee) card.Tag.text += "\n<color=#9fd0ff>확정</color>";
                card.Delay = i * 0.09f;
                card.Legendary = r.Rarity == HeroRarity.Legendary;
                card.Rect.localScale = Vector3.zero;
            }
            _revealTime = 0f;
        }

        public void Refresh(HeroRoster roster)
        {
            if (_heroScreen.activeSelf) RefreshHeroes(roster);
            if (_gachaScreen.activeSelf) RefreshRecruit(roster);
        }

        public void Tick(float udt)
        {
            if (_revealTime < 0f) return;
            _revealTime += udt;
            bool done = true;
            foreach (var c in _results)
            {
                if (!c.Rect.gameObject.activeSelf) continue;
                float t = Mathf.Clamp01((_revealTime - c.Delay) / 0.22f);
                if (t < 1f) done = false;
                float s = t <= 0f ? 0f : 1f + Mathf.Sin(t * Mathf.PI) * 0.25f * (1f - t);
                if (c.Legendary && t >= 1f) s = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.04f;
                c.Rect.localScale = new Vector3(s, s, 1f);
            }
            bool anyLegendary = _results.Exists(c => c.Legendary && c.Rect.gameObject.activeSelf);
            if (done && !anyLegendary) _revealTime = -1f;
        }

        // ------------------------------------------------------------------ refresh

        private void RefreshHeroes(HeroRoster roster)
        {
            var data = _ctx.Data;
            _slotsText.text = $"출전 <color=#ffffff>{SlotName(roster, HeroSlot.Leader)}</color>   ·   " +
                              $"관리 <color=#ffffff>{SlotName(roster, HeroSlot.Manager)}</color>   ·   " +
                              $"정찰 <color=#ffffff>{SlotName(roster, HeroSlot.Scout)}</color>";

            foreach (var card in _cards)
            {
                var def = data.Hero(card.Id);
                var owned = roster.Get(card.Id);
                var rc = RarityColors[(int)def.Rarity];
                bool selected = card.Id == _selectedHero;
                card.Bg.color = owned != null
                    ? Color.Lerp(new Color(0.12f, 0.13f, 0.16f), rc, selected ? 0.38f : 0.16f)
                    : new Color(0.09f, 0.09f, 0.1f, selected ? 1f : 0.85f);
                card.Strip.color = owned != null ? ParseHex(def.ColorHex, rc) : new Color(0.25f, 0.25f, 0.25f);
                card.Name.text = def.Name;
                card.Name.color = owned != null ? Color.white : new Color(0.55f, 0.55f, 0.58f);
                card.Meta.text = $"<color=#{ColorUtility.ToHtmlStringRGB(rc)}>{RarityNames[(int)def.Rarity]}</color> · {RoleNames[(int)def.Role]}{AssignedTag(roster, def)}";
                card.Stars.text = owned != null ? StarText(owned.Stars, data.HeroRules.MaxStars) : "";
                if (owned == null) card.State.text = "미보유";
                else if (owned.Stars >= data.HeroRules.MaxStars) card.State.text = "최대";
                else
                {
                    int need = data.HeroRules.CostToReach(owned.Stars + 1);
                    string col = owned.Shards >= need ? "7dff9a" : "aab2bd";
                    card.State.text = $"<color=#{col}>조각 {owned.Shards}/{need}</color>";
                }
            }

            if (_selectedHero == null || !data.HasHero(_selectedHero)) return;
            var sel = data.Hero(_selectedHero);
            var mine = roster.Get(_selectedHero);
            var color = RarityColors[(int)sel.Rarity];

            _detailTitle.text = sel.Name + (mine != null ? $"  <size=36>{StarText(mine.Stars, data.HeroRules.MaxStars)}</size>" : "  <size=30><color=#8a93a0>미보유</color></size>");
            _detailTitle.color = color;
            _detailMeta.text = $"{RarityNames[(int)sel.Rarity]} 등급  ·  {RoleNames[(int)sel.Role]} 영웅";
            _detailDesc.text = sel.Description;

            int stars = Math.Max(1, mine?.Stars ?? 1);
            float m = data.HeroRules.StatMultiplier(stars);
            switch (sel.Role)
            {
                case HeroRole.Combat:
                    var cs = roster.Owns(sel.Id) ? roster.CombatStats(sel.Id) : new HeroCombatStats(sel.Hp, sel.Damage, sel.FireInterval, sel.Range, sel.MoveSpeed, sel.CritChance, sel.SkillPower);
                    _detailStats.text =
                        $"체력 {cs.MaxHp:0}   공격 {cs.Damage:0.#}   연사 {1f / cs.FireInterval:0.0}/초\n" +
                        $"사거리 {cs.Range:0.#}   이동 {cs.MoveSpeed:0.#}   치명 {cs.CritChance * 100:0}%   화염 폭격 x{cs.SkillPower:0.00}";
                    break;
                case HeroRole.Economy:
                    _detailStats.text = $"관리자로 배치하면 기지 전체 생산 <color=#7dff9a>+{sel.EconBonus * m * 100:0.#}%</color>\n<color=#8a93a0>승급할수록 보너스가 커집니다</color>";
                    break;
                default:
                    _detailStats.text = $"정찰대장으로 배치하면 밤 자원 보상 <color=#7dff9a>+{sel.ScoutBonus * m * 100:0.#}%</color>\n<color=#8a93a0>승급할수록 보너스가 커집니다</color>";
                    break;
            }

            var promote = roster.CanPromote(sel.Id);
            if (mine == null)
            {
                _promoteLabel.text = "모집에서 획득";
                _promoteButton.interactable = false;
            }
            else if (promote == PromoteBlock.MaxStars)
            {
                _promoteLabel.text = "최대 승급";
                _promoteButton.interactable = false;
            }
            else
            {
                int need = data.HeroRules.CostToReach(mine.Stars + 1);
                _promoteLabel.text = $"승급 ★{mine.Stars + 1}\n<size=26>조각 {mine.Shards}/{need}</size>";
                _promoteButton.interactable = promote == PromoteBlock.None;
            }

            var slot = SlotFor(sel.Role);
            bool assigned = roster.AssignedId(slot) == sel.Id;
            _assignLabel.text = assigned ? "배치 중" : sel.Role == HeroRole.Combat ? "출전시키기" : sel.Role == HeroRole.Economy ? "관리자로 배치" : "정찰대장으로 배치";
            _assignButton.interactable = mine != null && !assigned;
        }

        private void RefreshRecruit(HeroRoster roster)
        {
            _ticketsText.text = $"모집권 {roster.Tickets}장";
            _pityText.text = $"전설 확정까지 {roster.PullsUntilPity}회";
            _pull1.interactable = roster.CanPull(1) == PullBlock.None;
            _pull10.interactable = roster.CanPull(10) == PullBlock.None;
        }

        // ------------------------------------------------------------------ actions

        private void Assign()
        {
            if (_selectedHero == null) return;
            _onAssign?.Invoke(SlotFor(_ctx.Data.Hero(_selectedHero).Role), _selectedHero);
        }

        private void ToggleRates() => _ratesOverlay.SetActive(!_ratesOverlay.activeSelf);

        private static HeroSlot SlotFor(HeroRole role) =>
            role == HeroRole.Economy ? HeroSlot.Manager : role == HeroRole.Explore ? HeroSlot.Scout : HeroSlot.Leader;

        private string SlotName(HeroRoster roster, HeroSlot slot)
        {
            var def = roster.Assigned(slot);
            return def == null ? "<color=#8a93a0>없음</color>" : $"{def.Name} ★{roster.Stars(def.Id)}";
        }

        private static string AssignedTag(HeroRoster roster, HeroDef def)
        {
            var slot = SlotFor(def.Role);
            return roster.AssignedId(slot) == def.Id ? (def.Role == HeroRole.Combat ? " · <color=#ffb060>출전</color>" : " · <color=#ffb060>배치</color>") : "";
        }

        private static string StarText(int stars, int max) => new string('★', stars) + new string('☆', Math.Max(0, max - stars));

        private string BuildDisclosure()
        {
            var g = _ctx.Data.Gacha;
            var r = _ctx.Data.HeroRules;
            var sb = new StringBuilder();
            sb.Append("<b>등급별 확률 (1회 모집 기준)</b>\n");
            for (int i = 3; i >= 0; i--)
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(RarityColors[i])}>{RarityNames[i]}</color>  {g.RarityRates[i] * 100:0.00}%\n");
            sb.Append("\n<b>영웅별 확률</b>\n");
            var list = g.Disclose(_ctx.Data.Heroes);
            list.Sort((a, b) => b.Rarity.CompareTo(a.Rarity));
            foreach (var d in list)
                sb.Append($"{d.Name} <color=#{ColorUtility.ToHtmlStringRGB(RarityColors[(int)d.Rarity])}>({RarityNames[(int)d.Rarity]})</color>  {d.Probability * 100:0.000}%\n");
            sb.Append($"\n<size=26><color=#aab2bd>· {g.PityLegendary - 1}회 연속으로 전설 영웅이 나오지 않으면 다음 모집은 전설 확정 (천장)\n");
            if (g.TenPullGuaranteeRare) sb.Append("· 10회 모집 시 희귀 등급 이상 1명 확정\n");
            sb.Append($"· 이미 보유한 영웅은 조각으로 전환 (일반 {r.DuplicateShards(HeroRarity.Common)} / 희귀 {r.DuplicateShards(HeroRarity.Rare)} / " +
                      $"영웅 {r.DuplicateShards(HeroRarity.Epic)} / 전설 {r.DuplicateShards(HeroRarity.Legendary)})\n");
            sb.Append("· 같은 등급 안의 영웅은 같은 확률로 등장</color></size>");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ build helpers

        internal static (GameObject screen, RectTransform box) Modal(RectTransform root, string name, string title)
        {
            var dim = UiKit.Image(name, root, new Color(0f, 0f, 0f, 0.7f), raycast: true);
            UiKit.Stretch(dim.rectTransform);
            var box = UiKit.Image("Box", dim.rectTransform, UiKit.Panel, rounded: true, raycast: true);
            UiKit.Anchor(box.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 40f), new Vector2(-24f, -60f));

            var t = UiKit.Text("Title", box.rectTransform, title, 54, UiKit.Accent, TextAnchor.MiddleLeft, true);
            UiKit.Anchor(t.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -120f), new Vector2(-140f, -30f));
            var close = UiKit.Button("Close", box.rectTransform, "✕", 44, new Color(0.25f, 0.27f, 0.3f), () => dim.gameObject.SetActive(false), out _);
            UiKit.Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-30f, -30f), new Vector2(90f, 90f));
            return (dim.gameObject, box.rectTransform);
        }

        private HeroCard CreateHeroCard(RectTransform parent, string id, int col, int row)
        {
            var bg = UiKit.Image("Hero_" + id, parent, UiKit.PanelLight, rounded: true, raycast: true);
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(col * 0.5f, 1f);
            rt.anchorMax = new Vector2(col * 0.5f + 0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(col == 0 ? 30f : 8f, 0f);
            rt.offsetMax = new Vector2(col == 0 ? -8f : -30f, 0f);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -210f - row * 152f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 140f);

            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                _selectedHero = id;
                _ctx.Feedback.Sfx.Play(SfxId.Click, 0f);
            });

            var strip = UiKit.Image("Strip", rt, Color.white, rounded: true);
            UiKit.Anchor(strip.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(10f, 12f), new Vector2(26f, -12f));

            var name = UiKit.Text("Name", rt, "", 38, Color.white, TextAnchor.UpperLeft, true);
            UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(42f, 0f), new Vector2(-12f, -14f));
            var meta = UiKit.Text("Meta", rt, "", 26, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(meta.rectTransform, Vector2.zero, Vector2.one, new Vector2(42f, 0f), new Vector2(-12f, -6f));
            var stars = UiKit.Text("Stars", rt, "", 28, RarityColors[3], TextAnchor.LowerLeft);
            UiKit.Anchor(stars.rectTransform, Vector2.zero, Vector2.one, new Vector2(42f, 14f), new Vector2(-12f, 0f));
            var state = UiKit.Text("State", rt, "", 26, UiKit.Muted, TextAnchor.LowerRight);
            UiKit.Anchor(state.rectTransform, Vector2.zero, Vector2.one, new Vector2(42f, 14f), new Vector2(-18f, 0f));

            return new HeroCard { Id = id, Bg = bg, Strip = strip, Name = name, Meta = meta, Stars = stars, State = state };
        }

        private ResultCard CreateResultCard(RectTransform parent, int index)
        {
            int col = index % 5, row = index / 5;
            var bg = UiKit.Image("Result" + index, parent, UiKit.PanelLight, rounded: true);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2((col + 0.5f) / 5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(180f, 250f);
            rt.anchoredPosition = new Vector2(0f, -480f - row * 280f);

            var name = UiKit.Text("Name", rt, "", 32, Color.white, TextAnchor.UpperCenter, true);
            UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, -30f));
            var rarity = UiKit.Text("Rarity", rt, "", 26, Color.white, TextAnchor.MiddleCenter, true);
            UiKit.Stretch(rarity.rectTransform);
            var tag = UiKit.Text("Tag", rt, "", 24, Color.white, TextAnchor.LowerCenter);
            UiKit.Anchor(tag.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 20f), new Vector2(-4f, 0f));
            bg.gameObject.SetActive(false);
            return new ResultCard { Rect = rt, Bg = bg, Name = name, Rarity = rarity, Tag = tag };
        }

        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c) ? c : fallback;
        }
    }
}

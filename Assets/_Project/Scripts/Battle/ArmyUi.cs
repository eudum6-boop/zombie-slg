using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Domain;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Troop screen: one card per troop type with training (×1/×5/×10) and healing of the wounded,
    /// plus both queues. View only; actions go out through callbacks to GameFlow.
    /// </summary>
    public sealed class ArmyUi
    {
        public static readonly string[] ZombieNames = { "워커", "러너", "브루트" };

        private static readonly Color[] TypeColors =
        {
            new Color(0.45f, 0.65f, 0.38f),
            new Color(0.85f, 0.72f, 0.45f),
            new Color(0.6f, 0.66f, 0.75f),
        };

        private static readonly int[] TrainAmounts = { 1, 5, 10 };

        private readonly BattleContext _ctx;
        private readonly Action<TroopType, int> _onTrain;
        private readonly Action<TroopType, int> _onHeal;
        private readonly GameObject _screen;
        private readonly Text _summary;
        private readonly Text _queues;
        private readonly List<Card> _cards = new List<Card>();

        private sealed class Card
        {
            public TroopType Type;
            public Text Count;
            public Text Cost;
            public readonly Button[] Train = new Button[3];
            public readonly Text[] TrainLabels = new Text[3];
            public Button Heal;
            public Text HealLabel;
        }

        public bool IsOpen => _screen.activeSelf;

        public ArmyUi(BattleContext ctx, RectTransform root, Action<TroopType, int> onTrain, Action<TroopType, int> onHeal)
        {
            _ctx = ctx;
            _onTrain = onTrain;
            _onHeal = onHeal;

            var (screen, box) = HeroUi.Modal(root, "ArmyScreen", "병력");
            _screen = screen;

            _summary = UiKit.Text("Summary", box, "", 32, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(_summary.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -200f), new Vector2(-40f, -125f));

            for (int i = 0; i < TroopRules.TypeCount; i++) _cards.Add(CreateCard(box, (TroopType)i, i));

            var queueBg = UiKit.Image("Queues", box, UiKit.PanelLight, rounded: true);
            UiKit.Anchor(queueBg.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 330f));
            _queues = UiKit.Text("Text", queueBg.rectTransform, "", 30, Color.white, TextAnchor.UpperLeft);
            _queues.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_queues.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 20f), new Vector2(-30f, -20f));

            _screen.SetActive(false);
        }

        private Card CreateCard(RectTransform box, TroopType type, int index)
        {
            var def = _ctx.Data.Troop(type);
            var card = new Card { Type = type };
            const float height = 400f;
            float top = -215f - index * (height + 14f);

            var bg = UiKit.Image("Troop_" + type, box, UiKit.PanelLight, rounded: true);
            UiKit.Anchor(bg.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, top - height), new Vector2(-30f, top));
            var c = bg.rectTransform;

            var strip = UiKit.Image("Strip", c, TypeColors[index], rounded: true);
            UiKit.Anchor(strip.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(14f, 0f));

            var name = UiKit.Text("Name", c, def.Name, 44, TypeColors[index], TextAnchor.UpperLeft, true);
            UiKit.Anchor(name.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(40f, -76f), new Vector2(0f, -18f));

            card.Count = UiKit.Text("Count", c, "", 36, Color.white, TextAnchor.UpperRight, true);
            UiKit.Anchor(card.Count.rectTransform, new Vector2(0.4f, 1f), Vector2.one, new Vector2(0f, -76f), new Vector2(-30f, -20f));

            var stats = UiKit.Text("Stats", c, StatsText(def), 28, new Color(0.85f, 0.87f, 0.9f), TextAnchor.UpperLeft);
            stats.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(stats.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -180f), new Vector2(-30f, -84f));

            card.Cost = UiKit.Text("Cost", c, "", 28, UiKit.Muted, TextAnchor.UpperLeft);
            UiKit.Anchor(card.Cost.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -240f), new Vector2(-30f, -188f));

            for (int k = 0; k < TrainAmounts.Length; k++)
            {
                int amount = TrainAmounts[k];
                float x0 = k * 0.2f, x1 = x0 + 0.2f;
                card.Train[k] = UiKit.Button("Train" + amount, c, "", 32, new Color(0.25f, 0.45f, 0.3f), () => _onTrain?.Invoke(type, amount), out card.TrainLabels[k]);
                UiKit.Anchor((RectTransform)card.Train[k].transform, new Vector2(x0, 0f), new Vector2(x1, 0f), new Vector2(k == 0 ? 40f : 8f, 30f), new Vector2(-8f, 150f));
            }
            card.Heal = UiKit.Button("Heal", c, "", 30, new Color(0.6f, 0.2f, 0.2f), () => Heal(type), out card.HealLabel);
            UiKit.Anchor((RectTransform)card.Heal.transform, new Vector2(0.6f, 0f), new Vector2(1f, 0f), new Vector2(16f, 30f), new Vector2(-30f, 150f));
            return card;
        }

        private static string StatsText(TroopDef def)
        {
            var strong = new List<string>();
            var weak = new List<string>();
            for (int z = 0; z < ZombieNames.Length; z++)
            {
                float m = def.VsZombie[z];
                if (m > 1.01f) strong.Add($"{ZombieNames[z]} x{m:0.0#}");
                else if (m < 0.99f) weak.Add($"{ZombieNames[z]} x{m:0.0#}");
            }
            string counters = (strong.Count > 0 ? "<color=#8fe08a>강함 " + string.Join(", ", strong) + "</color>  " : "") +
                              (weak.Count > 0 ? "<color=#ff9a80>약함 " + string.Join(", ", weak) + "</color>" : "");
            return $"체력 {def.Hp:0}  ·  공격 {def.Damage:0.#}/{def.FireInterval:0.#}초  ·  사거리 {def.Range:0.#}\n{counters}";
        }

        private void Heal(TroopType type)
        {
            // Heal as many wounded as the call allows; the domain checks resources.
            _onHeal?.Invoke(type, _healCount[(int)type]);
        }

        private readonly int[] _healCount = new int[TroopRules.TypeCount];

        public void Open(BaseState s, double now)
        {
            _screen.SetActive(true);
            Refresh(s, now);
        }

        public void Close() => _screen.SetActive(false);

        public void Refresh(BaseState s, double now)
        {
            int housed = s.TroopsHoused, cap = s.TroopCapacity;
            _summary.text = s.BarracksLevel > 0
                ? $"수용 {housed}/{cap}  ·  병영 Lv.{s.BarracksLevel}  ·  진료소 {(s.ClinicLevel > 0 ? "Lv." + s.ClinicLevel : "<color=#ff9a80>없음</color>")}\n<size=26>밤에는 병력이 북·서·동문에 나뉘어 배치됩니다</size>"
                : "<color=#ff9a80>병영을 건설하면 병력을 훈련할 수 있습니다</color>\n<size=26>밤에는 병력이 북·서·동문에 나뉘어 배치됩니다</size>";

            foreach (var card in _cards)
            {
                var t = card.Type;
                int wounded = s.Wounded(t);
                card.Count.text = wounded > 0 ? $"보유 {s.Healthy(t)}  <color=#ff9a80>부상 {wounded}</color>" : $"보유 {s.Healthy(t)}";
                var def = _ctx.Data.Troop(t);
                card.Cost.text = $"1명당 {CostText(s, def.TrainCost)}  ·  {UiKit.Duration(s.TrainSeconds(t, 1))}";

                for (int k = 0; k < TrainAmounts.Length; k++)
                {
                    int n = TrainAmounts[k];
                    var block = s.CanTrain(t, n);
                    card.Train[k].interactable = block == ArmyBlock.None;
                    card.TrainLabels[k].text = $"훈련 ×{n}";
                }

                // Heal the largest group affordable (all wounded if possible).
                int healN = wounded;
                while (healN > 0 && s.CanHeal(t, healN) == ArmyBlock.NotEnoughResources) healN--;
                _healCount[(int)t] = Math.Max(1, healN);
                bool canHeal = healN > 0 && s.CanHeal(t, healN) == ArmyBlock.None;
                card.Heal.interactable = canHeal;
                card.HealLabel.text = wounded == 0 ? "부상병 없음"
                    : s.ClinicLevel <= 0 ? "진료소 필요"
                    : canHeal ? $"치료 ×{healN}\n<size=24>{CostText(s, s.HealCost(t, healN))}  {UiKit.Duration(s.HealSeconds(t, healN))}</size>"
                    : "자원 부족";
            }

            var sb = new StringBuilder();
            sb.Append("<b>훈련 대기열</b>\n");
            AppendQueue(sb, s.TrainingQueue, now);
            sb.Append("\n<b>치료 대기열</b>\n");
            AppendQueue(sb, s.HealingQueue, now);
            _queues.text = sb.ToString();
        }

        private void AppendQueue(StringBuilder sb, IReadOnlyList<QueueItem> queue, double now)
        {
            if (queue.Count == 0) { sb.Append("<color=#9aa4b0>비어 있음</color>\n"); return; }
            int shown = 0;
            foreach (var q in queue)
            {
                if (shown++ >= 3) { sb.Append($"<color=#9aa4b0>… 외 {queue.Count - 3}건</color>\n"); break; }
                sb.Append($"{_ctx.Data.Troop(q.Type).Name} {q.Count}명  <color=#ffd070>{UiKit.Duration(Math.Max(0, q.End - now))}</color>\n");
            }
        }

        internal static string CostText(BaseState s, Cost c)
        {
            var parts = new List<string>();
            string[] names = { "식량", "연료", "자재" };
            for (int i = 0; i < 3; i++)
            {
                var r = (ResourceType)i;
                int need = c.Get(r);
                if (need <= 0) continue;
                bool ok = s.GetAmount(r) >= need;
                parts.Add(ok ? $"{names[i]} {need:N0}" : $"<color=#ff6a5a>{names[i]} {need:N0}</color>");
            }
            return parts.Count == 0 ? "무료" : string.Join(" ", parts);
        }
    }
}

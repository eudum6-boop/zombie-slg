using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZombieSLG.Application;

namespace ZombieSLG.Battle
{
    /// <summary>"Longest survival" ranking screen: top 20 and the player's own rank. View only.</summary>
    public sealed class RankingUi
    {
        private const int Rows = 20;
        private const float RowHeight = 60f;
        private static readonly Color MeColor = new Color(1f, 0.75f, 0.3f);
        private static readonly Color[] MedalColors =
        {
            new Color(1f, 0.82f, 0.3f),
            new Color(0.82f, 0.85f, 0.9f),
            new Color(0.85f, 0.55f, 0.3f),
        };

        private readonly GameObject _screen;
        private readonly Text _status;
        private readonly Text _mine;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public Image Bg;
            public Text Rank;
            public Text Name;
            public Text Score;
        }

        public bool IsOpen => _screen.activeSelf;

        public RankingUi(RectTransform root)
        {
            var (screen, box) = HeroUi.Modal(root, "RankingScreen", "순위");
            _screen = screen;

            var sub = UiKit.Text("Sub", box, "가장 오래 버틴 밤", 32, UiKit.Muted, TextAnchor.MiddleLeft);
            UiKit.Anchor(sub.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(40f, -190f), new Vector2(-40f, -130f));

            for (int i = 0; i < Rows; i++)
            {
                float top = -210f - i * (RowHeight + 6f);
                var bg = UiKit.Image("Row" + i, box, UiKit.PanelLight, rounded: true);
                UiKit.Anchor(bg.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(30f, top - RowHeight), new Vector2(-30f, top));
                var rank = UiKit.Text("Rank", bg.rectTransform, "", 32, Color.white, TextAnchor.MiddleCenter, true);
                UiKit.Anchor(rank.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(110f, 0f));
                var name = UiKit.Text("Name", bg.rectTransform, "", 32, Color.white, TextAnchor.MiddleLeft);
                UiKit.Anchor(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(120f, 0f), new Vector2(-260f, 0f));
                var score = UiKit.Text("Score", bg.rectTransform, "", 32, Color.white, TextAnchor.MiddleRight, true);
                UiKit.Anchor(score.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-250f, 0f), new Vector2(-24f, 0f));
                _rows.Add(new Row { Bg = bg, Rank = rank, Name = name, Score = score });
            }

            _status = UiKit.Text("Status", box, "", 36, UiKit.Muted, TextAnchor.MiddleCenter);
            UiKit.Anchor(_status.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(40f, -60f), new Vector2(-40f, 60f));

            var mineBg = UiKit.Image("Mine", box, new Color(0.25f, 0.18f, 0.08f, 0.95f), rounded: true);
            UiKit.Anchor(mineBg.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 150f));
            _mine = UiKit.Text("Text", mineBg.rectTransform, "", 36, MeColor, TextAnchor.MiddleCenter, true);
            UiKit.Stretch(_mine.rectTransform);

            _screen.SetActive(false);
        }

        public void OpenLoading()
        {
            _screen.SetActive(true);
            foreach (var r in _rows) r.Bg.gameObject.SetActive(false);
            _status.text = "불러오는 중…";
            _mine.text = "";
        }

        public void Close() => _screen.SetActive(false);

        /// <summary>page == null means offline or error.</summary>
        public void Show(RankPage page, int localBest)
        {
            if (page == null)
            {
                foreach (var r in _rows) r.Bg.gameObject.SetActive(false);
                _status.text = "서버에 연결되지 않아 순위를 볼 수 없습니다";
                _mine.text = localBest > 0 ? $"내 기록  NIGHT {localBest}" : "";
                return;
            }

            _status.text = page.Top.Count == 0 ? "아직 기록이 없습니다.\n밤을 버텨서 첫 번째로 이름을 올리세요!" : "";
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool show = i < page.Top.Count;
                row.Bg.gameObject.SetActive(show);
                if (!show) continue;
                var e = page.Top[i];
                row.Rank.text = e.Rank.ToString();
                row.Rank.color = e.Rank <= 3 ? MedalColors[e.Rank - 1] : Color.white;
                row.Name.text = e.IsMe ? e.Name + "  <size=26>(나)</size>" : e.Name;
                row.Name.color = e.IsMe ? MeColor : Color.white;
                row.Score.text = $"NIGHT {e.Score}";
                row.Bg.color = e.IsMe ? Color.Lerp(UiKit.PanelLight, MeColor, 0.25f) : UiKit.PanelLight;
            }

            if (page.Me != null) _mine.text = $"내 순위  {page.Me.Rank}위   ·   NIGHT {page.Me.Score}";
            else _mine.text = localBest > 0 ? $"내 기록 NIGHT {localBest} — 등록 중…" : "밤을 버티면 순위에 올라갑니다";
        }
    }
}

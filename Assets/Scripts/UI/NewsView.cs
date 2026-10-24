using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>오른쪽 아래 뉴스 · 공시 피드. 하루 10개 뉴스 진행도도 함께 표시.</summary>
    public class NewsView : MonoBehaviour
    {
        const float RowH = 62;

        class Row
        {
            public UIButton btn;
            public TextMeshProUGUI meta, head;
            public Image flash;
            public NewsItem item;
        }

        GameManager gm;
        RectTransform listRoot;
        readonly List<Row> rows = new List<Row>();
        Image liveDot;
        TextMeshProUGUI counter;
        NewsItem lastTop;

        public void Build(RectTransform area, GameManager gm)
        {
            this.gm = gm;
            counter = UI.Txt(area, "", 14, Theme.Text, true);
            counter.rectTransform.At(18, 0, 200, 44);
            var all = UI.Button(area, "전체 보기  ›", 12, () => gm.OpenNews(null),
                Theme.A(Theme.PanelHi, 0), Theme.PanelHi, Theme.PanelHi2, Theme.Sub, true);
            all.rectTransform().AtRight(80, 8, 96, 28);
            liveDot = UI.Img("Live", area, Theme.Live);
            liveDot.rectTransform.AtRight(58, 19, 6, 6);
            UI.Txt(area, "LIVE", 11, Theme.Live, true).rectTransform.AtRight(18, 13, 36, 18);
            UI.HLine(area, 44);
            listRoot = UI.Rect("List", area).Fill(0, 0, 45, 0);
            for (int i = 0; i < 12; i++) rows.Add(MakeRow(i));
        }

        Row MakeRow(int i)
        {
            var r = new Row();
            r.btn = UI.Button(listRoot, "", 1, null, Theme.A(Theme.PanelHi, 0), Theme.A(Theme.PanelHi, 0.7f), Theme.PanelHi2, Theme.Text);
            var rt = r.btn.rectTransform();
            rt.Top(i * RowH, RowH);
            Destroy(r.btn.label.gameObject);
            r.btn.label = null;
            r.flash = UI.Img("Flash", rt, Theme.A(Theme.Accent, 0));
            r.flash.rectTransform.Fill();
            r.meta = UI.Txt(rt, "", 11, Theme.Dim);
            r.meta.rectTransform.Top(9, 16, 18, 18);
            r.head = UI.Txt(rt, "", 13, Theme.Text);
            r.head.textWrappingMode = TextWrappingModes.Normal;
            r.head.alignment = TextAlignmentOptions.TopLeft;
            r.head.maxVisibleLines = 2;
            r.head.lineSpacing = 4;
            r.head.rectTransform.Top(27, 34, 18, 18);
            UI.Img("Sep", rt, Theme.LineSoft).rectTransform.Bottom(0, 1, 18, 18);
            r.btn.onClick = () => { if (r.item != null) gm.OpenNews(r.item); };
            return r;
        }

        public static string ToneLabel(NewsTone t, out Color c)
        {
            switch (t)
            {
                case NewsTone.Good: c = Theme.Up; return "호재";
                case NewsTone.Bad: c = Theme.Down; return "악재";
                case NewsTone.Macro: c = Theme.Accent; return "거시";
                case NewsTone.Notice: c = Theme.Liq; return "공시";
                default: c = Theme.Sub; return "시사";
            }
        }

        public void Refresh()
        {
            var news = gm.engine.news;
            counter.text = "뉴스 · 공시" + Fmt.Colored("   오늘 " + gm.engine.NewsFiredToday + "/" + MarketEngine.NewsPerDay, Theme.Dim);
            float h = listRoot.rect.height;
            int fit = Mathf.Clamp(Mathf.FloorToInt(h / RowH), 1, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int ni = news.Count - 1 - i;
                bool on = i < fit && ni >= 0;
                r.btn.gameObject.SetActive(on);
                if (!on) { r.item = null; continue; }
                var n = news[ni];
                if (r.item == n) continue;
                r.item = n;
                Color tc;
                string tone = ToneLabel(n.tone, out tc);
                r.meta.text = Fmt.Colored(tone, tc) + "   " + Fmt.Date(n.minute) + " " + Fmt.Clock(n.minute) +
                              (n.asset != null ? "   " + Fmt.Colored(n.asset.ticker, Theme.Sub) : "");
                r.head.text = n.headline;
                r.head.fontStyle = n.breaking ? FontStyles.Bold : FontStyles.Normal;
                r.head.color = n.tone == NewsTone.Info ? Theme.Sub : Theme.Text;
            }
            if (news.Count == 0)
            {
                rows[0].btn.gameObject.SetActive(true);
                rows[0].meta.text = "대기 중";
                rows[0].head.text = "하루에 10개의 뉴스가 발표돼요. 기업 뉴스는 몇 시간 안에 시세에 반드시 반영됩니다.";
            }
            var top = news.Count > 0 ? news[news.Count - 1] : null;
            if (top != null && top != lastTop)
            {
                lastTop = top;
                rows[0].flash.color = Theme.A(Theme.Accent, 0.14f);
            }
        }

        void Update()
        {
            if (rows.Count == 0) return;
            var f = rows[0].flash;
            if (f.color.a > 0) f.color = Theme.A(Theme.Accent, Mathf.Max(0, f.color.a - Time.unscaledDeltaTime * 0.06f));
            liveDot.color = Theme.A(Theme.Live, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f)));
        }
    }
}

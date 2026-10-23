using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 왼쪽 관심종목: 국내 50 / 해외 50 / 코인 100 / 보유 탭.
    /// 200개 행을 다 만들지 않고, 보이는 만큼만 재사용하는 가상 스크롤 리스트.
    /// </summary>
    public class WatchlistView : MonoBehaviour
    {
        const float W = 330, RowH = 52, ListTop = 82, MarketH = 150;
        const int Pool = 20;
        static readonly string[] SortNames = { "기본순", "상승률순", "하락률순" };

        class Row
        {
            public UIButton btn;
            public RectTransform rt;
            public Image marker;
            public TextMeshProUGUI name, ticker, price, change, badge;
            public Sparkline spark;
            public MarketAsset asset;
            public double lastPrice;
            public float flash;
        }

        GameManager gm;
        TabBar tabs;
        ScrollRect scroll;
        RectTransform content, viewport;
        Image scrollThumb;
        readonly List<Row> rows = new List<Row>();
        readonly List<MarketAsset> items = new List<MarketAsset>();
        UIButton sortBtn;
        TextMeshProUGUI countT;
        readonly TextMeshProUGUI[] idxV = new TextMeshProUGUI[3], idxC = new TextMeshProUGUI[3];
        int tab, sort;
        int firstShown = -1;

        public RectTransform Root { get; private set; }

        public void Build(RectTransform area, GameManager gm)
        {
            this.gm = gm;
            Root = area;
            tabs = new TabBar(area, new[] { "국내", "해외", "코인", "보유" }, 0, 0, W, 44, 14, i =>
            {
                tab = i;
                scroll.verticalNormalizedPosition = 1;
                Rebind();
            });
            UI.HLine(area, 44);

            UI.Txt(area, "정렬", 12, Theme.Dim).rectTransform.At(18, 50, 40, 28);
            sortBtn = UI.SolidButton(area, SortNames[0], 12, () =>
            {
                sort = (sort + 1) % SortNames.Length;
                sortBtn.SetLabel(SortNames[sort] + "  <size=70%>▼</size>");
                Rebind();
            });
            sortBtn.rectTransform().At(52, 51, 100, 26);
            sortBtn.SetLabel(SortNames[0] + "  <size=70%>▼</size>");
            countT = UI.Txt(area, "", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            countT.rectTransform.AtRight(18, 50, 150, 28);
            UI.HLine(area, ListTop - 1, 0, 0, Theme.LineSoft);

            // 가상 스크롤
            viewport = UI.Rect("Viewport", area).Fill(0, 0, ListTop, MarketH);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = Color.clear;
            content = UI.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 100);
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.onValueChanged.AddListener(_ => Layout());

            var track = UI.Img("ScrollTrack", area, Color.clear);
            track.rectTransform.Right(2, 3, ListTop + 4, MarketH + 4);
            scrollThumb = UI.Img("Thumb", track.transform, Theme.PanelHi2);
            scrollThumb.rectTransform.anchorMin = new Vector2(0, 1);
            scrollThumb.rectTransform.anchorMax = new Vector2(1, 1);
            scrollThumb.rectTransform.pivot = new Vector2(0.5f, 1);

            for (int i = 0; i < Pool; i++) rows.Add(MakeRow());

            // 시장 지수
            var mk = UI.Rect("Market", area).Bottom(0, MarketH);
            UI.HLine(mk, 0);
            UI.Txt(mk, "시장 지수", 12, Theme.Dim, true).rectTransform.At(18, 12, 200, 18);
            string[] names = { "BOD 국내지수", "GLX 해외지수", "크립토 지수" };
            for (int i = 0; i < 3; i++)
            {
                UI.Txt(mk, names[i], 13, Theme.Sub).rectTransform.At(18, 36 + i * 24, 160, 20);
                idxV[i] = UI.Txt(mk, "", 14, Theme.Text, true, TextAlignmentOptions.MidlineRight);
                idxV[i].rectTransform.AtRight(92, 36 + i * 24, 120, 20);
                idxC[i] = UI.Txt(mk, "", 12, Theme.Sub, true, TextAlignmentOptions.MidlineRight);
                idxC[i].rectTransform.AtRight(18, 36 + i * 24, 72, 20);
            }
            var keys = UI.Txt(mk,
                Fmt.Colored("Space", Theme.Sub) + " 일시정지  " + Fmt.Colored("1~3", Theme.Sub) + " 배속  " +
                Fmt.Colored("↑↓", Theme.Sub) + " 종목  " + Fmt.Colored("Tab", Theme.Sub) + " 탭  " + Fmt.Colored("Esc", Theme.Sub) + " 메뉴",
                11, Theme.Dim);
            keys.rectTransform.At(18, 116, 300, 20);

            Rebind();
        }

        Row MakeRow()
        {
            var r = new Row();
            r.btn = UI.Button(content, "", 1, null, Theme.A(Theme.PanelHi, 0), Theme.A(Theme.PanelHi, 0.7f), Theme.PanelHi2, Theme.Text);
            r.rt = r.btn.rectTransform();
            r.rt.anchorMin = new Vector2(0, 1); r.rt.anchorMax = new Vector2(1, 1);
            r.rt.pivot = new Vector2(0.5f, 1);
            r.rt.sizeDelta = new Vector2(0, RowH);
            Destroy(r.btn.label.gameObject);
            r.btn.label = null;
            var t = r.btn.transform;
            r.marker = UI.Img("Marker", t, Theme.Accent);
            r.marker.rectTransform.Left(0, 3, 0, 1);
            r.name = UI.Txt(t, "", 14, Theme.Text, true);
            r.name.rectTransform.At(18, 8, 128, 20);
            r.ticker = UI.Txt(t, "", 11, Theme.Dim);
            r.ticker.rectTransform.At(18, 29, 60, 16);
            r.badge = UI.Txt(t, "", 10, Theme.Accent, true);
            r.badge.rectTransform.At(64, 29, 90, 16);
            var sp = UI.Rect("Spark", t).At(150, 14, 56, 24);
            sp.gameObject.AddComponent<CanvasRenderer>();
            r.spark = sp.gameObject.AddComponent<Sparkline>();
            r.spark.raycastTarget = false;
            r.price = UI.Txt(t, "", 14, Theme.Text, true, TextAlignmentOptions.MidlineRight);
            r.price.rectTransform.AtRight(16, 7, 116, 20);
            r.change = UI.Txt(t, "", 12, Theme.Sub, true, TextAlignmentOptions.MidlineRight);
            r.change.rectTransform.AtRight(16, 29, 116, 16);
            UI.Img("Sep", t, Theme.LineSoft).rectTransform.Bottom(0, 1);
            r.btn.onClick = () => { if (r.asset != null) gm.Select(r.asset); };
            return r;
        }

        public void ShowKind(AssetKind k)
        {
            if (tab == 3) return;   // 보유 탭에서는 그대로
            int want = (int)k;
            if (tab == want) return;
            tab = want;
            tabs.Select(tab, false);
            Rebind();
            ScrollTo(gm.selected);
        }

        public void CycleTab()
        {
            tab = (tab + 1) % 3;
            tabs.Select(tab, false);
            scroll.verticalNormalizedPosition = 1;
            Rebind();
        }

        public void Rebind()
        {
            BuildItems();
            content.sizeDelta = new Vector2(0, Mathf.Max(1, items.Count) * RowH);
            firstShown = -1;
            Layout();
            Refresh();
        }

        void BuildItems()
        {
            items.Clear();
            var pf = gm.portfolio;
            foreach (var a in gm.engine.assets)
            {
                if (tab == 3) { if (pf.Owns(a)) items.Add(a); }
                else if ((int)a.kind == tab) items.Add(a);
            }
            if (sort == 1) items.Sort((x, y) => Key(y).CompareTo(Key(x)));
            else if (sort == 2) items.Sort((x, y) => Key(x).CompareTo(Key(y)));
            // 상장폐지 종목은 맨 아래로
            StableDelistLast();
        }

        void StableDelistLast()
        {
            var live = new List<MarketAsset>();
            var dead = new List<MarketAsset>();
            foreach (var a in items) (a.Delisted ? dead : live).Add(a);
            items.Clear();
            items.AddRange(live);
            items.AddRange(dead);
        }

        static double Key(MarketAsset a) { return a.Delisted ? -999 : a.ChangePct; }

        void Layout()
        {
            float y = content.anchoredPosition.y;
            int first = Mathf.Max(0, Mathf.FloorToInt(y / RowH));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int idx = first + i;
                bool on = idx < items.Count;
                r.btn.gameObject.SetActive(on);
                if (!on) { r.asset = null; continue; }
                r.rt.anchoredPosition = new Vector2(0, -idx * RowH);
                if (r.asset != items[idx]) { r.asset = items[idx]; r.lastPrice = r.asset.price; r.flash = 0; }
            }
            if (first != firstShown) { firstShown = first; Refresh(); }

            float vh = viewport.rect.height, ch = content.sizeDelta.y;
            bool need = ch > vh + 1;
            scrollThumb.enabled = need;
            if (need)
            {
                float trackH = ((RectTransform)scrollThumb.transform.parent).rect.height;
                float th = Mathf.Max(30, trackH * vh / ch);
                float t = Mathf.Clamp01(y / (ch - vh));
                scrollThumb.rectTransform.sizeDelta = new Vector2(0, th);
                scrollThumb.rectTransform.anchoredPosition = new Vector2(0, -t * (trackH - th));
            }
        }

        public void ScrollTo(MarketAsset a)
        {
            int i = items.IndexOf(a);
            if (i < 0) return;
            float vh = viewport.rect.height;
            float y = content.anchoredPosition.y;
            float top = i * RowH, bottom = top + RowH;
            if (top < y) y = top;
            else if (bottom > y + vh) y = bottom - vh;
            y = Mathf.Clamp(y, 0, Mathf.Max(0, content.sizeDelta.y - vh));
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
            Layout();
        }

        public void Refresh()
        {
            if (sort != 0 || tab == 3)
            {
                // 정렬/보유 탭은 값이 바뀌면 순서도 바뀐다
                var before = new List<MarketAsset>(items);
                BuildItems();
                bool same = before.Count == items.Count;
                for (int i = 0; same && i < items.Count; i++) same = before[i] == items[i];
                if (!same)
                {
                    content.sizeDelta = new Vector2(0, Mathf.Max(1, items.Count) * RowH);
                    firstShown = -1;
                    Layout();
                    return;
                }
            }

            var pf = gm.portfolio;
            int total = items.Count, dead = 0;
            foreach (var a in items) if (a.Delisted) dead++;
            countT.text = total + "종목" + (dead > 0 ? Fmt.Colored("  (상폐 " + dead + ")", Theme.Dim) : "");
            tabs.SetLabel(3, "보유" + (CountOwned() > 0 ? Fmt.Colored(" " + CountOwned(), Theme.Accent) : ""));

            foreach (var r in rows)
            {
                var a = r.asset;
                if (a == null || !r.btn.gameObject.activeSelf) continue;
                bool sel = gm.selected == a;
                r.marker.enabled = sel;
                r.btn.normal = sel ? Theme.PanelHi : Theme.A(Theme.PanelHi, 0);
                r.btn.Refresh();
                r.name.text = a.name;
                r.name.color = a.Delisted ? Theme.Dim : Theme.Text;
                r.ticker.text = a.ticker;

                string badge = "";
                Color bc = Theme.Accent;
                if (a.status != ListingStatus.Normal) { badge = a.StatusLabel; bc = a.Delisted ? Theme.Dim : Theme.Liq; }
                else if (pf.Owns(a)) badge = "보유";
                r.badge.text = badge;
                r.badge.color = bc;
                r.badge.rectTransform.anchoredPosition = new Vector2(18 + r.ticker.preferredWidth + 8, -29);

                if (a.Delisted)
                {
                    r.price.text = "상장폐지";
                    r.price.color = Theme.Dim;
                    r.change.text = "";
                    r.spark.values.Clear();
                    r.spark.SetVerticesDirty();
                    continue;
                }

                Color c = Theme.Change(a.Change);
                r.price.text = Fmt.Price(a.price);
                r.change.text = Fmt.Pct(a.ChangePct);
                r.change.color = c;
                if (a.price != r.lastPrice)
                {
                    r.flash = 1f;
                    r.price.color = a.price > r.lastPrice ? Theme.Up : Theme.Down;
                    r.lastPrice = a.price;
                }
                else if (r.flash <= 0) r.price.color = gm.engine.IsOpen(a) ? Theme.Text : Theme.Sub;
                a.GetRecentCloses(32, 5, r.spark.values);
                r.spark.lineColor = c == Theme.Sub ? Theme.Sub : Theme.A(c, 0.9f);
                r.spark.SetVerticesDirty();
            }

            var kinds = new[] { AssetKind.Domestic, AssetKind.Overseas, AssetKind.Coin };
            for (int i = 0; i < 3; i++)
            {
                double ch;
                double v = gm.engine.Index(kinds[i], out ch);
                idxV[i].text = v.ToString("#,0.00");
                idxC[i].text = Fmt.Pct(ch);
                idxC[i].color = Theme.Change(ch);
            }
        }

        int CountOwned()
        {
            int n = 0;
            foreach (var a in gm.engine.assets) if (gm.portfolio.Owns(a)) n++;
            return n;
        }

        void Update()
        {
            foreach (var r in rows)
            {
                if (r.flash <= 0) continue;
                r.flash -= Time.unscaledDeltaTime * 2.5f;
                if (r.flash <= 0 && r.asset != null && !r.asset.Delisted) r.price.color = Theme.Text;
            }
        }

        public void SelectRelative(int dir)
        {
            if (items.Count == 0) return;
            int i = items.IndexOf(gm.selected);
            i = i < 0 ? 0 : Mathf.Clamp(i + dir, 0, items.Count - 1);
            gm.Select(items[i]);
            ScrollTo(items[i]);
        }
    }
}

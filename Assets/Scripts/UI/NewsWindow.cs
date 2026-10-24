using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 뉴스 창: 왼쪽 목록(필터·스크롤) + 오른쪽 기사(제목·관련 종목·본문).
    /// 본문은 내장 AI가 실제 시세를 반영해 쓴다.
    /// </summary>
    public class NewsWindow : MonoBehaviour
    {
        const float W = 1320, H = 800, ListW = 470, RowH = 64, Pad = 32;
        const int Pool = 14;

        class Row
        {
            public UIButton btn;
            public RectTransform rt;
            public Image marker;
            public TextMeshProUGUI meta, head;
            public NewsItem item;
        }

        GameManager gm;
        Action onClose;
        RectTransform overlay, content, viewport, detail, bodyContent;
        ScrollRect scroll, bodyScroll;
        readonly List<Row> rows = new List<Row>();
        readonly List<NewsItem> items = new List<NewsItem>();
        TextMeshProUGUI countT, metaT, headT, bodyT, footT, emptyT;
        TextMeshProUGUI cardName, cardInfo, cardPrice;
        RectTransform card;
        UIButton gotoBtn;
        NewsItem current;
        int filter, firstShown = -1;

        public static NewsWindow Open(Transform root, GameManager gm, NewsItem focus, Action onClose)
        {
            RectTransform ov;
            var c = Modal.Open(root, W, H, out ov);
            ov.SetAsLastSibling();
            var w = ov.gameObject.AddComponent<NewsWindow>();
            w.gm = gm;
            w.onClose = onClose;
            w.overlay = ov;
            w.Build(c);
            w.Rebuild();
            var e = gm.engine.news;
            w.Show(focus ?? (e.Count > 0 ? e[e.Count - 1] : null));
            return w;
        }

        public void Close()
        {
            if (overlay != null) Destroy(overlay.gameObject);
            overlay = null;
            if (onClose != null) onClose();
        }

        // ───────────────────────── 조립 ─────────────────────────

        void Build(RectTransform c)
        {
            var left = UI.Img("List", c, Theme.Panel).rectTransform.Left(0, ListW);
            UI.Img("Line", c, Theme.Line).rectTransform.Left(ListW, 1);

            UI.Txt(left, "뉴스 · 공시", 20, Theme.Text, true).rectTransform.At(24, 22, 300, 28);
            countT = UI.Txt(left, "", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            countT.rectTransform.AtRight(24, 24, 200, 24);
            var seg = new Segmented(left, new[] { "전체", "보유", "호재", "악재", "공시·시장" }, 20, 62, ListW - 40, 32, 12, i =>
            {
                filter = i;
                scroll.verticalNormalizedPosition = 1;
                Rebuild();
            });
            seg.Select(0, false);
            UI.HLine(left, 108);

            viewport = UI.Rect("Viewport", left).Fill(0, 0, 109, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            content = UI.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            scroll.onValueChanged.AddListener(_ => Layout(false));
            for (int i = 0; i < Pool; i++) rows.Add(MakeRow());
            emptyT = UI.Txt(left, "", 13, Theme.Dim, false, TextAlignmentOptions.Center);
            emptyT.rectTransform.Top(200, 24);

            // ── 오른쪽 기사 ──
            detail = UI.Rect("Detail", c).Fill(ListW + 1, 0, 0, 0);
            UI.GhostButton(detail, "×", 22, Close, false).rectTransform().AtRight(16, 16, 44, 44);
            metaT = UI.Txt(detail, "", 13, Theme.Sub);
            metaT.rectTransform.At(Pad + 8, 30, 700, 20);
            headT = UI.Txt(detail, "", 26, Theme.Text, true, TextAlignmentOptions.TopLeft);
            headT.textWrappingMode = TextWrappingModes.Normal;
            headT.lineSpacing = 6;
            headT.rectTransform.At(Pad + 8, 62, W - ListW - Pad * 2 - 40, 80);

            card = UI.Img("AssetCard", detail, Theme.PanelHi).rectTransform;
            card.At(Pad + 8, 158, W - ListW - Pad * 2 - 16, 76);
            UI.Img("Bar", card, Theme.Accent).rectTransform.Left(0, 3);
            cardName = UI.Txt(card, "", 17, Theme.Text, true);
            cardName.rectTransform.At(20, 12, 360, 26);
            cardInfo = UI.Txt(card, "", 12, Theme.Dim);
            cardInfo.rectTransform.At(20, 42, 380, 20);
            cardPrice = UI.Txt(card, "", 14, Theme.Text, true, TextAlignmentOptions.MidlineRight);
            cardPrice.rectTransform.AtRight(170, 12, 360, 52);
            gotoBtn = UI.SolidButton(card, "종목 보기  ›", 13, () =>
            {
                if (current == null || current.asset == null) return;
                gm.Select(current.asset);
                Close();
            });
            gotoBtn.rectTransform().AtRight(16, 18, 140, 40);

            var bodyView = UI.Rect("BodyView", detail).Fill(Pad + 8, Pad, 252, 62);
            bodyView.gameObject.AddComponent<RectMask2D>();
            bodyView.gameObject.AddComponent<Image>().color = Color.clear;
            bodyContent = UI.Rect("BodyContent", bodyView);
            bodyContent.anchorMin = new Vector2(0, 1); bodyContent.anchorMax = new Vector2(1, 1);
            bodyContent.pivot = new Vector2(0.5f, 1);
            bodyT = UI.Txt(bodyContent, "", 16, Theme.Text, false, TextAlignmentOptions.TopLeft);
            bodyT.textWrappingMode = TextWrappingModes.Normal;
            bodyT.lineSpacing = 14;
            bodyT.paragraphSpacing = 10;
            bodyT.rectTransform.Fill();
            bodyScroll = bodyView.gameObject.AddComponent<ScrollRect>();
            bodyScroll.content = bodyContent;
            bodyScroll.viewport = bodyView;
            bodyScroll.horizontal = false;
            bodyScroll.movementType = ScrollRect.MovementType.Clamped;
            bodyScroll.scrollSensitivity = 40;

            UI.HLine(detail, H - 54, Pad + 8, Pad);
            footT = UI.Txt(detail, "", 12, Theme.Dim);
            footT.rectTransform.Bottom(14, 26, Pad + 8, Pad);
        }

        Row MakeRow()
        {
            var r = new Row();
            r.btn = UI.Button(content, "", 1, null, Theme.A(Theme.PanelHi, 0), Theme.A(Theme.PanelHi, 0.8f), Theme.PanelHi2, Theme.Text);
            r.rt = r.btn.rectTransform();
            r.rt.anchorMin = new Vector2(0, 1); r.rt.anchorMax = new Vector2(1, 1);
            r.rt.pivot = new Vector2(0.5f, 1);
            r.rt.sizeDelta = new Vector2(0, RowH);
            Destroy(r.btn.label.gameObject);
            r.btn.label = null;
            r.marker = UI.Img("Marker", r.rt, Theme.Accent);
            r.marker.rectTransform.Left(0, 3, 0, 1);
            r.meta = UI.Txt(r.rt, "", 11, Theme.Dim);
            r.meta.rectTransform.Top(10, 16, 22, 18);
            r.head = UI.Txt(r.rt, "", 14, Theme.Text);
            r.head.rectTransform.Top(30, 22, 22, 18);
            UI.Img("Sep", r.rt, Theme.LineSoft).rectTransform.Bottom(0, 1, 18, 18);
            r.btn.onClick = () => { if (r.item != null) Show(r.item); };
            return r;
        }

        // ───────────────────────── 목록 ─────────────────────────

        bool Pass(NewsItem n)
        {
            switch (filter)
            {
                case 1: return n.asset != null && gm.portfolio.Owns(n.asset);
                case 2: return n.tone == NewsTone.Good;
                case 3: return n.tone == NewsTone.Bad;
                case 4: return n.tone == NewsTone.Notice || n.tone == NewsTone.Macro || n.cat == NewsCat.Listing;
                default: return true;
            }
        }

        public void Rebuild()
        {
            items.Clear();
            var news = gm.engine.news;
            for (int i = news.Count - 1; i >= 0; i--) if (Pass(news[i])) items.Add(news[i]);
            content.sizeDelta = new Vector2(0, Mathf.Max(1, items.Count) * RowH);
            countT.text = items.Count + "건";
            emptyT.gameObject.SetActive(items.Count == 0);
            emptyT.text = news.Count == 0 ? "아직 발표된 뉴스가 없어요." : "이 조건에 맞는 뉴스가 없어요.";
            Layout(true);
        }

        void Layout(bool force)
        {
            float y = content.anchoredPosition.y;
            int first = Mathf.Max(0, Mathf.FloorToInt(y / RowH));
            if (first == firstShown && !force) return;
            firstShown = first;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int idx = first + i;
                bool on = idx < items.Count;
                r.btn.gameObject.SetActive(on);
                if (!on) { r.item = null; continue; }
                r.rt.anchoredPosition = new Vector2(0, -idx * RowH);
                var n = items[idx];
                r.item = n;
                Color tc;
                string tone = NewsView.ToneLabel(n.tone, out tc);
                r.meta.text = Fmt.Colored(tone, tc) + "   " + Fmt.Date(n.minute) + " " + Fmt.Clock(n.minute) +
                              (n.asset != null ? "   " + Fmt.Colored(n.asset.ticker, Theme.Sub) : "");
                r.head.text = n.headline;
                r.head.fontStyle = n.breaking ? FontStyles.Bold : FontStyles.Normal;
                bool sel = n == current;
                r.marker.enabled = sel;
                r.btn.normal = sel ? Theme.PanelHi : Theme.A(Theme.PanelHi, 0);
                r.btn.Refresh();
            }
        }

        // ───────────────────────── 기사 ─────────────────────────

        public void Show(NewsItem n)
        {
            current = n;
            Layout(true);
            if (n == null)
            {
                metaT.text = "";
                headT.text = "아직 발표된 뉴스가 없어요";
                card.gameObject.SetActive(false);
                bodyT.text = "하루에 10개의 뉴스가 발표돼요. 기업 악재는 몇 시간 안에 반드시 떨어지고, 호재는 반드시 올라요.";
                footT.text = "";
                return;
            }
            Color tc;
            string tone = NewsView.ToneLabel(n.tone, out tc);
            metaT.text = Fmt.Colored(tone, tc) + "   " + Fmt.FullDate(n.minute) + " " + Fmt.Clock(n.minute) +
                         "   " + Fmt.Colored("제목 · " + n.source, Theme.Dim);
            headT.text = n.headline;

            var a = n.asset;
            card.gameObject.SetActive(a != null);
            if (a != null) RefreshCard();

            bodyContent.anchoredPosition = Vector2.zero;
            // 내장 AI 본문: 같은 기사는 같은 문장(번호로 시드 고정), 시세 부분만 매번 최신으로
            SetBody(NewsAI.Body(n, gm.engine, new System.Random(n.id * 7919)), NewsAI.SrcInternal);
        }

        void SetBody(string text, string src)
        {
            bodyT.text = text;
            float w = ((RectTransform)bodyContent.parent).rect.width;
            float h = bodyT.GetPreferredValues(text, w, 0).y + 20;
            bodyContent.sizeDelta = new Vector2(0, h);
            footT.text = "본문 · 내장 AI 작성 (뉴스 " + (300 + NewsAI.LearnedCount) + "건 학습)   ·   AI가 쓴 가상의 기사예요. 시세 부분은 열 때마다 최신 가격으로 바뀝니다";
        }

        void RefreshCard()
        {
            var n = current;
            var a = n.asset;
            cardName.text = a.name + Fmt.Colored("  " + a.ticker, Theme.Dim);
            cardInfo.text = a.KindLabel + " · " + a.sector + (a.status != ListingStatus.Normal ? "  ·  " + Fmt.Colored(a.StatusLabel, Theme.Liq) : "");
            if (a.Delisted) { cardPrice.text = Fmt.Colored("상장폐지", Theme.Dim); return; }
            double ch = n.priceAt > 0 ? (a.price / n.priceAt - 1) * 100 : 0;
            cardPrice.text = Fmt.Colored("보도 당시 " + Fmt.Price(n.priceAt), Theme.Sub) + "   →   " + Fmt.Price(a.price) + "\n" +
                             Fmt.Colored("보도 이후 " + Fmt.Pct(ch), Theme.Change(ch));
        }

        float refreshT;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            refreshT += Time.unscaledDeltaTime;
            if (refreshT > 1f && current != null && current.asset != null)
            {
                refreshT = 0;
                RefreshCard();
            }
        }
    }
}

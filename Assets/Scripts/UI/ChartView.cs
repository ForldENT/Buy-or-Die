using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>캔들 + 거래량 + 가격선(현재가/평단/진입가/청산가)을 한 메시로 그린다.</summary>
    public class CandleChartGraphic : MaskableGraphic
    {
        public struct HLine { public double price; public Color color; public bool dashed; }

        public readonly List<Candle> data = new List<Candle>();
        public readonly List<double> gridPrices = new List<double>();
        public readonly List<HLine> lines = new List<HLine>();
        public Rect plot;          // 로컬 좌표(피벗 좌하단)
        public double min, max, volMax;
        public float slot = 9f;
        public float volumeFrac = 0.17f;
        public float alpha = 1f;

        public float PriceTop { get { return plot.yMax; } }
        public float PriceBottom { get { return plot.yMin + plot.height * volumeFrac + 10; } }

        public float Y(double price)
        {
            double t = (price - min) / Math.Max(1e-12, max - min);
            return PriceBottom + (float)t * (PriceTop - PriceBottom);
        }

        public double PriceAt(float y)
        {
            double t = (y - PriceBottom) / Math.Max(1f, PriceTop - PriceBottom);
            return min + t * (max - min);
        }

        public float X(int i) { return plot.xMax - (data.Count - i - 0.5f) * slot; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 grid = Theme.A(Theme.LineSoft, alpha);
            foreach (var gp in gridPrices)
            {
                float y = Mathf.Round(Y(gp)) + 0.5f;
                UIDraw.Box(vh, plot.xMin, y - 0.5f, plot.xMax, y + 0.5f, grid);
            }
            // 거래량 영역 구분선
            float vTop = plot.yMin + plot.height * volumeFrac;
            UIDraw.Box(vh, plot.xMin, vTop, plot.xMax, vTop + 1, grid);

            int n = data.Count;
            if (n == 0) return;
            float body = Mathf.Max(1f, Mathf.Floor(slot * 0.68f));
            Color32 up = Theme.A(Theme.Up, alpha), down = Theme.A(Theme.Down, alpha);
            Color32 upV = Theme.A(Theme.Up, 0.32f * alpha), downV = Theme.A(Theme.Down, 0.32f * alpha);

            for (int i = 0; i < n; i++)
            {
                var c = data[i];
                float cx = Mathf.Round(X(i));
                bool isUp = c.close >= c.open;
                Color32 col = isUp ? up : down;

                float yH = Y(c.high), yL = Y(c.low);
                UIDraw.Box(vh, cx - 0.5f, yL, cx + 0.5f, yH, col);

                float yO = Y(c.open), yC = Y(c.close);
                float b0 = Mathf.Min(yO, yC), b1 = Mathf.Max(yO, yC);
                if (b1 - b0 < 1f) b1 = b0 + 1f;
                UIDraw.Box(vh, cx - body * 0.5f, b0, cx + body * 0.5f, b1, col);

                if (volMax > 0)
                {
                    float vh0 = plot.yMin;
                    float vh1 = plot.yMin + (float)(c.volume / volMax) * (plot.height * volumeFrac - 4);
                    UIDraw.Box(vh, cx - body * 0.5f, vh0, cx + body * 0.5f, Mathf.Max(vh0 + 1, vh1), isUp ? upV : downV);
                }
            }

            foreach (var l in lines)
            {
                if (l.price < min || l.price > max) continue;
                float y = Mathf.Round(Y(l.price)) + 0.5f;
                Color32 lc = l.color;
                if (l.dashed) UIDraw.DashedH(vh, plot.xMin, plot.xMax, y, 4, 4, 1, lc);
                else UIDraw.Box(vh, plot.xMin, y - 0.5f, plot.xMax, y + 0.5f, lc);
            }
        }

        public void Redraw() { SetVerticesDirty(); }
    }

    /// <summary>선택 종목의 헤더(가격) + 캔들 차트 + 축 라벨 + 마우스 크로스헤어.</summary>
    public class ChartView : MonoBehaviour
    {
        const float PadL = 14, PadR = 124, PadT = 34, PadB = 28;
        static readonly int[] Timeframes = { 1, 5, 15, 60 };

        GameManager gm;
        TextMeshProUGUI nameT, metaT, priceT, changeT, readoutT, closedT;
        CandleChartGraphic chart;
        RectTransform chartRT;
        readonly List<TextMeshProUGUI> axis = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> times = new List<TextMeshProUGUI>();
        readonly List<Tag> tags = new List<Tag>();
        Image crossV, crossH;
        Tag hoverTag;
        TabBar tfTabs;
        int tf = 5;
        int hoverIndex = -1;

        class Tag
        {
            public Image bg;
            public TextMeshProUGUI text;
        }

        public void Build(RectTransform area, GameManager gm)
        {
            this.gm = gm;

            // ── 헤더 ──
            var header = UI.Rect("Header", area).Top(0, 74);
            nameT = UI.Txt(header, "", 22, Theme.Text, true);
            nameT.rectTransform.At(22, 12, 360, 30);
            metaT = UI.Txt(header, "", 12, Theme.Dim);
            metaT.rectTransform.At(22, 44, 360, 18);
            priceT = UI.Txt(header, "", 30, Theme.Text, true);
            priceT.rectTransform.At(330, 10, 260, 40);
            changeT = UI.Txt(header, "", 14, Theme.Sub, true);
            changeT.rectTransform.At(330, 48, 300, 20);

            tfTabs = new TabBar(header, new[] { "1분", "5분", "15분", "1시간" }, 0, 0, 280, 34, 13, i =>
            {
                tf = Timeframes[i];
                Refresh();
            });
            foreach (var b in tfTabs.buttons)
            {
                var rt = b.rectTransform();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-22 - 70 * (3 - tfTabs.buttons.IndexOf(b)), -20);
                rt.sizeDelta = new Vector2(70, 34);
            }
            tfTabs.Select(1, false);
            UI.HLine(area, 74);

            // ── 차트 ──
            var holder = UI.Rect("ChartArea", area).Fill(0, 0, 75, 0);
            var cg = UI.Rect("Chart", holder).Fill();
            chartRT = cg;
            chartRT.pivot = Vector2.zero;
            cg.gameObject.AddComponent<CanvasRenderer>();
            chart = cg.gameObject.AddComponent<CandleChartGraphic>();
            chart.raycastTarget = false;

            for (int i = 0; i < 8; i++)
            {
                var t = UI.Txt(chartRT, "", 11, Theme.Dim, false, TextAlignmentOptions.MidlineLeft, "Axis");
                Pivot(t.rectTransform, 0, 0.5f, 84, 16);
                axis.Add(t);
            }
            for (int i = 0; i < 8; i++)
            {
                var t = UI.Txt(chartRT, "", 11, Theme.Dim, false, TextAlignmentOptions.Center, "Time");
                Pivot(t.rectTransform, 0.5f, 0.5f, 70, 16);
                times.Add(t);
            }

            crossV = UI.Img("CrossV", chartRT, Theme.A(Theme.Sub, 0.35f));
            Pivot(crossV.rectTransform, 0.5f, 0, 1, 10);
            crossH = UI.Img("CrossH", chartRT, Theme.A(Theme.Sub, 0.35f));
            Pivot(crossH.rectTransform, 0, 0.5f, 10, 1);

            for (int i = 0; i < 6; i++) tags.Add(MakeTag());
            hoverTag = MakeTag();

            readoutT = UI.Txt(chartRT, "", 12, Theme.Sub);
            readoutT.rectTransform.At(PadL + 6, 8, 900, 20);

            closedT = UI.Txt(chartRT, "", 13, Theme.Sub, true, TextAlignmentOptions.Center);
            closedT.rectTransform.Center(760, 30, -PadR / 2, 40);
        }

        Tag MakeTag()
        {
            var bg = UI.Img("Tag", chartRT, Theme.Sub);
            Pivot(bg.rectTransform, 0, 0.5f, PadR - 6, 20);
            var t = UI.Txt(bg.transform, "", 11, Color.white, true, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.Fill(6, 4, 0, 0);
            var tag = new Tag { bg = bg, text = t };
            bg.gameObject.SetActive(false);
            return tag;
        }

        static void Pivot(RectTransform rt, float px, float py, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(px, py);
            rt.sizeDelta = new Vector2(w, h);
        }

        public void Refresh()
        {
            var a = gm.selected;
            if (a == null || chart == null) return;

            // 헤더
            nameT.text = a.name;
            metaT.text = a.ticker + "  ·  " + a.KindLabel + " · " + a.sector + "  ·  최대 x" + a.maxLeverage +
                (a.status != ListingStatus.Normal ? "  ·  " + Fmt.Colored(a.StatusLabel, a.Delisted ? Theme.Dim : Theme.Accent) : "");
            float nameW = Mathf.Max(nameT.preferredWidth, metaT.preferredWidth);
            float px = 22 + Mathf.Max(170, nameW + 34);
            priceT.rectTransform.anchoredPosition = new Vector2(px, -10);
            changeT.rectTransform.anchoredPosition = new Vector2(px, -48);
            Color cc = Theme.Change(a.Change);
            priceT.text = Fmt.Price(a.price);
            priceT.color = cc;
            string arrow = a.Change > 0 ? "▲ " : (a.Change < 0 ? "▼ " : "");
            changeT.text = arrow + Fmt.Price(Math.Abs(a.Change)) + "   " + Fmt.Pct(a.ChangePct) + Fmt.Colored("   전일 " + Fmt.Price(a.prevClose), Theme.Dim);
            changeT.color = cc;

            // 플롯 영역
            Rect r = chartRT.rect;
            if (r.width < 50 || r.height < 50) return;
            var plot = new Rect(PadL, PadB, r.width - PadL - PadR, r.height - PadT - PadB);
            chart.plot = plot;
            int count = Mathf.Max(10, Mathf.FloorToInt(plot.width / chart.slot) - 1);
            a.GetCandles(tf, count, chart.data);

            double mn = double.MaxValue, mx = double.MinValue, vm = 0;
            foreach (var c in chart.data)
            {
                if (c.low < mn) mn = c.low;
                if (c.high > mx) mx = c.high;
                if (c.volume > vm) vm = c.volume;
            }
            if (chart.data.Count == 0) { mn = a.price * 0.99; mx = a.price * 1.01; }
            double pad = Math.Max((mx - mn) * 0.08, a.price * 0.002);
            mn -= pad; mx += pad;
            chart.min = mn; chart.max = mx; chart.volMax = vm;

            // 그리드
            chart.gridPrices.Clear();
            double step = NiceStep((mx - mn) / 5.0);
            double g0 = Math.Ceiling(mn / step) * step;
            for (double g = g0; g <= mx && chart.gridPrices.Count < axis.Count; g += step) chart.gridPrices.Add(g);
            for (int i = 0; i < axis.Count; i++)
            {
                bool on = i < chart.gridPrices.Count;
                axis[i].gameObject.SetActive(on);
                if (!on) continue;
                axis[i].text = Fmt.Price(chart.gridPrices[i]);
                axis[i].rectTransform.anchoredPosition = new Vector2(plot.xMax + 10, chart.Y(chart.gridPrices[i]));
            }

            // 시간 라벨
            int n = chart.data.Count;
            int every = Mathf.Max(1, Mathf.CeilToInt(n / 7f));
            int ti = 0;
            for (int i = n - 1; i >= 0 && ti < times.Count; i -= every)
            {
                var t = times[ti++];
                t.gameObject.SetActive(true);
                t.text = TimeLabel(chart.data[i].time);
                t.rectTransform.anchoredPosition = new Vector2(chart.X(i), PadB / 2f);
            }
            for (; ti < times.Count; ti++) times[ti].gameObject.SetActive(false);

            // 가격선 (현재가 / 내 평단 / 포지션 진입가·청산가)
            chart.lines.Clear();
            int tagN = 0;
            var h = gm.portfolio.GetHolding(a);
            if (h != null)
            {
                chart.lines.Add(new CandleChartGraphic.HLine { price = h.avg, color = Theme.A(Theme.Accent, 0.8f), dashed = true });
                SetTag(tagN++, h.avg, "평단 " + Fmt.Price(h.avg), Theme.Accent, Theme.Desktop);
            }
            foreach (var p in gm.portfolio.positions)
            {
                if (p.asset != a || tagN >= tags.Count - 1) continue;
                Color pc = p.isLong ? Theme.Up : Theme.Down;
                chart.lines.Add(new CandleChartGraphic.HLine { price = p.entry, color = Theme.A(pc, 0.55f), dashed = true });
                chart.lines.Add(new CandleChartGraphic.HLine { price = p.liq, color = Theme.A(Theme.Liq, 0.7f), dashed = true });
                SetTag(tagN++, p.entry, (p.isLong ? "롱 " : "숏 ") + Fmt.Price(p.entry), Theme.A(pc, 0.85f), Color.white);
                if (tagN < tags.Count - 1) SetTag(tagN++, p.liq, "청산 " + Fmt.Price(p.liq), Theme.Liq, Theme.Desktop);
            }
            chart.lines.Add(new CandleChartGraphic.HLine { price = a.price, color = Theme.A(cc == Theme.Sub ? Theme.Sub : cc, 0.75f), dashed = true });
            SetTag(tagN++, a.price, Fmt.Price(a.price), cc == Theme.Sub ? Theme.PanelHi2 : cc, Color.white);
            for (; tagN < tags.Count; tagN++) tags[tagN].bg.gameObject.SetActive(false);
            tags[tags.Count - 1].bg.transform.SetAsLastSibling();

            closedT.gameObject.SetActive(!gm.engine.IsOpen(a));
            closedT.text = OrderView.ClosedReason(gm.engine, a, false);

            chart.Redraw();
            UpdateReadout();
        }

        void SetTag(int i, double price, string text, Color bg, Color fg)
        {
            var t = tags[i];
            if (price < chart.min || price > chart.max) { t.bg.gameObject.SetActive(false); return; }
            t.bg.gameObject.SetActive(true);
            t.bg.color = bg;
            t.text.color = fg;
            t.text.text = text;
            t.bg.rectTransform.anchoredPosition = new Vector2(chart.plot.xMax + 4, Mathf.Round(chart.Y(price)));
        }

        string TimeLabel(int minute)
        {
            if (tf >= 60 || minute % 1440 == 0) return Fmt.Date(minute) + " " + Fmt.Clock(minute);
            return Fmt.Clock(minute);
        }

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / exp;
            double nf = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
            return nf * exp;
        }

        void Update()
        {
            if (chart == null || gm == null) return;
            var mouse = Mouse.current;
            int idx = -1;
            Vector2 local = Vector2.zero;
            if (mouse != null && chart.data.Count > 0)
            {
                Vector2 sp = mouse.position.ReadValue();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(chartRT, sp, null, out local) &&
                    chart.plot.Contains(local))
                {
                    int i = chart.data.Count - 1 - Mathf.FloorToInt((chart.plot.xMax - local.x) / chart.slot);
                    if (i >= 0 && i < chart.data.Count) idx = i;
                }
            }

            bool on = idx >= 0;
            crossV.gameObject.SetActive(on);
            crossH.gameObject.SetActive(on && local.y > chart.PriceBottom);
            hoverTag.bg.gameObject.SetActive(on && local.y > chart.PriceBottom);
            if (on)
            {
                float x = Mathf.Round(chart.X(idx));
                crossV.rectTransform.anchoredPosition = new Vector2(x, chart.plot.yMin);
                crossV.rectTransform.sizeDelta = new Vector2(1, chart.plot.height);
                crossH.rectTransform.anchoredPosition = new Vector2(chart.plot.xMin, Mathf.Round(local.y));
                crossH.rectTransform.sizeDelta = new Vector2(chart.plot.width, 1);
                hoverTag.bg.color = Theme.PanelHi2;
                hoverTag.text.color = Theme.Text;
                hoverTag.text.text = Fmt.Price(chart.PriceAt(local.y));
                hoverTag.bg.rectTransform.anchoredPosition = new Vector2(chart.plot.xMax + 4, Mathf.Round(local.y));
                hoverTag.bg.transform.SetAsLastSibling();
            }
            if (idx != hoverIndex)
            {
                hoverIndex = idx;
                UpdateReadout();
            }
        }

        void UpdateReadout()
        {
            if (chart.data.Count == 0) { readoutT.text = ""; return; }
            int i = hoverIndex >= 0 && hoverIndex < chart.data.Count ? hoverIndex : chart.data.Count - 1;
            var c = chart.data[i];
            Color col = c.close >= c.open ? Theme.Up : Theme.Down;
            string sep = "     ";
            readoutT.text =
                Fmt.Colored(Fmt.Date(c.time) + " " + Fmt.Clock(c.time), Theme.Sub) + sep +
                Fmt.Colored("시 ", Theme.Dim) + Fmt.Colored(Fmt.Price(c.open), col) + sep +
                Fmt.Colored("고 ", Theme.Dim) + Fmt.Colored(Fmt.Price(c.high), col) + sep +
                Fmt.Colored("저 ", Theme.Dim) + Fmt.Colored(Fmt.Price(c.low), col) + sep +
                Fmt.Colored("종 ", Theme.Dim) + Fmt.Colored(Fmt.Price(c.close), col) + sep +
                Fmt.Colored("거래량 ", Theme.Dim) + Fmt.Colored(Fmt.Volume(c.volume), Theme.Sub);
        }
    }
}

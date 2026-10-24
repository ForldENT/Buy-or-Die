using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>중앙 하단: 보유 자산 / 레버리지 포지션 / 거래 내역 표.</summary>
    public class PortfolioView : MonoBehaviour
    {
        const float HeaderH = 44, ColH = 28, RowH = 31, PadX = 22, ActionW = 70;
        const int MaxRows = 6;

        struct Col
        {
            public string title; public float weight; public bool right;
            public Col(string t, float w, bool r) { title = t; weight = w; right = r; }
        }

        static readonly Col[] HoldCols =
        {
            new Col("종목", 1.5f, false), new Col("보유 수량", 1.1f, true), new Col("평균 단가", 1.1f, true),
            new Col("현재가", 1.1f, true), new Col("평가 금액", 1.2f, true), new Col("평가 손익", 1.2f, true), new Col("수익률", 0.9f, true)
        };
        static readonly Col[] PosCols =
        {
            new Col("종목", 1.4f, false), new Col("방향", 0.9f, false), new Col("진입가", 1.1f, true), new Col("현재가", 1.1f, true),
            new Col("청산가", 1.1f, true), new Col("증거금", 1.1f, true), new Col("손익", 1.2f, true), new Col("ROE", 0.8f, true)
        };
        static readonly Col[] LogCols =
        {
            new Col("시간", 1.0f, false), new Col("종목", 1.4f, false), new Col("구분", 1.0f, false), new Col("체결가", 1.2f, true),
            new Col("수량", 1.1f, true), new Col("실현 손익", 1.3f, true)
        };

        class Row
        {
            public UIButton bg;
            public readonly List<TextMeshProUGUI> cells = new List<TextMeshProUGUI>();
            public UIButton action;
            public object data;
        }

        GameManager gm;
        RectTransform area;
        TabBar tabs;
        readonly List<TextMeshProUGUI> heads = new List<TextMeshProUGUI>();
        readonly List<Row> rows = new List<Row>();
        TextMeshProUGUI empty, more;
        UIButton closeAll, historyBtn;
        int tab;
        float lastWidth = -1;

        public void Build(RectTransform area, GameManager gm)
        {
            this.gm = gm;
            this.area = area;
            tabs = new TabBar(area, new[] { "보유 자산", "레버리지 포지션", "거래 내역" }, 8, 0, 450, HeaderH, 14, i =>
            {
                tab = i;
                lastWidth = -1;
                Refresh();
            });
            closeAll = UI.Button(area, "전체 포지션 청산", 12, () => gm.CloseAllPositions(),
                Theme.A(Theme.Liq, 0.12f), Theme.A(Theme.Liq, 0.2f), Theme.A(Theme.Liq, 0.28f), Theme.Liq);
            closeAll.rectTransform().AtRight(PadX, 9, 130, 28);
            historyBtn = UI.SolidButton(area, "전체 내역 보기  ›", 12, () => gm.OpenHistory());
            historyBtn.rectTransform().AtRight(PadX, 9, 130, 28);
            more = UI.Txt(area, "", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            more.rectTransform.AtRight(PadX + 140, 9, 200, 28);
            UI.HLine(area, HeaderH);

            for (int i = 0; i < 8; i++)
            {
                var t = UI.Txt(area, "", 12, Theme.Dim);
                heads.Add(t);
            }
            for (int i = 0; i < MaxRows; i++) rows.Add(MakeRow(i));

            empty = UI.Txt(area, "", 13, Theme.Dim, false, TextAlignmentOptions.Center);
            empty.rectTransform.Top(HeaderH + ColH + 40, 30);
        }

        Row MakeRow(int i)
        {
            var r = new Row();
            r.bg = UI.Button(area, "", 1, null, Theme.A(Theme.PanelHi, 0), Theme.A(Theme.PanelHi, 0.8f), Theme.PanelHi2, Theme.Text);
            r.bg.rectTransform().Top(HeaderH + ColH + i * RowH, RowH);
            Destroy(r.bg.label.gameObject);
            r.bg.label = null;
            for (int c = 0; c < 8; c++)
            {
                var t = UI.Txt(r.bg.transform, "", 13, Theme.Text);
                r.cells.Add(t);
            }
            r.action = UI.Button(r.bg.transform, "매도", 12, null, Theme.PanelHi, Theme.PanelHi2, Theme.Line, Theme.Text);
            r.action.rectTransform().AtRight(PadX, 3, ActionW - 8, RowH - 6);
            r.bg.onClick = () =>
            {
                var h = r.data as Holding; if (h != null) { gm.Select(h.asset); return; }
                var p = r.data as Position; if (p != null) { gm.Select(p.asset); return; }
                var l = r.data as TradeLog; if (l != null) gm.Select(l.asset);
            };
            r.action.onClick = () =>
            {
                var h = r.data as Holding; if (h != null) { gm.SellAll(h.asset); return; }
                var p = r.data as Position; if (p != null) gm.ClosePosition(p);
            };
            return r;
        }

        Col[] Cols { get { return tab == 0 ? HoldCols : (tab == 1 ? PosCols : LogCols); } }

        void Layout()
        {
            float width = area.rect.width;
            if (Mathf.Abs(width - lastWidth) < 0.5f) return;
            lastWidth = width;
            var cols = Cols;
            float total = 0;
            foreach (var c in cols) total += c.weight;
            float avail = width - PadX * 2 - (tab == 2 ? 0 : ActionW + 10);
            float x = PadX;
            for (int i = 0; i < 8; i++)
            {
                bool on = i < cols.Length;
                heads[i].gameObject.SetActive(on);
                foreach (var r in rows) r.cells[i].gameObject.SetActive(on);
                if (!on) continue;
                float w = avail * cols[i].weight / total;
                var align = cols[i].right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                heads[i].text = cols[i].title;
                heads[i].alignment = align;
                heads[i].rectTransform.At(x, HeaderH, w - 8, ColH);
                foreach (var r in rows)
                {
                    r.cells[i].alignment = align;
                    r.cells[i].rectTransform.At(x, 0, w - 8, RowH);
                }
                x += w;
            }
        }

        public void Refresh()
        {
            if (area == null) return;
            Layout();
            var pf = gm.portfolio;
            tabs.SetLabel(0, "보유 자산" + (pf.holdingList.Count > 0 ? Fmt.Colored("  " + pf.holdingList.Count, Theme.Sub) : ""));
            tabs.SetLabel(1, "레버리지 포지션" + (pf.positions.Count > 0 ? Fmt.Colored("  " + pf.positions.Count, Theme.Accent) : ""));
            closeAll.gameObject.SetActive(tab == 1 && pf.positions.Count > 0);
            historyBtn.gameObject.SetActive(tab == 2);

            int count = tab == 0 ? pf.holdingList.Count : (tab == 1 ? pf.positions.Count : pf.logs.Count);
            empty.gameObject.SetActive(count == 0);
            empty.text = tab == 0 ? "보유 중인 자산이 없어요.  오른쪽 주문창에서 매수해 보세요."
                       : tab == 1 ? "열린 포지션이 없어요.  레버리지 탭에서 롱(상승) / 숏(하락)에 베팅해 보세요."
                       : "아직 거래 내역이 없어요.";
            more.text = count > MaxRows ? "외 " + (count - MaxRows) + "건" : "";

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool on = i < count;
                r.bg.gameObject.SetActive(on);
                if (!on) { r.data = null; continue; }
                if (tab == 0) FillHolding(r, pf.holdingList[i]);
                else if (tab == 1) FillPosition(r, pf.positions[i]);
                else FillLog(r, pf.logs[pf.logs.Count - 1 - i]);
                bool sel = GetAsset(r.data) == gm.selected;
                r.bg.normal = sel ? Theme.A(Theme.PanelHi, 0.6f) : Theme.A(Theme.PanelHi, 0);
                r.bg.Refresh();
            }
        }

        static MarketAsset GetAsset(object o)
        {
            var h = o as Holding; if (h != null) return h.asset;
            var p = o as Position; if (p != null) return p.asset;
            var l = o as TradeLog; if (l != null) return l.asset;
            return null;
        }

        void FillHolding(Row r, Holding h)
        {
            r.data = h;
            var a = h.asset;
            Color c = Theme.Change(h.PnL);
            Set(r, 0, a.name + Fmt.Colored("  " + a.ticker + (a.status != ListingStatus.Normal ? "  " + a.StatusLabel : ""), a.status != ListingStatus.Normal ? Theme.Accent : Theme.Dim), Theme.Text);
            Set(r, 1, Fmt.Qty(h.qty, a.qtyDecimals), Theme.Text);
            Set(r, 2, Fmt.Price(h.avg), Theme.Sub);
            Set(r, 3, Fmt.Price(a.price), Theme.Change(a.Change));
            Set(r, 4, Fmt.Won(h.Value), Theme.Text);
            Set(r, 5, Fmt.SignedWon(h.PnL), c);
            Set(r, 6, Fmt.Pct(h.PnLPct), c);
            r.action.gameObject.SetActive(true);
            r.action.SetLabel("전량 매도");
            r.action.SetColors(Theme.A(Theme.Down, 0.14f), Theme.A(Theme.Down, 0.22f), Theme.A(Theme.Down, 0.3f), Theme.Down);
            r.action.SetInteractable(gm.engine.IsTradable(a) && gm.state == GameState.Playing);
        }

        void FillPosition(Row r, Position p)
        {
            r.data = p;
            var a = p.asset;
            Color side = p.isLong ? Theme.Up : Theme.Down;
            Color c = Theme.Change(p.PnL);
            Set(r, 0, a.name + Fmt.Colored("  " + a.ticker, Theme.Dim), Theme.Text);
            Set(r, 1, (p.isLong ? "롱 ▲ " : "숏 ▼ ") + Fmt.Colored("x" + p.leverage, Theme.Sub), side);
            Set(r, 2, Fmt.Price(p.entry), Theme.Sub);
            Set(r, 3, Fmt.Price(a.price), Theme.Change(a.Change));
            double dist = Math.Abs(a.price - p.liq) / a.price;
            Set(r, 4, Fmt.Price(p.liq), dist < 0.02 ? Theme.Accent : Theme.Liq);
            Set(r, 5, Fmt.Won(p.margin), Theme.Text);
            Set(r, 6, Fmt.SignedWon(p.PnL), c);
            Set(r, 7, Fmt.Pct(p.Roe), c);
            r.action.gameObject.SetActive(true);
            r.action.SetLabel("청산");
            r.action.SetColors(Theme.PanelHi, Theme.PanelHi2, Theme.Line, Theme.Text);
            r.action.SetInteractable(gm.engine.IsTradable(a) && gm.state == GameState.Playing);
        }

        void FillLog(Row r, TradeLog l)
        {
            r.data = l;
            var a = l.asset;
            Set(r, 0, Fmt.Date(l.minute) + " " + Fmt.Clock(l.minute), Theme.Sub);
            Set(r, 1, a.name, Theme.Text);
            Color ac = (l.action == "강제청산" || l.action == "상장폐지") ? Theme.Liq : (l.positive ? Theme.Up : Theme.Down);
            Set(r, 2, l.action, ac);
            Set(r, 3, Fmt.Price(l.price), Theme.Text);
            Set(r, 4, Fmt.Qty(l.qty, Math.Max(a.qtyDecimals, l.action.StartsWith("롱") || l.action.StartsWith("숏") || l.action == "강제청산" ? 4 : 0)), Theme.Sub);
            Set(r, 5, l.hasPnl ? Fmt.SignedWon(l.pnl) : "—", l.hasPnl ? Theme.Change(l.pnl) : Theme.Dim);
            r.action.gameObject.SetActive(false);
        }

        static void Set(Row r, int i, string s, Color c)
        {
            var t = r.cells[i];
            if (t.text != s) t.text = s;
            t.color = c;
        }

        void Update()
        {
            if (area != null && Mathf.Abs(area.rect.width - lastWidth) > 0.5f) Refresh();
        }
    }
}

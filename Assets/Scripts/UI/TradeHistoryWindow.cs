using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 전체 거래 내역 창. 저장된 거래 기록(최근 1,000건)을 필터·통계와 함께 스크롤로 보여준다.
    /// 행을 누르면 그 종목을 선택하고 창을 닫는다.
    /// </summary>
    public class TradeHistoryWindow : MonoBehaviour
    {
        const float W = 1240, H = 780, PadX = 36, RowH = 34, ListTop = 236;
        const int Pool = 18;

        struct Col
        {
            public string title; public float weight; public bool right;
            public Col(string t, float w, bool r) { title = t; weight = w; right = r; }
        }

        static readonly Col[] Cols =
        {
            new Col("일시", 1.15f, false), new Col("종목", 1.6f, false), new Col("구분", 1.0f, false),
            new Col("체결가", 1.2f, true), new Col("수량", 1.1f, true), new Col("거래금액", 1.3f, true), new Col("실현 손익", 1.3f, true)
        };

        class Row
        {
            public UIButton btn;
            public RectTransform rt;
            public readonly List<TextMeshProUGUI> cells = new List<TextMeshProUGUI>();
            public TradeLog log;
        }

        GameManager gm;
        Action onClose;
        RectTransform overlay, content, viewport;
        ScrollRect scroll;
        readonly List<Row> rows = new List<Row>();
        readonly List<TradeLog> items = new List<TradeLog>();
        TextMeshProUGUI emptyT, countT;
        readonly TextMeshProUGUI[] statV = new TextMeshProUGUI[6];
        int filter;
        int firstShown = -1;
        float[] colX, colW;

        public static TradeHistoryWindow Open(Transform root, GameManager gm, Action onClose)
        {
            RectTransform ov;
            var c = Modal.Open(root, W, H, out ov);
            ov.SetAsLastSibling();
            var w = ov.gameObject.AddComponent<TradeHistoryWindow>();
            w.gm = gm;
            w.onClose = onClose;
            w.overlay = ov;
            w.Build(c);
            return w;
        }

        public void Close()
        {
            if (overlay != null) Destroy(overlay.gameObject);
            overlay = null;
            if (onClose != null) onClose();
        }

        void Build(RectTransform c)
        {
            UI.Txt(c, "거래 내역", 22, Theme.Text, true).rectTransform.At(PadX, 28, 300, 32);
            UI.Txt(c, "최근 " + Portfolio.MaxLogs.ToString("#,0") + "건까지 저장돼요  ·  행을 누르면 그 종목으로 이동", 12, Theme.Dim)
                .rectTransform.At(PadX, 62, 600, 18);
            UI.GhostButton(c, "×", 22, Close, false).rectTransform().AtRight(20, 20, 44, 44);
            UI.HLine(c, 96, PadX, PadX);

            // 통계
            string[] labels = { "총 거래", "실현 손익 합계", "승률 (손익 확정 거래)", "최고 수익", "최대 손실", "강제청산 · 상폐" };
            float sw = (W - PadX * 2) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                UI.Txt(c, labels[i], 12, Theme.Dim).rectTransform.At(PadX + sw * i, 110, sw - 10, 18);
                statV[i] = UI.Txt(c, "", 18, Theme.Text, true);
                statV[i].rectTransform.At(PadX + sw * i, 130, sw - 10, 26);
            }

            // 필터
            var seg = new Segmented(c, new[] { "전체", "매수", "매도", "레버리지", "청산 · 상폐" }, PadX, 172, 560, 34, 13, i =>
            {
                filter = i;
                scroll.verticalNormalizedPosition = 1;
                Rebuild();
            });
            seg.Select(0, false);
            countT = UI.Txt(c, "", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            countT.rectTransform.AtRight(PadX, 172, 300, 34);

            // 열 제목
            float total = 0;
            foreach (var col in Cols) total += col.weight;
            float avail = W - PadX * 2;
            colX = new float[Cols.Length];
            colW = new float[Cols.Length];
            float x = PadX;
            for (int i = 0; i < Cols.Length; i++)
            {
                colX[i] = x;
                colW[i] = avail * Cols[i].weight / total;
                var t = UI.Txt(c, Cols[i].title, 12, Theme.Dim, false, Cols[i].right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                t.rectTransform.At(x, ListTop - 28, colW[i] - 10, 24);
                x += colW[i];
            }
            UI.HLine(c, ListTop - 2, PadX, PadX);

            // 가상 스크롤 리스트
            viewport = UI.Rect("Viewport", c).Fill(PadX - 8, PadX - 8, ListTop, 24);
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
            scroll.onValueChanged.AddListener(_ => Layout());

            for (int i = 0; i < Pool; i++) rows.Add(MakeRow());

            emptyT = UI.Txt(c, "", 14, Theme.Dim, false, TextAlignmentOptions.Center);
            emptyT.rectTransform.Top(ListTop + 120, 30);

            RefreshStats();
            Rebuild();
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
            for (int i = 0; i < Cols.Length; i++)
            {
                var t = UI.Txt(r.btn.transform, "", 13, Theme.Text, false, Cols[i].right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                t.rectTransform.At(colX[i] - (PadX - 8), 0, colW[i] - 10, RowH);
                r.cells.Add(t);
            }
            UI.Img("Sep", r.btn.transform, Theme.LineSoft).rectTransform.Bottom(0, 1, 8, 8);
            r.btn.onClick = () =>
            {
                if (r.log == null) return;
                gm.Select(r.log.asset);
                Close();
            };
            return r;
        }

        static bool IsLeverage(TradeLog l) { return l.action.StartsWith("롱") || l.action.StartsWith("숏"); }
        static bool IsForced(TradeLog l) { return l.action == "강제청산" || l.action == "상장폐지"; }

        bool Pass(TradeLog l)
        {
            switch (filter)
            {
                case 1: return l.action == "매수";
                case 2: return l.action == "매도";
                case 3: return IsLeverage(l);
                case 4: return IsForced(l) || l.action.EndsWith("청산");
                default: return true;
            }
        }

        void Rebuild()
        {
            items.Clear();
            var logs = gm.portfolio.logs;
            for (int i = logs.Count - 1; i >= 0; i--) if (Pass(logs[i])) items.Add(logs[i]);
            content.sizeDelta = new Vector2(0, Mathf.Max(1, items.Count) * RowH);
            countT.text = items.Count.ToString("#,0") + "건";
            emptyT.gameObject.SetActive(items.Count == 0);
            emptyT.text = logs.Count == 0 ? "아직 거래 기록이 없어요." : "이 조건에 맞는 거래가 없어요.";
            firstShown = -1;
            Layout();
        }

        void RefreshStats()
        {
            var logs = gm.portfolio.logs;
            int closed = 0, wins = 0, forced = 0;
            double sum = 0, best = 0, worst = 0;
            foreach (var l in logs)
            {
                if (IsForced(l)) forced++;
                if (!l.hasPnl) continue;
                closed++;
                sum += l.pnl;
                if (l.pnl > 0) wins++;
                if (l.pnl > best) best = l.pnl;
                if (l.pnl < worst) worst = l.pnl;
            }
            statV[0].text = logs.Count.ToString("#,0") + "건";
            statV[1].text = Fmt.SignedWon(sum) + "원";
            statV[1].color = Theme.Change(sum);
            statV[2].text = closed > 0 ? (100.0 * wins / closed).ToString("0.0") + "%" + Fmt.Colored("  " + wins + " / " + closed, Theme.Dim) : "—";
            statV[3].text = best > 0 ? Fmt.SignedWon(best) + "원" : "—";
            statV[3].color = best > 0 ? Theme.Up : Theme.Dim;
            statV[4].text = worst < 0 ? Fmt.SignedWon(worst) + "원" : "—";
            statV[4].color = worst < 0 ? Theme.Down : Theme.Dim;
            statV[5].text = forced + "회";
            statV[5].color = forced > 0 ? Theme.Liq : Theme.Text;
        }

        void Layout()
        {
            float y = content.anchoredPosition.y;
            int first = Mathf.Max(0, Mathf.FloorToInt(y / RowH));
            if (first == firstShown) return;
            firstShown = first;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int idx = first + i;
                bool on = idx < items.Count;
                r.btn.gameObject.SetActive(on);
                if (!on) { r.log = null; continue; }
                r.rt.anchoredPosition = new Vector2(0, -idx * RowH);
                Fill(r, items[idx]);
            }
        }

        void Fill(Row r, TradeLog l)
        {
            r.log = l;
            var a = l.asset;
            bool lev = IsLeverage(l) || l.action == "강제청산";
            Set(r, 0, Fmt.Date(l.minute) + " " + Fmt.Clock(l.minute), Theme.Sub);
            Set(r, 1, a.name + Fmt.Colored("  " + a.ticker, Theme.Dim), a.Delisted ? Theme.Dim : Theme.Text);
            Color ac = IsForced(l) ? Theme.Liq : (l.positive ? Theme.Up : Theme.Down);
            Set(r, 2, l.action, ac);
            Set(r, 3, l.price > 0 ? Fmt.Price(l.price) : "—", Theme.Text);
            Set(r, 4, Fmt.Qty(l.qty, Math.Max(a.qtyDecimals, lev ? 4 : 0)), Theme.Sub);
            Set(r, 5, l.price > 0 ? Fmt.Won(l.price * l.qty) : "—", Theme.Sub);
            Set(r, 6, l.hasPnl ? Fmt.SignedWon(l.pnl) : "—", l.hasPnl ? Theme.Change(l.pnl) : Theme.Dim);
        }

        static void Set(Row r, int i, string s, Color c)
        {
            r.cells[i].text = s;
            r.cells[i].color = c;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }
    }
}

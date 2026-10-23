using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>오른쪽 주문창: 현물(매수/매도) · 레버리지(롱/숏).</summary>
    public class OrderView : MonoBehaviour
    {
        const float W = 372, P = 18;
        static readonly int[] Levs = { 1, 2, 5, 10, 25, 50 };
        static readonly double[] Pcts = { 0.1, 0.25, 0.5, 1.0 };

        GameManager gm;
        TabBar modeTabs;
        RectTransform spotRoot, levRoot;
        TextMeshProUGUI assetName, assetPrice, hint;

        // 현물
        UIButton buySide, sellSide, spotAction;
        TextMeshProUGUI availLabel, availValue, heldValue, qtyValue, amountValue, feeValue;
        readonly List<UIButton> spotPctBtns = new List<UIButton>();
        bool buyMode = true;
        double spotQty;
        int spotPct = -1;

        // 레버리지
        readonly List<UIButton> levBtns = new List<UIButton>();
        readonly List<UIButton> marginBtns = new List<UIButton>();
        TextMeshProUGUI marginValue, notionalValue, liqLongValue, liqShortValue, posInfo;
        UIButton longBtn, shortBtn;
        int lev = 10;
        int marginPct = -1;

        MarketAsset boundAsset;
        string flashMsg;
        Color flashColor;
        float flashUntil;

        public void Build(RectTransform area, GameManager gm)
        {
            this.gm = gm;
            modeTabs = new TabBar(area, new[] { "현물 매매", "레버리지 선물" }, 0, 0, W, 44, 14, i =>
            {
                spotRoot.gameObject.SetActive(i == 0);
                levRoot.gameObject.SetActive(i == 1);
                Refresh();
            });
            UI.HLine(area, 44);

            assetName = UI.Txt(area, "", 15, Theme.Text, true);
            assetName.rectTransform.At(P, 56, 200, 24);
            assetPrice = UI.Txt(area, "", 15, Theme.Text, true, TextAlignmentOptions.MidlineRight);
            assetPrice.rectTransform.AtRight(P, 56, 170, 24);

            spotRoot = UI.Rect("Spot", area).Fill();
            levRoot = UI.Rect("Leverage", area).Fill();
            BuildSpot(spotRoot);
            BuildLeverage(levRoot);
            levRoot.gameObject.SetActive(false);

            hint = UI.Txt(area, "", 12, Theme.Dim, false, TextAlignmentOptions.TopLeft);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.rectTransform.At(P, 428, W - P * 2, 40);
        }

        public void SelectMode(int i) { modeTabs.Select(i, true); }

        // ───────────────────────── 현물 ─────────────────────────

        void BuildSpot(RectTransform r)
        {
            float half = (W - P * 2 - 6) / 2;
            buySide = UI.Button(r, "매수", 14, () => SetSide(true), Theme.PanelHi, Theme.PanelHi2, Theme.Line, Theme.Sub);
            buySide.rectTransform().At(P, 90, half, 38);
            sellSide = UI.Button(r, "매도", 14, () => SetSide(false), Theme.PanelHi, Theme.PanelHi2, Theme.Line, Theme.Sub);
            sellSide.rectTransform().At(P + half + 6, 90, half, 38);

            availLabel = Label(r, "주문 가능", 144);
            availValue = Value(r, 144);
            Label(r, "보유 수량", 170);
            heldValue = Value(r, 170);

            Label(r, "주문 수량", 208);
            qtyValue = Value(r, 204, 20, 26);

            float bw = (W - P * 2 - 5 * 5) / 6f;
            var minus = UI.SolidButton(r, "−", 16, () => Nudge(-1));
            minus.rectTransform().At(P, 244, bw, 34);
            string[] labels = { "10%", "25%", "50%", "최대" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = UI.SolidButton(r, labels[i], 12, () => SetSpotPct(k));
                b.rectTransform().At(P + (bw + 5) * (i + 1), 244, bw, 34);
                spotPctBtns.Add(b);
            }
            var plus = UI.SolidButton(r, "+", 16, () => Nudge(1));
            plus.rectTransform().At(P + (bw + 5) * 5, 244, bw, 34);

            Label(r, "주문 금액", 294);
            amountValue = Value(r, 294);
            Label(r, "수수료 (0.05%)", 320);
            feeValue = Value(r, 320);

            spotAction = UI.Button(r, "매수하기", 17, DoSpot, Theme.Up, Theme.A(Theme.Up, 0.85f), Theme.A(Theme.Up, 0.7f), Color.white);
            spotAction.rectTransform().At(P, 360, W - P * 2, 54);
            spotAction.useDisabledBg = true;
            spotAction.disabledBg = Theme.PanelHi;
            SetSide(true);
        }

        void SetSide(bool buy)
        {
            buyMode = buy;
            spotQty = 0;
            spotPct = -1;
            Color c = buy ? Theme.Up : Theme.Down;
            buySide.SetColors(buy ? Theme.A(Theme.Up, 0.16f) : Theme.PanelHi, buy ? Theme.A(Theme.Up, 0.22f) : Theme.PanelHi2, Theme.Line, buy ? Theme.Up : Theme.Sub);
            sellSide.SetColors(!buy ? Theme.A(Theme.Down, 0.18f) : Theme.PanelHi, !buy ? Theme.A(Theme.Down, 0.24f) : Theme.PanelHi2, Theme.Line, !buy ? Theme.Down : Theme.Sub);
            spotAction.SetColors(c, Theme.A(c, 0.85f), Theme.A(c, 0.7f), Color.white);
            Refresh();
        }

        void ApplySideColors()
        {
            bool buy = buyMode;
            Color c = buy ? Theme.Up : Theme.Down;
            buySide.SetColors(buy ? Theme.A(Theme.Up, 0.16f) : Theme.PanelHi, buy ? Theme.A(Theme.Up, 0.22f) : Theme.PanelHi2, Theme.Line, buy ? Theme.Up : Theme.Sub);
            sellSide.SetColors(!buy ? Theme.A(Theme.Down, 0.18f) : Theme.PanelHi, !buy ? Theme.A(Theme.Down, 0.24f) : Theme.PanelHi2, Theme.Line, !buy ? Theme.Down : Theme.Sub);
            spotAction.SetColors(c, Theme.A(c, 0.85f), Theme.A(c, 0.7f), Color.white);
        }

        /// <summary>휴장/상장폐지 안내 문구. shortForm은 버튼용.</summary>
        public static string ClosedReason(MarketEngine e, MarketAsset a, bool shortForm)
        {
            if (a.Delisted) return shortForm ? "상장폐지된 종목" : "상장폐지된 종목이라 더 이상 거래할 수 없어요.";
            int mins = e.MinutesUntilOpen(a.kind);
            string when = mins > 0 ? Fmt.Duration(mins) + " 후 개장" : "";
            if (shortForm) return "휴장 · " + when;
            string market = a.kind == AssetKind.Domestic ? "국내 주식(평일 09:00~15:30)" : "해외 주식(평일 22:30~05:00, 한국 시간)";
            return market + "은 지금 휴장이에요. " + when + ". 코인은 24시간 거래돼요.";
        }

        double SpotMax(MarketAsset a)
        {
            if (buyMode) return gm.portfolio.MaxBuyQty(a);
            var h = gm.portfolio.GetHolding(a);
            return h != null ? h.qty : 0;
        }

        void SetSpotPct(int i)
        {
            var a = gm.selected;
            spotPct = i;
            spotQty = Portfolio.RoundDown(SpotMax(a) * Pcts[i], a.qtyDecimals);
            Refresh();
        }

        void Nudge(int dir)
        {
            var a = gm.selected;
            double max = SpotMax(a);
            double step = a.qtyDecimals == 0 ? 1 : Math.Max(Math.Pow(10, -a.qtyDecimals), Portfolio.RoundDown(max * 0.05, a.qtyDecimals));
            if (a.qtyDecimals == 0 && a.price < 5000) step = 10;
            spotQty = Math.Max(0, Math.Min(max, Portfolio.RoundDown(spotQty + dir * step, a.qtyDecimals)));
            spotPct = -1;
            Refresh();
        }

        void DoSpot()
        {
            string err = buyMode ? gm.BuySpot(spotQty) : gm.SellSpot(spotQty);
            if (err != null) { Flash(err, Theme.Accent); return; }
            spotQty = 0;
            spotPct = -1;
            Refresh();
        }

        // ───────────────────────── 레버리지 ─────────────────────────

        void BuildLeverage(RectTransform r)
        {
            Label(r, "레버리지 배율", 96);
            float bw = (W - P * 2 - 5 * 5) / 6f;
            for (int i = 0; i < Levs.Length; i++)
            {
                int k = i;
                var b = UI.SolidButton(r, "x" + Levs[i], 13, () => { lev = Levs[k]; Refresh(); });
                b.rectTransform().At(P + (bw + 5) * i, 120, bw, 34);
                levBtns.Add(b);
            }

            Label(r, "증거금 (내 돈)", 170);
            marginValue = Value(r, 166, 18, 26);
            float mw = (W - P * 2 - 3 * 5) / 4f;
            string[] labels = { "10%", "25%", "50%", "최대" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = UI.SolidButton(r, labels[i], 12, () => { marginPct = k; Refresh(); });
                b.rectTransform().At(P + (mw + 5) * i, 200, mw, 32);
                marginBtns.Add(b);
            }

            Label(r, "포지션 규모", 246);
            notionalValue = Value(r, 246);
            Label(r, "롱 청산가", 272);
            liqLongValue = Value(r, 272);
            Label(r, "숏 청산가", 298);
            liqShortValue = Value(r, 298);

            float half = (W - P * 2 - 8) / 2;
            longBtn = BigSideButton(r, "롱  ▲", "오르면 수익", Theme.Up, () => DoLev(true));
            longBtn.rectTransform().At(P, 334, half, 62);
            shortBtn = BigSideButton(r, "숏  ▼", "내리면 수익", Theme.Down, () => DoLev(false));
            shortBtn.rectTransform().At(P + half + 8, 334, half, 62);

            posInfo = UI.Txt(r, "", 12, Theme.Sub);
            posInfo.rectTransform.At(P, 402, W - P * 2, 20);
        }

        UIButton BigSideButton(Transform parent, string title, string sub, Color c, Action onClick)
        {
            var b = UI.Button(parent, title, 18, onClick, c, Theme.A(c, 0.85f), Theme.A(c, 0.7f), Color.white);
            b.useDisabledBg = true;
            b.disabledBg = Theme.PanelHi;
            b.label.rectTransform.Fill(0, 0, 8, 22);
            var s = UI.Txt(b.transform, sub, 11, Theme.A(Color.white, 0.8f), false, TextAlignmentOptions.Center);
            s.rectTransform.Bottom(10, 16);
            return b;
        }

        double MarginAmount()
        {
            if (marginPct < 0) return 0;
            return Math.Floor(gm.portfolio.MaxMargin(lev) * Pcts[marginPct]);
        }

        void DoLev(bool isLong)
        {
            string err = gm.OpenPosition(isLong, lev, MarginAmount());
            if (err != null) { Flash(err, Theme.Accent); return; }
            marginPct = -1;
            Refresh();
        }

        // ───────────────────────── 공통 ─────────────────────────

        TextMeshProUGUI Label(Transform parent, string s, float y)
        {
            var t = UI.Txt(parent, s, 13, Theme.Sub);
            t.rectTransform.At(P, y, 170, 20);
            return t;
        }

        TextMeshProUGUI Value(Transform parent, float y, float size = 13, float h = 20)
        {
            var t = UI.Txt(parent, "", size, Theme.Text, true, TextAlignmentOptions.MidlineRight);
            t.rectTransform.AtRight(P, y, 220, h);
            return t;
        }

        public void Flash(string msg, Color c)
        {
            flashMsg = msg;
            flashColor = c;
            flashUntil = Time.unscaledTime + 2.5f;
            Sfx.I.Error();
            Refresh();
        }

        public void Refresh()
        {
            var a = gm.selected;
            if (a == null || hint == null || levRoot == null) return;
            var pf = gm.portfolio;
            if (a != boundAsset)
            {
                boundAsset = a;
                spotQty = 0; spotPct = -1; marginPct = -1;
            }
            if (lev > a.maxLeverage) lev = a.maxLeverage;

            assetName.text = a.name + Fmt.Colored("  " + a.ticker, Theme.Dim);
            assetPrice.text = Fmt.Price(a.price) + Fmt.Colored(" 원", Theme.Dim);
            assetPrice.color = Theme.Change(a.Change);

            bool tradable = gm.engine.IsTradable(a);
            bool playing = gm.state == GameState.Playing;
            var h = pf.GetHolding(a);

            if (spotRoot.gameObject.activeSelf)
            {
                ApplySideColors();
                double max = SpotMax(a);
                if (spotPct >= 0) spotQty = Portfolio.RoundDown(max * Pcts[spotPct], a.qtyDecimals);
                if (spotQty > max) spotQty = max;

                availLabel.text = buyMode ? "주문 가능 현금" : "매도 가능 수량";
                availValue.text = buyMode ? Fmt.Won(pf.cash) + " 원" : Fmt.Qty(max, a.qtyDecimals) + " " + a.Unit;
                heldValue.text = h != null
                    ? Fmt.Qty(h.qty, a.qtyDecimals) + " " + a.Unit + Fmt.Colored("  (" + Fmt.Pct(h.PnLPct) + ")", Theme.Change(h.PnL))
                    : Fmt.Colored("없음", Theme.Dim);
                qtyValue.text = Fmt.Qty(spotQty, a.qtyDecimals) + Fmt.Colored(" " + a.Unit, Theme.Dim);
                double amount = spotQty * a.price;
                amountValue.text = Fmt.Won(amount) + " 원";
                feeValue.text = Fmt.Won(amount * Portfolio.FeeRate) + " 원";
                for (int i = 0; i < spotPctBtns.Count; i++)
                {
                    spotPctBtns[i].textColor = i == spotPct ? (buyMode ? Theme.Up : Theme.Down) : Theme.Sub;
                    spotPctBtns[i].Refresh();
                }

                bool ok = playing && tradable && spotQty > 0;
                spotAction.SetInteractable(ok);
                if (!tradable) spotAction.SetLabel(ClosedReason(gm.engine, a, true));
                else if (spotQty <= 0) spotAction.SetLabel(buyMode ? "수량을 선택하세요" : (max > 0 ? "수량을 선택하세요" : "보유 수량 없음"));
                else spotAction.SetLabel((buyMode ? "매수하기  " : "매도하기  ") + Fmt.Short(amount) + "원");
            }
            else
            {
                for (int i = 0; i < levBtns.Count; i++)
                {
                    bool allowed = Levs[i] <= a.maxLeverage;
                    levBtns[i].SetInteractable(allowed);
                    bool on = Levs[i] == lev;
                    levBtns[i].normal = on ? Theme.A(Theme.Accent, 0.16f) : Theme.PanelHi;
                    levBtns[i].textColor = on ? Theme.Accent : Theme.Sub;
                    levBtns[i].Refresh();
                }
                for (int i = 0; i < marginBtns.Count; i++)
                {
                    marginBtns[i].textColor = i == marginPct ? Theme.Accent : Theme.Sub;
                    marginBtns[i].Refresh();
                }
                double margin = MarginAmount();
                marginValue.text = Fmt.Won(margin) + Fmt.Colored(" 원", Theme.Dim);
                notionalValue.text = Fmt.Won(margin * lev) + " 원" + Fmt.Colored("  (x" + lev + ")", Theme.Dim);
                liqLongValue.text = Fmt.Colored(Fmt.Price(Portfolio.LiqPrice(true, a.price, lev)), Theme.Up) +
                                    Fmt.Colored("  " + Fmt.Pct(-(1.0 / lev - Portfolio.MaintRate) * 100), Theme.Dim);
                liqShortValue.text = Fmt.Colored(Fmt.Price(Portfolio.LiqPrice(false, a.price, lev)), Theme.Down) +
                                     Fmt.Colored("  " + Fmt.Pct((1.0 / lev - Portfolio.MaintRate) * 100), Theme.Dim);

                bool ok = playing && tradable && margin >= 10000;
                longBtn.SetInteractable(ok);
                shortBtn.SetInteractable(ok);
                longBtn.SetColors(Theme.Up, Theme.A(Theme.Up, 0.85f), Theme.A(Theme.Up, 0.7f), Color.white);
                shortBtn.SetColors(Theme.Down, Theme.A(Theme.Down, 0.85f), Theme.A(Theme.Down, 0.7f), Color.white);
                if (!tradable) { string cl = a.Delisted ? "상장폐지" : "휴장"; longBtn.SetLabel(cl); shortBtn.SetLabel(cl); }
                else { longBtn.SetLabel("롱  ▲"); shortBtn.SetLabel("숏  ▼"); }

                int pc = pf.PositionCount(a);
                double upnl = 0;
                foreach (var p in pf.positions) if (p.asset == a) upnl += p.PnL;
                posInfo.text = pc > 0
                    ? "이 종목 포지션 " + pc + "개  ·  미실현 " + Fmt.Colored(Fmt.SignedWon(upnl) + "원", Theme.Change(upnl))
                    : Fmt.Colored("이 종목에 열린 포지션이 없습니다", Theme.Dim);
            }

            // 안내 문구
            if (Time.unscaledTime < flashUntil && flashMsg != null)
            {
                hint.text = Fmt.Colored("! " + flashMsg, flashColor);
            }
            else if (!tradable)
            {
                hint.text = ClosedReason(gm.engine, a, false);
            }
            else if (a.status == ListingStatus.Warning || a.status == ListingStatus.Liquidation)
            {
                hint.text = Fmt.Colored(a.status == ListingStatus.Liquidation
                    ? "! 정리매매 중: 장 마감 후 상장폐지됩니다. 보유분은 0원이 돼요. (숏은 수익 확정)"
                    : "! 관리종목: 다음 거래일에 상장폐지가 결정될 수 있어요.", Theme.Accent);
            }
            else if (spotRoot.gameObject.activeSelf)
            {
                hint.text = buyMode
                    ? "가격이 오를 것 같으면 매수, 오른 뒤 매도해서 차익을 챙기세요."
                    : "보유한 수량만 팔 수 있어요. 하락에 베팅하려면 레버리지 탭의 숏을 쓰세요.";
            }
            else
            {
                hint.text = "레버리지는 수익도 손실도 배율만큼 커집니다. 가격이 청산가에 닿으면 증거금을 전부 잃어요.";
            }
        }

        void Update()
        {
            if (flashMsg != null && Time.unscaledTime >= flashUntil)
            {
                flashMsg = null;
                Refresh();
            }
        }
    }
}

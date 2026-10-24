using System;
using System.Collections.Generic;

namespace BuyOrDie
{
    public class Holding
    {
        public MarketAsset asset;
        public double qty;
        public double avg;
        public double Value { get { return asset.Delisted ? 0 : qty * asset.price; } }
        public double PnL { get { return Value - avg * qty; } }
        public double PnLPct { get { return avg > 0 ? (Value / (avg * qty) - 1.0) * 100.0 : 0; } }
    }

    public class Position
    {
        public int id;
        public MarketAsset asset;
        public bool isLong;
        public int leverage;
        public double entry, margin, qty, liq;
        public int openedAt;

        public double PnL { get { return (isLong ? asset.price - entry : entry - asset.price) * qty; } }
        public double Roe { get { return margin > 0 ? PnL / margin * 100.0 : 0; } }
        public double Value { get { return Math.Max(0, margin + PnL); } }
        public double Notional { get { return qty * asset.price; } }
    }

    public class TradeLog
    {
        public int minute;
        public MarketAsset asset;
        public string action;
        public bool positive;
        public double price, qty;
        public double pnl;
        public bool hasPnl;
    }

    /// <summary>플레이어 계좌: 현금, 현물 보유, 레버리지 포지션, 거래 기록.</summary>
    public class Portfolio
    {
        public const double StartCash = 10000000;
        public const double FeeRate = 0.0005;
        public const double MaintRate = 0.005;
        /// <summary>저장되는 거래 기록 최대 건수 (오래된 것부터 지워짐)</summary>
        public const int MaxLogs = 1000;

        public double cash = StartCash;
        public double realized;
        public double peakEquity = StartCash;
        public int tradeCount, liquidations;
        public readonly Dictionary<MarketAsset, Holding> holdings = new Dictionary<MarketAsset, Holding>();
        public readonly List<Holding> holdingList = new List<Holding>();
        public readonly List<Position> positions = new List<Position>();
        public readonly List<TradeLog> logs = new List<TradeLog>();
        int nextId = 1;

        public double SpotValue { get { double v = 0; foreach (var h in holdingList) v += h.Value; return v; } }
        public double SpotPnL { get { double v = 0; foreach (var h in holdingList) v += h.PnL; return v; } }
        public double PositionValue { get { double v = 0; foreach (var p in positions) v += p.Value; return v; } }
        public double PositionPnL { get { double v = 0; foreach (var p in positions) v += p.PnL; return v; } }
        public double Equity { get { return cash + SpotValue + PositionValue; } }
        public double ReturnPct { get { return (Equity / StartCash - 1.0) * 100.0; } }

        public Holding GetHolding(MarketAsset a)
        {
            Holding h;
            return holdings.TryGetValue(a, out h) ? h : null;
        }

        public int PositionCount(MarketAsset a)
        {
            int n = 0;
            foreach (var p in positions) if (p.asset == a) n++;
            return n;
        }

        public bool Owns(MarketAsset a) { return GetHolding(a) != null || PositionCount(a) > 0; }

        public static double RoundDown(double q, int dec)
        {
            double m = Math.Pow(10, dec);
            return Math.Floor(q * m + 1e-7) / m;
        }

        public double MaxBuyQty(MarketAsset a)
        {
            return RoundDown(cash / (a.price * (1 + FeeRate)), a.qtyDecimals);
        }

        public string BuySpot(MarketAsset a, double qty, int minute)
        {
            qty = RoundDown(qty, a.qtyDecimals);
            if (qty <= 0) return "주문 수량을 선택하세요";
            double cost = qty * a.price;
            double fee = cost * FeeRate;
            if (cost + fee > cash + 0.5) return "현금이 부족합니다";
            cash -= cost + fee;
            var h = GetHolding(a);
            if (h == null)
            {
                h = new Holding { asset = a };
                holdings[a] = h;
                holdingList.Add(h);
            }
            h.avg = (h.avg * h.qty + cost) / (h.qty + qty);
            h.qty += qty;
            realized -= fee;
            tradeCount++;
            Log(minute, a, "매수", true, a.price, qty, 0, false);
            return null;
        }

        public string SellSpot(MarketAsset a, double qty, int minute)
        {
            var h = GetHolding(a);
            if (h == null || h.qty <= 0) return "보유 수량이 없습니다";
            qty = Math.Min(RoundDown(qty, a.qtyDecimals), h.qty);
            if (qty <= 0) return "주문 수량을 선택하세요";
            double proceeds = qty * a.price;
            double fee = proceeds * FeeRate;
            double pnl = (a.price - h.avg) * qty - fee;
            cash += proceeds - fee;
            realized += pnl;
            h.qty -= qty;
            if (h.qty < Math.Pow(10, -a.qtyDecimals) * 0.5) RemoveHolding(h);
            tradeCount++;
            Log(minute, a, "매도", false, a.price, qty, pnl, true);
            return null;
        }

        void RemoveHolding(Holding h)
        {
            holdings.Remove(h.asset);
            holdingList.Remove(h);
        }

        public static double LiqPrice(bool isLong, double entry, int lev)
        {
            double m = 1.0 / lev - MaintRate;
            return isLong ? entry * (1 - m) : entry * (1 + m);
        }

        public string OpenPosition(MarketAsset a, bool isLong, int lev, double margin, int minute, out Position opened)
        {
            opened = null;
            if (margin < 10000) return "증거금은 최소 1만 원입니다";
            double notional = margin * lev;
            double fee = notional * FeeRate;
            if (margin + fee > cash + 0.5) return "현금이 부족합니다";
            cash -= margin + fee;
            realized -= fee;
            var p = new Position
            {
                id = nextId++, asset = a, isLong = isLong, leverage = lev, entry = a.price, margin = margin,
                qty = notional / a.price, liq = LiqPrice(isLong, a.price, lev), openedAt = minute
            };
            positions.Add(p);
            tradeCount++;
            Log(minute, a, (isLong ? "롱 x" : "숏 x") + lev, isLong, a.price, p.qty, 0, false);
            opened = p;
            return null;
        }

        public double MaxMargin(int lev) { return Math.Floor(cash / (1 + lev * FeeRate)); }

        public double ClosePosition(Position p, int minute)
        {
            if (!positions.Contains(p)) return 0;
            double fee = p.Notional * FeeRate;
            double payout = Math.Max(0, p.margin + p.PnL - fee);
            double pnl = payout - p.margin;
            cash += payout;
            realized += pnl;
            positions.Remove(p);
            tradeCount++;
            Log(minute, p.asset, (p.isLong ? "롱" : "숏") + " 청산", !p.isLong, p.asset.price, p.qty, pnl, true);
            return pnl;
        }

        public void CheckLiquidations(MarketAsset a, double low, double high, int minute, List<Position> liquidated)
        {
            for (int i = positions.Count - 1; i >= 0; i--)
            {
                var p = positions[i];
                if (p.asset != a) continue;
                bool hit = p.isLong ? low <= p.liq : high >= p.liq;
                if (!hit) continue;
                positions.RemoveAt(i);
                realized -= p.margin;
                liquidations++;
                Log(minute, a, "강제청산", false, p.liq, p.qty, -p.margin, true);
                liquidated.Add(p);
            }
        }

        /// <summary>상장폐지: 현물은 전액 손실, 포지션은 마지막 가격으로 정산 (숏은 수익).</summary>
        public double HandleDelisting(MarketAsset a, int minute, out double spotLoss)
        {
            spotLoss = 0;
            var h = GetHolding(a);
            if (h != null)
            {
                spotLoss = h.avg * h.qty;
                realized -= spotLoss;
                Log(minute, a, "상장폐지", false, 0, h.qty, -spotLoss, true);
                RemoveHolding(h);
            }
            double posPnl = 0;
            foreach (var p in new List<Position>(positions))
                if (p.asset == a) posPnl += ClosePosition(p, minute);
            return posPnl;
        }

        public void UpdatePeak()
        {
            double e = Equity;
            if (e > peakEquity) peakEquity = e;
        }

        void Log(int minute, MarketAsset a, string action, bool positive, double price, double qty, double pnl, bool hasPnl)
        {
            logs.Add(new TradeLog { minute = minute, asset = a, action = action, positive = positive, price = price, qty = qty, pnl = pnl, hasPnl = hasPnl });
            if (logs.Count > MaxLogs) logs.RemoveRange(0, logs.Count - MaxLogs);
        }

        // ───────────────────────── 저장 / 불러오기 ─────────────────────────

        public PortfolioSave Capture()
        {
            var s = new PortfolioSave
            {
                cash = cash, realized = realized, peak = peakEquity, trades = tradeCount, liqs = liquidations, nextId = nextId,
                holdings = new List<HoldingSave>(), positions = new List<PositionSave>(), logs = new List<LogSave>()
            };
            foreach (var h in holdingList) s.holdings.Add(new HoldingSave { asset = h.asset.index, qty = h.qty, avg = h.avg });
            foreach (var p in positions)
                s.positions.Add(new PositionSave { id = p.id, asset = p.asset.index, isLong = p.isLong, lev = p.leverage, entry = p.entry, margin = p.margin, qty = p.qty, liq = p.liq, openedAt = p.openedAt });
            int start = Math.Max(0, logs.Count - MaxLogs);
            for (int i = start; i < logs.Count; i++)
            {
                var l = logs[i];
                s.logs.Add(new LogSave { minute = l.minute, asset = l.asset.index, action = l.action, positive = l.positive, price = l.price, qty = l.qty, pnl = l.pnl, hasPnl = l.hasPnl });
            }
            return s;
        }

        public void Restore(PortfolioSave s, List<MarketAsset> assets)
        {
            cash = s.cash; realized = s.realized; peakEquity = s.peak; tradeCount = s.trades; liquidations = s.liqs; nextId = s.nextId;
            holdings.Clear(); holdingList.Clear(); positions.Clear(); logs.Clear();
            foreach (var h in s.holdings)
            {
                var hh = new Holding { asset = assets[h.asset], qty = h.qty, avg = h.avg };
                holdings[hh.asset] = hh;
                holdingList.Add(hh);
            }
            foreach (var p in s.positions)
                positions.Add(new Position { id = p.id, asset = assets[p.asset], isLong = p.isLong, leverage = p.lev, entry = p.entry, margin = p.margin, qty = p.qty, liq = p.liq, openedAt = p.openedAt });
            foreach (var l in s.logs)
                logs.Add(new TradeLog { minute = l.minute, asset = assets[l.asset], action = l.action, positive = l.positive, price = l.price, qty = l.qty, pnl = l.pnl, hasPnl = l.hasPnl });
        }
    }
}

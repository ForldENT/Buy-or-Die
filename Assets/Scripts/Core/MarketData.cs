using System;
using System.Collections.Generic;

namespace BuyOrDie
{
    public enum AssetKind { Domestic, Overseas, Coin }

    public enum ListingStatus { Normal, Warning, Liquidation, Delisted }

    public enum NewsTone { Good, Bad, Macro, Info, Notice }

    public struct Candle
    {
        public int time;
        public double open, high, low, close, volume;
    }

    public class NewsItem
    {
        public int minute;
        public string headline;
        public NewsTone tone;
        public MarketAsset asset;
        public bool breaking;
    }

    /// <summary>
    /// 게임 속 거래 종목 하나 (국내 주식 / 해외 주식 / 코인).
    /// 가격은 1분 캔들 단위로 기록되고, 차트에서는 원하는 분봉으로 묶어서 보여준다.
    /// </summary>
    public class MarketAsset
    {
        public const int MaxCandles = 1500;

        public readonly int index;
        public readonly string name;
        public readonly string ticker;
        public readonly string sector;
        public readonly AssetKind kind;
        public readonly double sigma;   // 1분당 변동성
        public readonly double beta;    // 시장 흐름 민감도
        public readonly double initPrice;
        public readonly int qtyDecimals;
        public readonly int maxLeverage;
        public readonly double baseVolume;

        public double price;
        public double prevClose;   // 전일 종가 (코인은 09:00 기준가)
        public double anchor;      // 가격이 서서히 돌아가려는 적정가
        public double pendingGap;  // 장이 닫혀 있을 때 쌓인 뉴스 → 다음 개장 시 갭
        public bool limitUpNotified, limitDownNotified;

        // 뉴스 이벤트: 정해진 시간 안에 목표가까지 반드시 이동
        public bool evActive;
        public int evElapsed, evDuration;
        public double evStartLog, evTargetLog;

        // 상장폐지 단계
        public ListingStatus status = ListingStatus.Normal;
        public int statusDay;

        public readonly List<Candle> candles = new List<Candle>(MaxCandles + 8);

        public MarketAsset(int index, string name, string ticker, string sector, AssetKind kind, double price, double sigma, double beta)
        {
            this.index = index;
            this.name = name;
            this.ticker = ticker;
            this.sector = sector;
            this.kind = kind;
            this.price = price;
            initPrice = price;
            prevClose = price;
            anchor = price;
            this.sigma = sigma;
            this.beta = beta;
            if (kind == AssetKind.Coin)
            {
                qtyDecimals = price >= 1000000 ? 4 : price >= 10000 ? 2 : 0;
                maxLeverage = 50;
            }
            else
            {
                qtyDecimals = 0;
                maxLeverage = 10;
            }
            baseVolume = 4.0e9 / price;
        }

        public bool IsCoin { get { return kind == AssetKind.Coin; } }
        public bool IsStock { get { return kind != AssetKind.Coin; } }
        public bool Delisted { get { return status == ListingStatus.Delisted; } }
        public double ChangePct { get { return prevClose > 0 ? (price / prevClose - 1.0) * 100.0 : 0.0; } }
        public double Change { get { return price - prevClose; } }
        public double LimitUp { get { return prevClose * 1.3; } }
        public double LimitDown { get { return prevClose * 0.7; } }
        public bool HasPriceLimit { get { return kind == AssetKind.Domestic && status != ListingStatus.Liquidation; } }
        public string Unit { get { return IsCoin ? ticker : "주"; } }

        public string KindLabel
        {
            get { return kind == AssetKind.Domestic ? "국내주식" : kind == AssetKind.Overseas ? "해외주식" : "코인"; }
        }

        public string StatusLabel
        {
            get
            {
                switch (status)
                {
                    case ListingStatus.Warning: return IsCoin ? "유의" : "관리";
                    case ListingStatus.Liquidation: return IsCoin ? "종료예정" : "정리매매";
                    case ListingStatus.Delisted: return "상폐";
                    default: return "";
                }
            }
        }

        public void AddCandle(Candle c)
        {
            candles.Add(c);
            if (candles.Count > MaxCandles) candles.RemoveRange(0, candles.Count - MaxCandles);
        }

        /// <summary>1분 캔들을 tf분 단위로 묶어 최근 count개를 output에 채운다.</summary>
        public void GetCandles(int tf, int count, List<Candle> output)
        {
            output.Clear();
            int i = candles.Count - 1;
            while (i >= 0 && output.Count < count)
            {
                int key = candles[i].time / tf;
                Candle agg = candles[i];
                int j = i - 1;
                while (j >= 0 && candles[j].time / tf == key)
                {
                    Candle c = candles[j];
                    agg.open = c.open;
                    if (c.high > agg.high) agg.high = c.high;
                    if (c.low < agg.low) agg.low = c.low;
                    agg.volume += c.volume;
                    j--;
                }
                agg.time = key * tf;
                output.Add(agg);
                i = j;
            }
            output.Reverse();
        }

        /// <summary>스파크라인용 최근 종가.</summary>
        public void GetRecentCloses(int n, int step, List<double> output)
        {
            output.Clear();
            int start = Math.Max(0, candles.Count - 1 - (n - 1) * step);
            for (int i = start; i < candles.Count; i += step) output.Add(candles[i].close);
        }
    }
}

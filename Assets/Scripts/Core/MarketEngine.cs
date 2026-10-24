using System;
using System.Collections.Generic;

namespace BuyOrDie
{
    public class ScheduledNews
    {
        public int minute, template, asset = -1, duration;
        public double mag;
        public bool fired;
    }

    /// <summary>
    /// 시장 시뮬레이터. Step() 한 번 = 게임 시간 1분.
    /// - 달력: 2027-01-04(월) = DAY 1. 주말에는 주식 휴장, 코인은 24시간.
    /// - 국내 09:00~15:30, 해외 22:30~익일 05:00 (한국 시간).
    /// - 하루 10개의 뉴스가 300종 중에서 무작위로 예약되어 발표된다.
    ///   기업 악재는 몇 시간 안에 반드시 하락, 호재는 반드시 상승 (목표가 추적 방식).
    /// - 관리종목 지정 → 정리매매 → 상장폐지 단계.
    /// </summary>
    public class MarketEngine
    {
        public static readonly DateTime Epoch = new DateTime(2026, 12, 31);
        public const int FirstDayIndex = 4;            // 2027-01-04 (월)
        public const int DomOpen = 9 * 60, DomClose = 15 * 60 + 30;
        public const int OvsOpen = 22 * 60 + 30, OvsClose = 5 * 60;
        public const int NewsPerDay = 10;
        const int SubSteps = 4;

        public readonly List<MarketAsset> assets;
        public readonly List<NewsItem> news = new List<NewsItem>();
        public readonly List<ScheduledNews> schedule = new List<ScheduledNews>();
        public int minute;
        public int seed;
        public bool suppressNews;

        // 대폭락장(-1) / 대호황장(+1): 낮은 확률로 발생해 1~2일 이어지는 시장 전체 이벤트
        public int regime, regimeEnd, regimeStart = -1, regimePending, lastRegimeDay = -99, regimeWaveDay;
        public double volMult = 1;
        public event Action<int> RegimeChanged;
        public const double CrashChance = 0.035, BoomChance = 0.035;
        public string RegimeName { get { return regime < 0 ? "대폭락장" : regime > 0 ? "대호황장" : ""; } }

        public event Action<NewsItem> NewsPublished;
        public event Action<AssetKind> SessionOpened;
        public event Action<AssetKind> SessionClosed;
        public event Action DayStarted;
        public event Action<MarketAsset, bool> PriceLimitHit;
        public event Action<MarketAsset> AssetDelisted;

        Random rng;
        readonly Dictionary<string, double> sectorFactor = new Dictionary<string, double>();
        readonly List<string> sectorKeys = new List<string>();
        public double domFactor, ovsFactor, cryFactor;
        bool domWasOpen, ovsWasOpen;

        public MarketEngine(int seed)
        {
            this.seed = seed;
            rng = new Random(seed);
            assets = AssetCatalog.Create();
            foreach (var a in assets)
                if (!sectorFactor.ContainsKey(a.sector)) { sectorFactor[a.sector] = 0; sectorKeys.Add(a.sector); }
        }

        // ───────────────────────── 달력 / 장 시간 ─────────────────────────

        public int DayIndex { get { return minute / 1440; } }
        public int Day { get { return DayIndex - FirstDayIndex + 1; } }
        public int MinuteOfDay { get { return minute % 1440; } }
        public DateTime Now { get { return Epoch.AddMinutes(minute); } }

        public static bool Weekday(int dayIndex)
        {
            var d = Epoch.AddDays(dayIndex).DayOfWeek;
            return d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
        }

        public static string DayName(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "월";
                case DayOfWeek.Tuesday: return "화";
                case DayOfWeek.Wednesday: return "수";
                case DayOfWeek.Thursday: return "목";
                case DayOfWeek.Friday: return "금";
                case DayOfWeek.Saturday: return "토";
                default: return "일";
            }
        }

        public static bool DomesticOpenAt(int m)
        {
            int mod = m % 1440;
            return Weekday(m / 1440) && mod >= DomOpen && mod < DomClose;
        }

        public static bool OverseasOpenAt(int m)
        {
            int mod = m % 1440, d = m / 1440;
            return (mod >= OvsOpen && Weekday(d)) || (mod < OvsClose && Weekday(d - 1));
        }

        public static bool OpenAt(AssetKind k, int m)
        {
            if (k == AssetKind.Coin) return true;
            return k == AssetKind.Domestic ? DomesticOpenAt(m) : OverseasOpenAt(m);
        }

        public bool DomesticOpen { get { return DomesticOpenAt(minute); } }
        public bool OverseasOpen { get { return OverseasOpenAt(minute); } }
        public bool AnyStockOpen { get { return DomesticOpen || OverseasOpen; } }
        public bool IsOpen(MarketAsset a) { return !a.Delisted && OpenAt(a.kind, minute); }
        public bool IsTradable(MarketAsset a) { return IsOpen(a); }

        /// <summary>해당 시장이 다음에 열리기까지 남은 분 (열려 있으면 0).</summary>
        public int MinutesUntilOpen(AssetKind k)
        {
            if (OpenAt(k, minute)) return 0;
            int m = (minute / 30 + 1) * 30;
            for (int i = 0; i < 4 * 48; i++, m += 30) if (OpenAt(k, m)) return m - minute;
            return -1;
        }

        /// <summary>열려 있는 시장이 닫히기까지 남은 분.</summary>
        public int MinutesUntilClose(AssetKind k)
        {
            if (!OpenAt(k, minute)) return 0;
            int m = (minute / 30 + 1) * 30;
            for (int i = 0; i < 2 * 48; i++, m += 30) if (!OpenAt(k, m)) return m - minute;
            return 1440;
        }

        public int NewsFiredToday
        {
            get
            {
                int n = 0, d = DayIndex;
                foreach (var s in schedule) if (s.fired && s.minute / 1440 == d) n++;
                return n;
            }
        }

        // ───────────────────────── 진행 ─────────────────────────

        /// <summary>게임 시작 전 과거 차트를 채운다 (1/1 금 09:00 → 1/4 월 08:50).</summary>
        public void Warmup()
        {
            suppressNews = true;
            minute = 1 * 1440 + DomOpen - 1;
            domWasOpen = DomesticOpen;
            ovsWasOpen = OverseasOpen;
            int target = FirstDayIndex * 1440 + DomOpen - 10;
            while (minute < target) Step();
            foreach (var a in assets)
            {
                a.evActive = false;
                a.pendingGap = 0;
                a.anchor = a.price;
            }
            news.Clear();
            schedule.Clear();
            suppressNews = false;
            BuildSchedule(DayIndex, minute + 3);
        }

        public void Step()
        {
            minute++;
            int mod = MinuteOfDay;
            if (mod == 0) NewDay();

            bool dOpen = DomesticOpen, oOpen = OverseasOpen;
            if (dOpen && !domWasOpen) OpenSession(AssetKind.Domestic);
            if (!dOpen && domWasOpen) CloseSession(AssetKind.Domestic);
            if (oOpen && !ovsWasOpen) OpenSession(AssetKind.Overseas);
            if (!oOpen && ovsWasOpen) CloseSession(AssetKind.Overseas);
            domWasOpen = dOpen;
            ovsWasOpen = oOpen;

            if (mod == DomOpen)
            {
                foreach (var a in assets)
                    if (a.IsCoin && !a.Delisted) a.prevClose = a.price;   // 코인 일일 기준가 09:00 리셋
            }

            if (!suppressNews) FireScheduled();
            if (!suppressNews) UpdateRegime();
            UpdateFactors();
            for (int i = 0; i < assets.Count; i++)
            {
                var a = assets[i];
                if (!IsOpen(a)) continue;
                SimulateMinute(a);
            }
            CheckDelistings();
        }

        void NewDay()
        {
            if (suppressNews) return;
            ProcessListingStatus();
            BuildSchedule(DayIndex, minute + 1);
            PlanRegime();
            if (DayStarted != null) DayStarted();
        }

        void OpenSession(AssetKind kind)
        {
            foreach (var a in assets)
            {
                if (a.kind != kind || a.Delisted) continue;
                a.prevClose = a.price;
                a.limitUpNotified = a.limitDownNotified = false;
                double gap = a.pendingGap + Gauss() * a.sigma * 4.0;
                a.pendingGap = 0;
                double p = a.price * Math.Exp(gap);
                if (a.HasPriceLimit) p = Clamp(p, a.LimitDown, a.LimitUp);
                a.price = p;
            }
            if (SessionOpened != null && !suppressNews) SessionOpened(kind);
        }

        void CloseSession(AssetKind kind)
        {
            if (SessionClosed != null && !suppressNews) SessionClosed(kind);
        }

        void UpdateFactors()
        {
            domFactor = domFactor * 0.92 + Gauss() * 0.00006;
            ovsFactor = ovsFactor * 0.92 + Gauss() * 0.00006;
            cryFactor = cryFactor * 0.92 + Gauss() * 0.0001;
            foreach (var k in sectorKeys) sectorFactor[k] = sectorFactor[k] * 0.9 + Gauss() * 0.00005;
        }

        double FactorOf(MarketAsset a)
        {
            return a.kind == AssetKind.Domestic ? domFactor : a.kind == AssetKind.Overseas ? ovsFactor : cryFactor;
        }

        void SimulateMinute(MarketAsset a)
        {
            double o = a.price, h = o, l = o, p = o;
            double mkt = a.beta * FactorOf(a);
            double sd = a.sigma * volMult / Math.Sqrt(SubSteps);
            bool ev = a.evActive;

            if (ev)
            {
                // 뉴스 이벤트: 목표가 경로를 강하게 추적 → 방향은 반드시 뉴스대로
                a.evElapsed++;
                double t = Math.Min(1.0, (double)a.evElapsed / a.evDuration);
                double e = 1 - (1 - t) * (1 - t);
                double path = a.evStartLog + (a.evTargetLog - a.evStartLog) * e;
                for (int s = 0; s < SubSteps; s++)
                {
                    double r = 0.22 * (path - Math.Log(p)) + sd * 0.45 * Gauss() + mkt * 0.3 / SubSteps;
                    p *= Math.Exp(r);
                    if (a.HasPriceLimit) p = Clamp(p, a.LimitDown, a.LimitUp);
                    if (p > h) h = p;
                    if (p < l) l = p;
                }
                if (a.evElapsed >= a.evDuration)
                {
                    a.evActive = false;
                    a.anchor = Math.Exp(a.evTargetLog);
                }
            }
            else
            {
                double meanRev = 0.0025 * Math.Log(a.anchor / p);
                double drift = mkt + sectorFactor[a.sector] + meanRev;
                for (int s = 0; s < SubSteps; s++)
                {
                    double r = drift / SubSteps + sd * Gauss();
                    if (rng.NextDouble() < 0.003) r += sd * Gauss() * 5.0;
                    p *= Math.Exp(r);
                    if (a.HasPriceLimit) p = Clamp(p, a.LimitDown, a.LimitUp);
                    if (p > h) h = p;
                    if (p < l) l = p;
                }
                a.anchor *= Math.Exp(Gauss() * a.sigma * 0.2);
            }

            a.price = p;
            double move = Math.Abs(Math.Log(p / o)) / a.sigma;
            double vol = a.baseVolume * (0.4 + rng.NextDouble() * 0.8) * (1.0 + move * 0.9 + (ev ? 1.4 : 0));
            a.AddCandle(new Candle { time = minute, open = o, high = h, low = l, close = p, volume = vol });

            if (a.HasPriceLimit && !suppressNews)
            {
                if (!a.limitUpNotified && p >= a.LimitUp * 0.9999)
                {
                    a.limitUpNotified = true;
                    if (PriceLimitHit != null) PriceLimitHit(a, true);
                }
                if (!a.limitDownNotified && p <= a.LimitDown * 1.0001)
                {
                    a.limitDownNotified = true;
                    if (PriceLimitHit != null) PriceLimitHit(a, false);
                }
            }
        }

        /// <summary>
        /// duration분(실제로 시장이 열려 있는 시간 기준) 안에 가격을 (1+mag)배 쪽으로 이동시킨다.
        /// 시장이 닫혀 있으면 다음 개장 갭으로 반영 (force면 개장 후 이벤트로 진행).
        /// </summary>
        public void StartEvent(MarketAsset a, double mag, int duration, bool force = false)
        {
            if (a.Delisted) return;
            double delta = Math.Log(1 + mag);
            if (!force && !IsOpen(a))
            {
                a.pendingGap += delta;
                a.anchor *= Math.Exp(delta);
                return;
            }
            double cur = Math.Log(a.price);
            double rem = a.evActive ? a.evTargetLog - cur : 0;
            if (rem * delta < 0) rem = 0;   // 반대 방향 잔여 추세는 버려서 새 뉴스 방향을 보장
            a.evStartLog = cur;
            a.evTargetLog = cur + rem + delta;
            a.evElapsed = 0;
            a.evDuration = Math.Max(20, duration);
            a.evActive = true;
        }

        // ───────────────────────── 하루 10개 뉴스 ─────────────────────────

        void BuildSchedule(int dayIndex, int fromMinute)
        {
            schedule.RemoveAll(s => s.minute / 1440 < dayIndex);
            var templates = NewsCatalog.All;
            var used = new HashSet<int>();
            int made = 0, guard = 0;
            while (made < NewsPerDay && guard++ < 600)
            {
                var t = templates[rng.Next(templates.Count)];
                if (used.Contains(t.id)) continue;
                var s = Plan(t, dayIndex, fromMinute);
                if (s == null) continue;
                used.Add(t.id);
                schedule.Add(s);
                made++;
            }
            schedule.Sort((x, y) => x.minute.CompareTo(y.minute));
        }

        struct Window { public int start, end, sessionEnd; }

        bool PickWindow(AssetKind kind, int day, int from, int latestOffsetFromOpen, out Window w)
        {
            int d0 = day * 1440;
            var list = new List<Window>();
            if (kind == AssetKind.Domestic)
            {
                if (Weekday(day)) list.Add(new Window { start = d0 + DomOpen, end = d0 + DomOpen + latestOffsetFromOpen, sessionEnd = d0 + DomClose });
            }
            else if (kind == AssetKind.Overseas)
            {
                if (Weekday(day - 1)) list.Add(new Window { start = d0, end = d0 + 210, sessionEnd = d0 + OvsClose });
                if (Weekday(day)) list.Add(new Window { start = d0 + OvsOpen, end = d0 + 1439, sessionEnd = d0 + 1440 + OvsClose });
            }
            else list.Add(new Window { start = d0, end = d0 + 1439, sessionEnd = d0 + 1440 + 600 });

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var x = list[i];
                x.start = Math.Max(x.start, from);
                if (x.end - x.start < 5) list.RemoveAt(i); else list[i] = x;
            }
            if (list.Count == 0) { w = default(Window); return false; }
            w = list[rng.Next(list.Count)];
            return true;
        }

        ScheduledNews Plan(NewsTemplate t, int day, int from)
        {
            int d0 = day * 1440;
            from = Math.Max(from, d0);
            var s = new ScheduledNews { template = t.id };

            if (t.IsCompany || t.IsCoin)
            {
                var cands = new List<MarketAsset>();
                foreach (var a in assets)
                {
                    if (a.status == ListingStatus.Liquidation || a.Delisted) continue;
                    if (t.IsCoin != a.IsCoin) continue;
                    if (t.sectors != null && Array.IndexOf(t.sectors, a.sector) < 0) continue;
                    cands.Add(a);
                }
                for (int tries = 0; tries < 8 && cands.Count > 0; tries++)
                {
                    var a = cands[rng.Next(cands.Count)];
                    Window w;
                    if (!PickWindow(a.kind, day, from, 300, out w)) continue;
                    s.asset = a.index;
                    s.minute = rng.Next(w.start, w.end + 1);
                    int maxDur = w.sessionEnd - s.minute - 10;
                    s.duration = Math.Max(30, Math.Min(rng.Next(60, 241), maxDur));
                    double m = a.IsCoin ? Range(0.08, 0.22) : Range(0.05, 0.14);
                    s.mag = t.IsGood ? m : -m;
                    return s;
                }
                return null;
            }

            if (t.IsSector)
            {
                AssetKind? best = null;
                foreach (AssetKind k in new[] { AssetKind.Domestic, AssetKind.Overseas, AssetKind.Coin })
                {
                    bool has = false;
                    foreach (var a in assets) if (a.kind == k && !a.Delisted && Array.IndexOf(t.sectors, a.sector) >= 0) { has = true; break; }
                    if (!has) continue;
                    Window w;
                    if (PickWindow(k, day, from, 300, out w)) { best = k; s.minute = rng.Next(w.start, w.end + 1); s.duration = Math.Max(30, Math.Min(rng.Next(90, 241), w.sessionEnd - s.minute - 10)); break; }
                }
                if (best == null) return null;
                double m = Range(0.03, 0.08);
                s.mag = t.IsGood ? m : -m;
                return s;
            }

            // 거시경제 · 시사: 하루 중 아무 때나
            if (d0 + 1439 - from < 5) return null;
            s.minute = rng.Next(from, d0 + 1440);
            s.duration = rng.Next(120, 241);
            s.mag = t.kind == NewsKind.Macro ? Range(0.02, 0.045) : 0;
            return s;
        }

        void FireScheduled()
        {
            for (int i = 0; i < schedule.Count; i++)
            {
                var s = schedule[i];
                if (s.fired || s.minute > minute) continue;
                s.fired = true;
                Fire(s);
            }
        }

        void Fire(ScheduledNews s)
        {
            var t = NewsCatalog.All[s.template];
            switch (t.kind)
            {
                case NewsKind.CompanyGood:
                case NewsKind.CompanyBad:
                case NewsKind.CoinGood:
                case NewsKind.CoinBad:
                {
                    var a = assets[s.asset];
                    if (a.Delisted || a.status == ListingStatus.Liquidation) return;
                    StartEvent(a, s.mag, s.duration);
                    bool breaking = Math.Abs(s.mag) >= (a.IsCoin ? 0.16 : 0.10);
                    string head = string.Format(t.text, a.name);
                    Publish((breaking ? "[속보] " : "") + head, t.IsGood ? NewsTone.Good : NewsTone.Bad, a, breaking);
                    break;
                }
                case NewsKind.SectorGood:
                case NewsKind.SectorBad:
                    foreach (var a in assets)
                    {
                        if (a.Delisted || a.status == ListingStatus.Liquidation) continue;
                        if (Array.IndexOf(t.sectors, a.sector) < 0) continue;
                        StartEvent(a, s.mag * Range(0.7, 1.25), s.duration);
                    }
                    Publish(t.text, t.IsGood ? NewsTone.Good : NewsTone.Bad, null, false);
                    break;
                case NewsKind.Macro:
                    foreach (var a in assets)
                    {
                        if (a.Delisted || a.status == ListingStatus.Liquidation) continue;
                        float mult = a.kind == AssetKind.Domestic ? t.dom : a.kind == AssetKind.Overseas ? t.ovs : t.coin;
                        if (mult == 0) continue;
                        double m = s.mag * Math.Abs(mult) * Clamp(a.beta, 0.5, 1.6);
                        StartEvent(a, mult > 0 ? m : -m, s.duration);
                    }
                    Publish(t.text, NewsTone.Macro, null, t.text.StartsWith("[속보]"));
                    break;
                default:
                    Publish(t.text, NewsTone.Info, null, false);
                    break;
            }
        }

        // ───────────────────────── 대폭락장 / 대호황장 ─────────────────────────

        static readonly string[] CrashNews =
        {
            "[긴급] 글로벌 증시 블랙먼데이… 대폭락장 진입",
            "[긴급] 초대형 헤지펀드 파산… 전 세계 시장 동반 폭락",
            "[긴급] 신용위기 공포 확산… 패닉셀 쏟아진다",
            "[긴급] 거대 은행 뱅크런 사태… 금융 시스템 흔들",
            "[긴급] 전면전 위기 고조… 위험자산 대탈출"
        };
        static readonly string[] BoomNews =
        {
            "[특보] 역사적 유동성 랠리… 대호황장 개막",
            "[특보] AI 산업혁명 기대감 폭발… 전 종목 불기둥",
            "[특보] 전쟁 종식 선언… 위험자산 일제히 폭등",
            "[특보] 중앙은행 무제한 양적완화… 시장은 축제",
            "[특보] 인플레이션 완전 정복 선언… 사상 최대 랠리"
        };

        /// <summary>매일 자정에 낮은 확률로 오늘 중 대폭락/대호황을 예약한다.</summary>
        void PlanRegime()
        {
            if (regime != 0 || regimeStart >= 0 || Day < 3 || DayIndex - lastRegimeDay < 5) return;
            double r = rng.NextDouble();
            int type = r < CrashChance ? -1 : (r < CrashChance + BoomChance ? 1 : 0);
            if (type == 0) return;
            int d0 = DayIndex * 1440;
            regimeStart = Weekday(DayIndex) ? d0 + DomOpen + rng.Next(20, 240) : d0 + rng.Next(10 * 60, 20 * 60);
            regimePending = type;
        }

        void UpdateRegime()
        {
            if (regimeStart >= 0 && minute >= regimeStart)
            {
                int t = regimePending;
                regimeStart = -1;
                regimePending = 0;
                StartRegime(t);
            }
            if (regime == 0) return;
            if (minute >= regimeEnd) { EndRegime(); return; }
            if (DayIndex != regimeWaveDay && MinuteOfDay == DomOpen) SecondWave();
        }

        /// <summary>디버그/테스트용: 즉시 발생.</summary>
        public void ForceRegime(int type)
        {
            if (regime != 0) EndRegime();
            StartRegime(type);
        }

        void StartRegime(int type)
        {
            regime = type;
            lastRegimeDay = DayIndex;
            regimeWaveDay = DayIndex;
            int days = rng.Next(1, 3);
            regimeEnd = (DayIndex + days) * 1440 + DomClose;
            volMult = type < 0 ? 1.8 : 1.45;
            foreach (var a in assets)
            {
                if (a.Delisted || a.status == ListingStatus.Liquidation) continue;
                double m = (a.IsCoin ? Range(0.22, 0.42) : Range(0.10, 0.24)) * Clamp(a.beta, 0.6, 1.5);
                m = type < 0 ? -Math.Min(m, 0.6) : m;
                StartEvent(a, m, a.IsCoin ? rng.Next(300, 480) : rng.Next(200, 330));
            }
            var pool = type < 0 ? CrashNews : BoomNews;
            Publish(pool[rng.Next(pool.Length)], NewsTone.Macro, null, true);
            if (RegimeChanged != null) RegimeChanged(regime);
        }

        void SecondWave()
        {
            regimeWaveDay = DayIndex;
            foreach (var a in assets)
            {
                if (a.Delisted || a.status == ListingStatus.Liquidation) continue;
                double m = Range(0.04, 0.12) * (a.IsCoin ? 1.5 : 1.0);
                StartEvent(a, regime < 0 ? -m : m, 240);
            }
            Publish(regime < 0 ? "[속보] 폭락 이틀째… 투매 행렬 멈추지 않는다" : "[속보] 랠리 이틀째… 신고가 행진 계속",
                NewsTone.Macro, null, true);
        }

        void EndRegime()
        {
            int was = regime;
            regime = 0;
            volMult = 1;
            foreach (var a in assets)
            {
                if (a.Delisted || a.status == ListingStatus.Liquidation) continue;
                double m = Range(0.02, 0.06);
                StartEvent(a, was < 0 ? m : -m, 180);   // 반등 / 숨 고르기
            }
            Publish(was < 0 ? "[시황] 공포 진정… 저가 매수세 유입되며 반등" : "[시황] 과열 식으며 숨 고르기… 차익 실현 매물",
                NewsTone.Macro, null, false);
            if (RegimeChanged != null) RegimeChanged(0);
        }

        // ───────────────────────── 상장폐지 ─────────────────────────

        void ProcessListingStatus()
        {
            int today = DayIndex;
            bool wk = Weekday(today);
            int active = 0;
            foreach (var a in assets)
            {
                if (a.status == ListingStatus.Warning && today > a.statusDay && (a.IsCoin || wk))
                {
                    if (rng.NextDouble() < 0.6)
                    {
                        a.status = ListingStatus.Liquidation;
                        a.statusDay = today;
                        StartEvent(a, -Range(0.55, 0.8), a.IsCoin ? 600 : 330, true);
                        Publish(a.IsCoin
                                ? "[공지] " + a.name + ", 거래지원 종료 확정… 오늘 중 상장폐지"
                                : "[공시] " + a.name + ", 상장폐지 결정… 다음 장 하루 정리매매 후 퇴출",
                            NewsTone.Notice, a, true);
                    }
                    else
                    {
                        a.status = ListingStatus.Normal;
                        StartEvent(a, Range(0.08, 0.15), 180, true);
                        Publish((a.IsCoin ? "[공지] " : "[공시] ") + a.name + (a.IsCoin ? ", 유의 종목 지정 해제" : ", 관리종목 지정 해제… 상장 유지 확정"),
                            NewsTone.Good, a, false);
                    }
                }
                if (a.status == ListingStatus.Warning || a.status == ListingStatus.Liquidation) active++;
            }

            if (active < 3 && Day >= 2 && rng.NextDouble() < 0.45)
            {
                MarketAsset pick = null;
                double worst = double.MaxValue;
                for (int i = 0; i < 10; i++)
                {
                    var a = assets[rng.Next(assets.Count)];
                    if (a.status != ListingStatus.Normal) continue;
                    if (a.IsStock && !wk) continue;
                    double ratio = a.price / a.initPrice;
                    if (ratio < worst) { worst = ratio; pick = a; }
                }
                if (pick != null)
                {
                    pick.status = ListingStatus.Warning;
                    pick.statusDay = today;
                    StartEvent(pick, -Range(0.1, 0.18), 240, true);
                    Publish(pick.IsCoin
                            ? "[공지] " + pick.name + ", 투자 유의 종목 지정… 거래지원 종료 검토"
                            : "[공시] " + pick.name + ", 관리종목 지정… 상장폐지 심사 착수",
                        NewsTone.Notice, pick, true);
                }
            }
        }

        void CheckDelistings()
        {
            foreach (var a in assets)
            {
                if (a.status != ListingStatus.Liquidation || a.evActive) continue;
                if (a.IsStock && OpenAt(a.kind, minute)) continue;
                a.status = ListingStatus.Delisted;
                a.evActive = false;
                Publish("[상장폐지] " + a.name + " 오늘부로 " + (a.IsCoin ? "거래지원 종료" : "상장폐지") + "… 보유분은 휴지 조각으로",
                    NewsTone.Notice, a, true);
                if (AssetDelisted != null) AssetDelisted(a);
            }
        }

        // ───────────────────────── 지수 ─────────────────────────

        public double Index(AssetKind kind, out double changePct)
        {
            double sum = 0, chg = 0;
            int n = 0;
            foreach (var a in assets)
            {
                if (a.kind != kind || a.Delisted) continue;
                sum += a.price / a.initPrice;
                chg += a.ChangePct;
                n++;
            }
            if (n == 0) { changePct = 0; return 0; }
            changePct = chg / n;
            double baseV = kind == AssetKind.Domestic ? 2650 : kind == AssetKind.Overseas ? 5800 : 1000;
            return baseV * sum / n;
        }

        // ───────────────────────── 발행 ─────────────────────────

        void Publish(string headline, NewsTone tone, MarketAsset asset, bool breaking)
        {
            if (suppressNews) return;
            var n = new NewsItem { minute = minute, headline = headline, tone = tone, asset = asset, breaking = breaking };
            news.Add(n);
            if (news.Count > 120) news.RemoveAt(0);
            if (NewsPublished != null) NewsPublished(n);
        }

        // ───────────────────────── 저장 / 불러오기 ─────────────────────────

        public void Capture(SaveData d)
        {
            d.minute = minute;
            d.seed = seed;
            d.domFactor = domFactor; d.ovsFactor = ovsFactor; d.cryFactor = cryFactor;
            d.regime = regime; d.regimeEnd = regimeEnd; d.regimeStart = regimeStart; d.regimePending = regimePending;
            d.lastRegimeDay = lastRegimeDay; d.regimeWaveDay = regimeWaveDay; d.volMult = volMult;
            d.assets = new List<AssetSave>();
            foreach (var a in assets)
            {
                d.assets.Add(new AssetSave
                {
                    price = a.price, prevClose = a.prevClose, anchor = a.anchor, pendingGap = a.pendingGap,
                    evActive = a.evActive, evElapsed = a.evElapsed, evDuration = a.evDuration,
                    evStartLog = a.evStartLog, evTargetLog = a.evTargetLog,
                    status = (int)a.status, statusDay = a.statusDay,
                    limitUp = a.limitUpNotified, limitDown = a.limitDownNotified
                });
            }
            d.news = new List<NewsSave>();
            int start = Math.Max(0, news.Count - 60);
            for (int i = start; i < news.Count; i++)
            {
                var n = news[i];
                d.news.Add(new NewsSave { minute = n.minute, headline = n.headline, tone = (int)n.tone, asset = n.asset != null ? n.asset.index : -1, breaking = n.breaking });
            }
            d.schedule = new List<ScheduledSave>();
            foreach (var s in schedule)
                d.schedule.Add(new ScheduledSave { minute = s.minute, template = s.template, asset = s.asset, duration = s.duration, mag = s.mag, fired = s.fired });
        }

        public void Restore(SaveData d)
        {
            minute = d.minute;
            seed = d.seed;
            rng = new Random(d.seed ^ (d.minute * 7919));
            domFactor = d.domFactor; ovsFactor = d.ovsFactor; cryFactor = d.cryFactor;
            if (d.version >= 3)
            {
                regime = d.regime; regimeEnd = d.regimeEnd; regimeStart = d.regimeStart; regimePending = d.regimePending;
                lastRegimeDay = d.lastRegimeDay; regimeWaveDay = d.regimeWaveDay; volMult = d.volMult > 0 ? d.volMult : 1;
            }
            for (int i = 0; i < assets.Count && i < d.assets.Count; i++)
            {
                var a = assets[i];
                var s = d.assets[i];
                a.price = s.price; a.prevClose = s.prevClose; a.anchor = s.anchor; a.pendingGap = s.pendingGap;
                a.evActive = s.evActive; a.evElapsed = s.evElapsed; a.evDuration = s.evDuration;
                a.evStartLog = s.evStartLog; a.evTargetLog = s.evTargetLog;
                a.status = (ListingStatus)s.status; a.statusDay = s.statusDay;
                a.limitUpNotified = s.limitUp; a.limitDownNotified = s.limitDown;
                SynthesizeHistory(a);
            }
            news.Clear();
            foreach (var n in d.news)
                news.Add(new NewsItem { minute = n.minute, headline = n.headline, tone = (NewsTone)n.tone, asset = n.asset >= 0 ? assets[n.asset] : null, breaking = n.breaking });
            schedule.Clear();
            foreach (var s in d.schedule)
                schedule.Add(new ScheduledNews { minute = s.minute, template = s.template, asset = s.asset, duration = s.duration, mag = s.mag, fired = s.fired });
            domWasOpen = DomesticOpen;
            ovsWasOpen = OverseasOpen;
        }

        /// <summary>불러오기 시 차트가 비지 않도록, 현재가에서 거꾸로 걸어가며 과거 1분 캔들을 만든다.</summary>
        void SynthesizeHistory(MarketAsset a)
        {
            a.candles.Clear();
            var tmp = new List<Candle>(900);
            double p = a.price;
            double sd = a.sigma / Math.Sqrt(SubSteps);
            for (int m = minute; m > minute - 6 * 1440 && tmp.Count < 900; m--)
            {
                if (!OpenAt(a.kind, m)) continue;
                double c = p, h = p, l = p;
                for (int s = 0; s < SubSteps; s++)
                {
                    p *= Math.Exp(-sd * Gauss());
                    if (p > h) h = p;
                    if (p < l) l = p;
                }
                double vol = a.baseVolume * (0.4 + rng.NextDouble() * 0.8) * (1 + Math.Abs(Math.Log(c / p)) / a.sigma * 0.9);
                tmp.Add(new Candle { time = m, open = p, high = h, low = l, close = c, volume = vol });
            }
            for (int i = tmp.Count - 1; i >= 0; i--) a.AddCandle(tmp[i]);
        }

        // ───────────────────────── 유틸 ─────────────────────────

        double Gauss()
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        double Range(double a, double b) { return a + (b - a) * rng.NextDouble(); }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}

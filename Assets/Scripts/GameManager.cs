using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BuyOrDie
{
    public enum GameState { Intro, Playing, Over }

    /// <summary>
    /// 메인 게임 총괄: 시작 방식(새 게임 + 튜토리얼 / 이어하기), 시간 흐름(배속), 시장 엔진, 계좌,
    /// 상장폐지 정산, 자동 저장, 승패 판정, 알림. UI는 실행 시 코드로 조립된다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;

        public const double GoalEquity = 1000000000;
        public const double DieEquity = 10000;
        public static readonly int[] Speeds = { 0, 1, 2, 5 };

        [Tooltip("주식 장이 열려 있을 때 게임 1분이 현실 몇 초인지 (1배속)")]
        public float secondsPerMinuteOpen = 0.25f;
        [Tooltip("평일 장 마감 시간대 (코인만 거래)")]
        public float secondsPerMinuteClosed = 0.06f;
        [Tooltip("주말 (주식 휴장)")]
        public float secondsPerMinuteWeekend = 0.03f;

        public MarketEngine engine;
        public Portfolio portfolio;
        public MarketAsset selected;
        public GameState state = GameState.Intro;
        public int speedIndex = 1;
        public bool goalReached;
        public Sfx sfx;
        public double dayStartEquity;

        GameUI ui;
        float acc;
        readonly List<Position> liqBuffer = new List<Position>();

        public int Speed { get { return Speeds[speedIndex]; } }

        static readonly double[] RankAt = { 0, 5e6, 2e7, 5e7, 1.5e8, 4e8, 1e9 };
        static readonly string[] RankName = { "깡통 계좌", "개미", "불개미", "슈퍼개미", "큰손", "고래", "월가의 늑대" };

        public static string Rank(double equity, out string next, out double progress)
        {
            int i = 0;
            while (i + 1 < RankAt.Length && equity >= RankAt[i + 1]) i++;
            if (i + 1 < RankAt.Length)
            {
                next = RankName[i + 1];
                progress = (equity - RankAt[i]) / (RankAt[i + 1] - RankAt[i]);
            }
            else { next = null; progress = 1; }
            return RankName[i];
        }

        // ───────────────────────── 시작 ─────────────────────────

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            sfx = Sfx.I;

            bool loaded = false;
            if (GameLaunch.mode == LaunchMode.Load)
            {
                var data = SaveSystem.Load();
                if (data != null) { LoadMarket(data); loaded = true; }
            }
            if (!loaded) NewMarket();

            ui = gameObject.AddComponent<GameUI>();
            ui.Build(this);
            state = GameState.Playing;
            ui.OnSelectionChanged();
            ui.RefreshAll();

            if (!GameLaunch.fromTitle) SceneFader.RevealOnStart();

            if (loaded)
            {
                speedIndex = Mathf.Clamp(Settings.DefaultSpeed, 1, 3);
                ui.ShowDayBanner();
                Toast("이어하기", Fmt.FullDate(engine.minute) + " " + Fmt.Clock(engine.minute) + " 시점부터 다시 시작합니다.", Theme.Accent);
            }
            else
            {
                speedIndex = 0;
                ui.RefreshTaskbar();
                ui.tutorial.Begin(ui, this, FinishTutorial);
            }
            GameLaunch.mode = LaunchMode.Load;
        }

        void FinishTutorial()
        {
            speedIndex = Mathf.Clamp(Settings.DefaultSpeed, 1, 3);
            ui.ShowDayBanner();
            ui.RefreshAll();
            Toast("거래 시작", "국내 정규장은 09:00에 열려요. 코인은 지금 바로 거래할 수 있어요.", Theme.Accent);
            SaveGame(false);
        }

        void HookEngine()
        {
            engine.NewsPublished += OnNews;
            engine.SessionOpened += OnSessionOpened;
            engine.SessionClosed += OnSessionClosed;
            engine.DayStarted += OnDayStarted;
            engine.PriceLimitHit += OnPriceLimit;
            engine.AssetDelisted += OnDelisted;
        }

        void NewMarket()
        {
            engine = new MarketEngine(Environment.TickCount);
            engine.Warmup();
            portfolio = new Portfolio();
            selected = engine.assets[0];
            goalReached = false;
            dayStartEquity = portfolio.Equity;
            HookEngine();
        }

        void LoadMarket(SaveData d)
        {
            engine = new MarketEngine(d.seed);
            engine.Restore(d);
            portfolio = new Portfolio();
            portfolio.Restore(d.portfolio, engine.assets);
            selected = engine.assets[Mathf.Clamp(d.selected, 0, engine.assets.Count - 1)];
            goalReached = d.goalReached;
            dayStartEquity = d.dayStartEquity > 0 ? d.dayStartEquity : portfolio.Equity;
            HookEngine();
        }

        public void Restart()
        {
            SaveSystem.Delete();
            GameLaunch.mode = LaunchMode.New;
            GameLaunch.fromTitle = true;
            SceneFader.Load(SceneFader.GameScene, "새 시장을 여는 중");
        }

        public void ExitToTitle()
        {
            if (state == GameState.Playing) SaveGame(false);
            GameLaunch.fromTitle = true;
            SceneFader.Load(SceneFader.TitleScene, "타이틀로 돌아가는 중");
        }

        public void ContinueAfterGoal()
        {
            ui.HideOverlays();
            speedIndex = Mathf.Max(1, speedIndex);
        }

        public void SetSpeed(int index)
        {
            speedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
            ui.RefreshTaskbar();
        }

        public void Select(MarketAsset a)
        {
            if (a == null) return;
            selected = a;
            ui.OnSelectionChanged();
        }

        // ───────────────────────── 저장 ─────────────────────────

        public void SaveGame(bool announce)
        {
            if (state != GameState.Playing) return;
            var d = new SaveData();
            engine.Capture(d);
            d.portfolio = portfolio.Capture();
            d.day = engine.Day;
            d.equity = portfolio.Equity;
            d.dayStartEquity = dayStartEquity;
            d.goalReached = goalReached;
            d.selected = selected != null ? selected.index : 0;
            SaveSystem.Save(d);
            SaveBest();
            if (announce)
            {
                sfx.Profit();
                Toast("저장 완료", Fmt.FullDate(engine.minute) + " " + Fmt.Clock(engine.minute) + "  ·  총자산 " + Fmt.Short(d.equity) + "원", Theme.Live);
            }
        }

        void OnApplicationQuit()
        {
            if (state == GameState.Playing && engine != null) SaveGame(false);
        }

        // ───────────────────────── 루프 ─────────────────────────

        float Interval()
        {
            if (engine.AnyStockOpen) return secondsPerMinuteOpen;
            bool weekend = !MarketEngine.Weekday(engine.DayIndex) && !engine.OverseasOpen;
            return weekend ? secondsPerMinuteWeekend : secondsPerMinuteClosed;
        }

        void Update()
        {
            HandleKeys();
            if (state != GameState.Playing || Speed == 0 || ui.OverlayOpen || SceneFader.Busy) return;

            acc += Time.unscaledDeltaTime * Speed;
            int steps = 0;
            while (steps < 80)
            {
                float interval = Interval();
                if (acc < interval) break;
                acc -= interval;
                StepOnce();
                steps++;
                if (state != GameState.Playing || ui.OverlayOpen) { acc = 0; break; }
            }
            if (steps >= 80) acc = 0;
            if (steps > 0) ui.RefreshAll();
        }

        void StepOnce()
        {
            engine.Step();

            liqBuffer.Clear();
            foreach (var a in engine.assets)
            {
                if (a.candles.Count == 0 || portfolio.PositionCount(a) == 0) continue;
                var c = a.candles[a.candles.Count - 1];
                if (c.time != engine.minute) continue;
                portfolio.CheckLiquidations(a, c.low, c.high, engine.minute, liqBuffer);
            }
            foreach (var p in liqBuffer)
            {
                sfx.Liquidated();
                ui.Shake();
                Toast("강제 청산", p.asset.name + " " + (p.isLong ? "롱" : "숏") + " x" + p.leverage + " 포지션이 청산되어 증거금 " +
                      Fmt.Won(p.margin) + "원을 잃었습니다.", Theme.Liq);
            }

            portfolio.UpdatePeak();
            double eq = portfolio.Equity;
            if (eq < DieEquity && portfolio.positions.Count == 0) { GameOver(); return; }
            if (!goalReached && eq >= GoalEquity)
            {
                goalReached = true;
                SaveBest();
                sfx.Win();
                ui.ShowWin();
            }
        }

        void GameOver()
        {
            state = GameState.Over;
            SaveBest();
            SaveSystem.Delete();
            sfx.Lose();
            ui.RefreshAll();
            ui.ShowGameOver();
        }

        // ───────────────────────── 거래 ─────────────────────────

        string Guard(MarketAsset a)
        {
            if (state != GameState.Playing) return "게임이 진행 중이 아닙니다";
            if (a.Delisted) return "상장폐지된 종목입니다";
            if (!engine.IsTradable(a)) return OrderView.ClosedReason(engine, a, false);
            return null;
        }

        public string BuySpot(double qty)
        {
            var a = selected;
            string err = Guard(a) ?? portfolio.BuySpot(a, qty, engine.minute);
            if (err != null) return err;
            sfx.Buy();
            Toast("매수 체결", a.name + " " + Fmt.Qty(qty, a.qtyDecimals) + a.Unit + " · " + Fmt.Price(a.price) + "원", Theme.Up);
            ui.RefreshAll();
            return null;
        }

        public string SellSpot(double qty)
        {
            var a = selected;
            var h = portfolio.GetHolding(a);
            double avg = h != null ? h.avg : 0;
            string err = Guard(a) ?? portfolio.SellSpot(a, qty, engine.minute);
            if (err != null) return err;
            sfx.Sell();
            double pnl = (a.price - avg) * qty;
            Toast("매도 체결", a.name + " " + Fmt.Qty(qty, a.qtyDecimals) + a.Unit + " · 손익 " +
                  Fmt.Colored(Fmt.SignedWon(pnl) + "원", Theme.Change(pnl)), Theme.Down);
            ui.RefreshAll();
            return null;
        }

        public void SellAll(MarketAsset a)
        {
            var h = portfolio.GetHolding(a);
            if (h == null) return;
            selected = a;
            string err = SellSpot(h.qty);
            if (err != null) Toast("매도 불가", err, Theme.Accent);
            ui.OnSelectionChanged();
        }

        public string OpenPosition(bool isLong, int lev, double margin)
        {
            var a = selected;
            Position p = null;
            string err = Guard(a) ?? portfolio.OpenPosition(a, isLong, lev, margin, engine.minute, out p);
            if (err != null) return err;
            if (isLong) sfx.Buy(); else sfx.Sell();
            Toast(isLong ? "롱 진입" : "숏 진입", a.name + " x" + lev + " · 증거금 " + Fmt.Won(margin) + "원 · 청산가 " + Fmt.Price(p.liq),
                isLong ? Theme.Up : Theme.Down);
            ui.RefreshAll();
            return null;
        }

        public void ClosePosition(Position p)
        {
            string err = Guard(p.asset);
            if (err != null) { Toast("청산 불가", err, Theme.Accent); return; }
            double pnl = portfolio.ClosePosition(p, engine.minute);
            if (pnl >= 0) sfx.Profit(); else sfx.Sell();
            Toast("포지션 청산", p.asset.name + " " + (p.isLong ? "롱" : "숏") + " x" + p.leverage + " · 실현 " +
                  Fmt.Colored(Fmt.SignedWon(pnl) + "원", Theme.Change(pnl)), Theme.Change(pnl));
            ui.RefreshAll();
        }

        public void CloseAllPositions()
        {
            double total = 0;
            int n = 0, skipped = 0;
            foreach (var p in new List<Position>(portfolio.positions))
            {
                if (!engine.IsTradable(p.asset)) { skipped++; continue; }
                total += portfolio.ClosePosition(p, engine.minute);
                n++;
            }
            if (n > 0)
            {
                if (total >= 0) sfx.Profit(); else sfx.Sell();
                Toast("전체 청산", n + "개 포지션 · 실현 " + Fmt.Colored(Fmt.SignedWon(total) + "원", Theme.Change(total)) +
                      (skipped > 0 ? " (휴장 종목 " + skipped + "개 제외)" : ""), Theme.Change(total));
            }
            else if (skipped > 0) Toast("청산 불가", "휴장 중인 시장의 포지션은 개장 후 청산할 수 있어요", Theme.Accent);
            ui.RefreshAll();
        }

        // ───────────────────────── 시장 이벤트 ─────────────────────────

        void OnNews(NewsItem n)
        {
            ui.OnNews(n);
            bool mine = n.asset != null && portfolio.Owns(n.asset);
            int mode = Settings.NewsAlerts;
            bool show = n.tone == NewsTone.Notice && mine;               // 내 종목 공시는 항상
            if (mode == 0) show |= n.breaking || mine;
            else if (mode == 1) show |= mine;
            if (!show) return;
            Color c;
            string tone = NewsView.ToneLabel(n.tone, out c);
            sfx.News();
            Toast((mine ? "보유 종목 " : "") + tone, n.headline, c);
        }

        void OnSessionOpened(AssetKind k)
        {
            if (k == AssetKind.Domestic)
            {
                dayStartEquity = portfolio.Equity;
                Toast("국내 정규장 개장", Fmt.Date(engine.minute) + " 09:00 — 밤사이 뉴스가 시가에 반영됐어요.", Theme.Live);
            }
            else Toast("해외 정규장 개장", "22:30 (한국 시간) — 해외 주식 거래가 시작됐어요. 05:00 마감.", Theme.Live);
            sfx.News();
        }

        void OnSessionClosed(AssetKind k)
        {
            if (k == AssetKind.Domestic)
            {
                double d = portfolio.Equity - dayStartEquity;
                double pct = dayStartEquity > 0 ? d / dayStartEquity * 100 : 0;
                Toast("국내 장 마감 · DAY " + engine.Day, "오늘 손익 " + Fmt.Colored(Fmt.SignedWon(d) + "원 (" + Fmt.Pct(pct) + ")", Theme.Change(d)) +
                      "\n해외장은 22:30에 열려요.", Theme.Sub);
            }
            else Toast("해외 장 마감", "05:00 — 해외 주식 거래가 끝났어요.", Theme.Sub);
        }

        void OnDayStarted()
        {
            sfx.DayChime();
            ui.ShowDayBanner();
            if (Settings.AutoSave) SaveGame(false);
            SaveBest();
        }

        void OnPriceLimit(MarketAsset a, bool up)
        {
            if (!portfolio.Owns(a) && Settings.NewsAlerts != 0) return;
            Toast(up ? "상한가!" : "하한가!", a.name + (up ? " +30% 상한가 도달" : " -30% 하한가 도달"), up ? Theme.Up : Theme.Down);
        }

        void OnDelisted(MarketAsset a)
        {
            double spotLoss;
            double posPnl = portfolio.HandleDelisting(a, engine.minute, out spotLoss);
            if (spotLoss > 0 || Math.Abs(posPnl) > 0)
            {
                sfx.Liquidated();
                ui.Shake();
                string body = a.name + " 상장폐지";
                if (spotLoss > 0) body += " · 보유분 " + Fmt.Colored("-" + Fmt.Won(spotLoss) + "원", Theme.Down) + " 손실";
                if (Math.Abs(posPnl) > 0) body += " · 포지션 정산 " + Fmt.Colored(Fmt.SignedWon(posPnl) + "원", Theme.Change(posPnl));
                Toast("상장폐지 정산", body, Theme.Liq);
            }
            ui.watchlist.Rebind();
        }

        public void Toast(string title, string body, Color c)
        {
            if (ui != null) ui.Toast(title, body, c);
        }

        // ───────────────────────── 기록 ─────────────────────────

        public static double BestEquity
        {
            get { double v; return double.TryParse(PlayerPrefs.GetString("bod_best_equity", "0"), out v) ? v : 0; }
        }

        public static int BestDay { get { return PlayerPrefs.GetInt("bod_best_day", 0); } }

        void SaveBest()
        {
            if (portfolio.peakEquity > BestEquity) PlayerPrefs.SetString("bod_best_equity", portfolio.peakEquity.ToString("R"));
            if (engine.Day > BestDay) PlayerPrefs.SetInt("bod_best_day", engine.Day);
            PlayerPrefs.Save();
        }

        // ───────────────────────── 단축키 ─────────────────────────

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || state != GameState.Playing || ui == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (ui.tutorial != null && ui.tutorial.Active) return;
                ui.TogglePauseMenu();
                return;
            }
            if (ui.OverlayOpen) return;
            if (kb.spaceKey.wasPressedThisFrame) SetSpeed(speedIndex == 0 ? Mathf.Clamp(Settings.DefaultSpeed, 1, 3) : 0);
            if (kb.digit1Key.wasPressedThisFrame) SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) SetSpeed(3);
            if (kb.upArrowKey.wasPressedThisFrame) ui.watchlist.SelectRelative(-1);
            if (kb.downArrowKey.wasPressedThisFrame) ui.watchlist.SelectRelative(1);
            if (kb.tabKey.wasPressedThisFrame) ui.watchlist.CycleTab();
        }
    }
}

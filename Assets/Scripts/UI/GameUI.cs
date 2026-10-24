using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 메인 게임 화면을 "내 PC 데스크톱" 시점으로 조립한다.
    /// 바탕화면 + HTS 창(타이틀바 / 자산 요약 + 장 시간 / 3단 본문 / 속보 티커) + 작업표시줄 + 알림 + 모달.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        const float TaskbarH = 46, Margin = 14, TitleH = 36, SummaryH = 76, TickerH = 30;
        const float LeftW = 330, RightW = 372, BottomH = 260, OrderH = 472;

        GameManager gm;
        public RectTransform root;
        RectTransform window;
        Vector2 windowBase;
        float shakeT;

        public WatchlistView watchlist;
        public ChartView chart;
        public OrderView order;
        public PortfolioView portfolioView;
        public NewsView news;

        // 튜토리얼이 가리킬 영역
        public RectTransform summaryRT, marketStripRT, watchlistRT, chartRT, orderRT, portfolioRT, newsRT, speedRT, menuRT;

        // 타이틀바 / 요약
        TextMeshProUGUI dateT, statusT;
        TextMeshProUGUI equityT, returnT, cashT, spotT, posT, realT, rankT, rankNextT;
        Image rankFill;
        readonly TextMeshProUGUI[] mktStatus = new TextMeshProUGUI[3];
        readonly Image[] mktFill = new Image[3];

        // 작업표시줄
        readonly List<UIButton> speedBtns = new List<UIButton>();
        TextMeshProUGUI clockT, dayT;
        Image tickFill;
        int shownMinute = -1;
        UIButton soundBtn;

        // 티커
        RectTransform tickerMask;
        TextMeshProUGUI tickerT;
        float tickerX;

        // 알림
        RectTransform toastRoot;
        class ToastItem { public RectTransform rt; public CanvasGroup cg; public float born; public float y; }
        readonly List<ToastItem> toasts = new List<ToastItem>();
        public float toastLife = 4.8f;

        // 날짜 배너
        CanvasGroup bannerCg;
        TextMeshProUGUI bannerDay, bannerDate, bannerSub;
        float bannerT = -1;

        // 모달
        RectTransform overlay;      // 승리/파산
        RectTransform pauseMenu;
        int modalCount;
        public TutorialOverlay tutorial;
        int lastModalCloseFrame = -1;

        // 대폭락장 / 대호황장 경보
        CanvasGroup alertCg;
        TextMeshProUGUI alertTitle, alertSub, alertTip;
        Image alertBar, regimeStrip;
        float alertT = -1;

        public bool OverlayOpen
        {
            get { return overlay != null || pauseMenu != null || modalCount > 0 || (tutorial != null && tutorial.Active); }
        }

        // ───────────────────────── 조립 ─────────────────────────

        public void Build(GameManager gm)
        {
            this.gm = gm;
            EnsureEventSystem();
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Theme.Desktop;
                cam.orthographic = true;
            }

            root = (RectTransform)Desktop.MakeCanvas("UI Canvas").transform;
            Desktop.Build(root);
            BuildWindow();
            BuildTaskbar();
            BuildBanner();
            BuildAlert();
            toastRoot = UI.Rect("Toasts", root);
            toastRoot.anchorMin = toastRoot.anchorMax = toastRoot.pivot = new Vector2(1, 0);
            toastRoot.anchoredPosition = new Vector2(-Margin - 18, TaskbarH + Margin + TickerH + 14);
            toastRoot.sizeDelta = new Vector2(380, 10);
            tutorial = root.gameObject.AddComponent<TutorialOverlay>();
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        void BuildWindow()
        {
            var frame = UI.Img("HTS Window", root, Theme.Line);
            window = frame.rectTransform.Fill(Margin, Margin, Margin, TaskbarH + Margin);
            windowBase = window.anchoredPosition;
            var inner = UI.Img("Inner", window, Theme.Window).rectTransform.Fill(1, 1, 1, 1);

            // 타이틀바
            var title = UI.Img("TitleBar", inner, Theme.Hex("0A0E13")).rectTransform.Top(0, TitleH);
            UI.Txt(title, Fmt.Colored("BUY", Theme.Up) + Fmt.Colored(" OR ", Theme.Dim) + Fmt.Colored("DIE", Theme.Down), 15, Theme.Text, true)
                .rectTransform.At(16, 0, 140, TitleH);
            UI.Txt(title, "HTS 트레이딩 터미널   ·   개인 계좌 7700-1004", 12, Theme.Dim).rectTransform.At(128, 0, 420, TitleH);
            dateT = UI.Txt(title, "", 14, Theme.Text, true, TextAlignmentOptions.Center);
            dateT.rectTransform.Center(520, TitleH);
            statusT = UI.Txt(title, "", 12, Theme.Accent, true, TextAlignmentOptions.MidlineRight);
            statusT.rectTransform.AtRight(200, 0, 420, TitleH);
            UI.Txt(title, "—        □        ×", 13, Theme.Dim, false, TextAlignmentOptions.MidlineRight).rectTransform.AtRight(18, 0, 160, TitleH);
            UI.HLine(inner, TitleH);
            regimeStrip = UI.Img("RegimeStrip", inner, Theme.Up);
            regimeStrip.rectTransform.Top(TitleH - 2, 3);
            regimeStrip.enabled = false;

            summaryRT = UI.Rect("Summary", inner).Top(TitleH + 1, SummaryH);
            BuildSummary(summaryRT);
            UI.HLine(inner, TitleH + 1 + SummaryH);

            float bodyTop = TitleH + SummaryH + 2;
            var body = UI.Rect("Body", inner).Fill(0, 0, bodyTop, TickerH + 1);

            var left = UI.Img("Watchlist", body, Theme.Panel).rectTransform.Left(0, LeftW);
            UI.Img("Line", body, Theme.Line).rectTransform.Left(LeftW, 1);
            var right = UI.Img("RightColumn", body, Theme.Panel).rectTransform.Right(0, RightW);
            UI.Img("Line", body, Theme.Line).rectTransform.Right(RightW, 1);
            var center = UI.Rect("Center", body).Fill(LeftW + 1, RightW + 1, 0, 0);

            var chartArea = UI.Rect("ChartPanel", center).Fill(0, 0, 0, BottomH + 1);
            UI.Img("Line", center, Theme.Line).rectTransform.Bottom(BottomH, 1);
            var bottom = UI.Rect("PortfolioPanel", center).Bottom(0, BottomH);
            var orderArea = UI.Rect("OrderPanel", right).Top(0, OrderH);
            UI.HLine(right, OrderH);
            var newsArea = UI.Rect("NewsPanel", right).Fill(0, 0, OrderH + 1, 0);

            watchlistRT = left; chartRT = chartArea; portfolioRT = bottom; orderRT = orderArea; newsRT = newsArea;

            watchlist = left.gameObject.AddComponent<WatchlistView>();
            watchlist.Build(left, gm);
            chart = chartArea.gameObject.AddComponent<ChartView>();
            chart.Build(chartArea, gm);
            portfolioView = bottom.gameObject.AddComponent<PortfolioView>();
            portfolioView.Build(bottom, gm);
            order = orderArea.gameObject.AddComponent<OrderView>();
            order.Build(orderArea, gm);
            news = newsArea.gameObject.AddComponent<NewsView>();
            news.Build(newsArea, gm);

            BuildTicker(UI.Rect("Ticker", inner).Bottom(0, TickerH));
            UI.Img("Line", inner, Theme.Line).rectTransform.Bottom(TickerH, 1);
        }

        void BuildSummary(RectTransform s)
        {
            Stat(s, 24, 300, "총 자산", out equityT, 26);
            returnT = UI.Txt(s, "", 15, Theme.Sub, true);
            returnT.rectTransform.At(24, 30, 200, 32);
            Stat(s, 360, 170, "보유 현금", out cashT, 18);
            Stat(s, 545, 160, "현물 평가손익", out spotT, 18);
            Stat(s, 720, 160, "레버리지 미실현", out posT, 18);
            Stat(s, 895, 160, "누적 실현손익", out realT, 18);

            // 장 시간 / 하루 경과
            marketStripRT = UI.Rect("MarketClock", s).At(1080, 8, 470, 60);
            UI.Img("Sep", s, Theme.LineSoft).rectTransform.At(1060, 14, 1, 48);
            string[] labels = { "국내", "해외", "오늘" };
            for (int i = 0; i < 3; i++)
            {
                float y = i * 20;
                UI.Txt(marketStripRT, labels[i], 12, Theme.Sub, true).rectTransform.At(0, y, 40, 20);
                mktStatus[i] = UI.Txt(marketStripRT, "", 12, Theme.Sub);
                mktStatus[i].rectTransform.At(40, y, 250, 20);
                var track = UI.Img("Track", marketStripRT, Theme.PanelHi2);
                track.rectTransform.At(300, y + 9, 160, 3);
                mktFill[i] = UI.Img("Fill", track.transform, Theme.Live);
                var f = mktFill[i].rectTransform;
                f.anchorMin = Vector2.zero; f.anchorMax = new Vector2(0, 1);
                f.offsetMin = f.offsetMax = Vector2.zero;
            }

            UI.Txt(s, "투자자 등급", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight).rectTransform.AtRight(24, 12, 220, 18);
            rankT = UI.Txt(s, "", 20, Theme.Accent, true, TextAlignmentOptions.MidlineRight);
            rankT.rectTransform.AtRight(24, 30, 220, 26);
            var trackR = UI.Img("RankTrack", s, Theme.PanelHi2);
            trackR.rectTransform.AtRight(24, 60, 150, 3);
            rankFill = UI.Img("RankFill", trackR.transform, Theme.Accent);
            rankFill.rectTransform.anchorMin = Vector2.zero;
            rankFill.rectTransform.anchorMax = new Vector2(0, 1);
            rankFill.rectTransform.offsetMin = rankFill.rectTransform.offsetMax = Vector2.zero;
            rankNextT = UI.Txt(s, "", 11, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            rankNextT.rectTransform.AtRight(182, 53, 160, 16);
        }

        void Stat(RectTransform s, float x, float w, string label, out TextMeshProUGUI value, float size)
        {
            UI.Txt(s, label, 12, Theme.Dim).rectTransform.At(x, 12, w, 18);
            value = UI.Txt(s, "", size, Theme.Text, true);
            value.rectTransform.At(x, 32, w, 32);
        }

        void BuildTicker(RectTransform t)
        {
            UI.Img("Bg", t, Theme.Hex("0A0E13")).rectTransform.Fill();
            var label = UI.Img("Label", t, Theme.Hex("FF4757")).rectTransform.Left(0, 64);
            UI.Txt(label, "속보", 12, Color.white, true, TextAlignmentOptions.Center).rectTransform.Fill();
            tickerMask = UI.Rect("Mask", t).Fill(64, 0, 0, 0);
            tickerMask.gameObject.AddComponent<RectMask2D>();
            tickerT = UI.Txt(tickerMask, "", 12, Theme.Sub);
            tickerT.overflowMode = TextOverflowModes.Overflow;
            var rt = tickerT.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(6000, 0);
            UpdateTickerText();
        }

        void BuildTaskbar()
        {
            var bar = UI.Img("Taskbar", root, Theme.Hex("0B0F15")).rectTransform.Bottom(0, TaskbarH);
            UI.Img("Line", bar, Theme.Line).rectTransform.Top(0, 1);

            var start = UI.Img("Start", bar, Theme.A(Theme.Accent, 0.1f)).rectTransform.Left(0, TaskbarH);
            UI.Txt(start, "◆", 18, Theme.Accent, true, TextAlignmentOptions.Center).rectTransform.Fill();
            UI.Txt(bar, "BuyOrDie OS", 12, Theme.Dim).rectTransform.At(TaskbarH + 14, 0, 110, TaskbarH);
            var app = UI.Img("App_HTS", bar, Theme.A(Theme.PanelHi, 0.8f)).rectTransform.At(TaskbarH + 130, 5, 150, TaskbarH - 10);
            UI.Txt(app, "HTS 트레이딩", 13, Theme.Text, true, TextAlignmentOptions.Center).rectTransform.Fill();
            UI.Img("Active", app, Theme.Accent).rectTransform.Bottom(0, 2, 40, 40);

            menuRT = UI.Rect("MenuButtons", bar).At(TaskbarH + 296, 5, 376, TaskbarH - 10);
            var save = UI.GhostButton(menuRT, "저장", 13, () => gm.SaveGame(true), true);
            save.rectTransform().At(0, 0, 70, TaskbarH - 10);
            var hist = UI.GhostButton(menuRT, "내역", 13, OpenHistory, true);
            hist.rectTransform().At(76, 0, 70, TaskbarH - 10);
            var set = UI.GhostButton(menuRT, "설정", 13, OpenSettings, true);
            set.rectTransform().At(152, 0, 70, TaskbarH - 10);
            var newsBtn = UI.GhostButton(menuRT, "뉴스", 13, () => OpenNews(null), true);
            newsBtn.rectTransform().At(228, 0, 70, TaskbarH - 10);
            var menu = UI.GhostButton(menuRT, "메뉴", 13, OpenPauseMenu, true);
            menu.rectTransform().At(304, 0, 70, TaskbarH - 10);

            float x = 18;
            var clock = UI.Rect("Clock", bar).AtRight(x, 0, 150, TaskbarH);
            clockT = UI.Txt(clock, "", 15, Theme.Text, true, TextAlignmentOptions.MidlineRight);
            clockT.rectTransform.Top(5, 20);
            dayT = UI.Txt(clock, "", 11, Theme.Dim, false, TextAlignmentOptions.MidlineRight);
            dayT.rectTransform.Top(25, 16);
            // 다음 시세 갱신까지 남은 시간 (얇은 막대)
            var tickTrack = UI.Img("TickTrack", clock, Theme.LineSoft);
            tickTrack.rectTransform.Bottom(3, 2, 60, 0);
            tickFill = UI.Img("TickFill", tickTrack.transform, Theme.Accent);
            tickFill.rectTransform.anchorMin = Vector2.zero;
            tickFill.rectTransform.anchorMax = new Vector2(0, 1);
            tickFill.rectTransform.offsetMin = tickFill.rectTransform.offsetMax = Vector2.zero;
            x += 150 + 14;

            soundBtn = UI.GhostButton(bar, "♪", 16, ToggleSound, true);
            soundBtn.rectTransform().AtRight(x, 5, 40, TaskbarH - 10);
            x += 40 + 14;
            UI.Img("Sep", bar, Theme.Line).rectTransform.AtRight(x - 7, 12, 1, TaskbarH - 24);

            speedRT = UI.Rect("Speed", bar).AtRight(x, 0, 4 * 46 + 46, TaskbarH);
            string[] labels = { "5x", "2x", "1x", "II" };
            int[] idx = { 3, 2, 1, 0 };
            for (int i = 0; i < labels.Length; i++)
            {
                int k = idx[i];
                var b = UI.Button(bar, labels[i], 13, () => gm.SetSpeed(k), Theme.A(Theme.PanelHi, 0), Theme.PanelHi, Theme.PanelHi2, Theme.Sub);
                b.rectTransform().AtRight(x, 7, 44, TaskbarH - 14);
                speedBtns.Add(b);
                x += 46;
            }
            speedBtns.Reverse();
            UI.Txt(bar, "배속", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight).rectTransform.AtRight(x + 2, 0, 40, TaskbarH);
        }

        void BuildBanner()
        {
            var b = UI.Img("DayBanner", root, Theme.Line);
            var rt = b.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(560, 132);
            rt.anchoredPosition = new Vector2(0, 120);
            var inner = UI.Img("Inner", rt, Theme.A(Theme.Window, 0.97f)).rectTransform.Fill(1, 1, 1, 1);
            UI.Img("Bar", inner, Theme.Accent).rectTransform.Top(0, 2);
            bannerDay = UI.Txt(inner, "", 40, Theme.Text, true, TextAlignmentOptions.Center);
            bannerDay.rectTransform.Top(16, 48);
            bannerDate = UI.Txt(inner, "", 16, Theme.Sub, true, TextAlignmentOptions.Center);
            bannerDate.rectTransform.Top(66, 24);
            bannerSub = UI.Txt(inner, "", 12, Theme.Dim, false, TextAlignmentOptions.Center);
            bannerSub.rectTransform.Top(94, 20);
            bannerCg = b.gameObject.AddComponent<CanvasGroup>();
            bannerCg.alpha = 0;
            bannerCg.blocksRaycasts = false;
            bannerCg.interactable = false;
        }

        // ───────────────────────── 날짜 배너 ─────────────────────────

        public void ShowDayBanner()
        {
            var e = gm.engine;
            var d = e.Now;
            bannerDay.text = "DAY " + e.Day;
            bannerDate.text = d.Year + "년 " + d.Month + "월 " + d.Day + "일 " + MarketEngine.DayName(d.DayOfWeek) + "요일";
            bool wk = MarketEngine.Weekday(e.DayIndex);
            bannerSub.text = wk
                ? "국내 정규장 09:00 – 15:30   ·   해외 정규장 22:30 – 05:00   ·   오늘의 뉴스 10건"
                : Fmt.Colored("주말 — 주식 시장 휴장", Theme.Accent) + "   ·   코인은 24시간 거래   ·   오늘의 뉴스 10건";
            bannerT = 0;
        }

        // ───────────────────────── 갱신 ─────────────────────────

        public void Rebind()
        {
            watchlist.Rebind();
            tickerX = 0;
            UpdateTickerText();
            RefreshAll();
        }

        public void RefreshAll()
        {
            RefreshSummary();
            RefreshTaskbar();
            watchlist.Refresh();
            chart.Refresh();
            order.Refresh();
            portfolioView.Refresh();
            news.Refresh();
        }

        public void OnSelectionChanged()
        {
            watchlist.ShowKind(gm.selected.kind);
            watchlist.Refresh();
            chart.Refresh();
            order.Refresh();
            portfolioView.Refresh();
        }

        public void OnNews(NewsItem n)
        {
            UpdateTickerText();
            news.Refresh();
        }

        void RefreshSummary()
        {
            var pf = gm.portfolio;
            var e = gm.engine;
            double eq = pf.Equity;
            equityT.text = Fmt.Won(eq) + Fmt.Colored(" 원", Theme.Dim);
            returnT.text = Fmt.Pct(pf.ReturnPct);
            returnT.color = Theme.Change(pf.ReturnPct);
            returnT.rectTransform.anchoredPosition = new Vector2(24 + equityT.preferredWidth + 14, -31);
            cashT.text = Fmt.Won(pf.cash);
            SetSigned(spotT, pf.SpotPnL);
            SetSigned(posT, pf.PositionPnL);
            SetSigned(realT, pf.realized);

            string next;
            double prog;
            rankT.text = GameManager.Rank(eq, out next, out prog);
            rankFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)prog), 1);
            rankNextT.text = next != null ? "다음  " + next : "최고 등급";

            // 장 시계
            MarketRow(0, AssetKind.Domestic, MarketEngine.DomOpen, MarketEngine.DomClose - MarketEngine.DomOpen);
            MarketRow(1, AssetKind.Overseas, MarketEngine.OvsOpen, 1440 - MarketEngine.OvsOpen + MarketEngine.OvsClose);
            int mod = e.MinuteOfDay;
            mktStatus[2].text = Fmt.Colored(((int)(mod / 14.4f)) + "% 경과", Theme.Text) + Fmt.Colored("   자정까지 " + Fmt.Duration(1440 - mod), Theme.Dim);
            mktFill[2].color = Theme.Accent;
            mktFill[2].rectTransform.anchorMax = new Vector2(mod / 1440f, 1);

            shownMinute = -1;   // 시계는 Update에서 매 프레임 부드럽게 갱신
        }

        void MarketRow(int i, AssetKind k, int openMod, int length)
        {
            var e = gm.engine;
            bool open = MarketEngine.OpenAt(k, e.minute);
            if (open)
            {
                int left = e.MinutesUntilClose(k);
                mktStatus[i].text = Fmt.Colored("● 장중", Theme.Live) + Fmt.Colored("   마감까지 " + Fmt.Duration(left), Theme.Sub);
                mktFill[i].color = Theme.Live;
                mktFill[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01(1f - left / (float)length), 1);
            }
            else
            {
                int until = e.MinutesUntilOpen(k);
                bool weekend = until > 1440;
                mktStatus[i].text = Fmt.Colored("○ " + (weekend ? "주말 휴장" : "휴장"), Theme.Dim) +
                                    Fmt.Colored("   개장까지 " + Fmt.Duration(Mathf.Max(0, until)), Theme.Dim);
                mktFill[i].color = Theme.Dim;
                mktFill[i].rectTransform.anchorMax = new Vector2(0, 1);
            }
        }

        static void SetSigned(TextMeshProUGUI t, double v)
        {
            t.text = Fmt.SignedWon(v);
            t.color = Theme.Change(v);
        }

        public void RefreshTaskbar()
        {
            var e = gm.engine;
            shownMinute = -1;
            for (int i = 0; i < speedBtns.Count; i++)
            {
                bool on = i == gm.speedIndex;
                speedBtns[i].normal = on ? Theme.A(Theme.Accent, 0.14f) : Theme.A(Theme.PanelHi, 0);
                speedBtns[i].textColor = on ? Theme.Accent : Theme.Sub;
                speedBtns[i].Refresh();
            }
            bool muted = Settings.Volume <= 0.001f;
            soundBtn.textColor = muted ? Theme.Dim : Theme.Sub;
            soundBtn.SetLabel(muted ? "♪×" : "♪");
            soundBtn.Refresh();
            string regime = "";
            if (e.regime != 0)
            {
                Color rc = e.regime < 0 ? Theme.Down : Theme.Up;
                regime = Fmt.Colored((e.regime < 0 ? "▼ " : "▲ ") + e.RegimeName + " 진행 중 · 종료까지 " + Fmt.Duration(Mathf.Max(0, e.regimeEnd - e.minute)), rc);
            }
            string state = "";
            if (gm.state == GameState.Playing && gm.Speed == 0) state = "■  일시정지  ·  Space 로 재개";
            else if (gm.state == GameState.Playing && !e.AnyStockOpen) state = Fmt.Colored("주식 휴장 · 시간 빠르게 진행", Theme.Sub);
            statusT.text = regime.Length > 0 && state.Length > 0 ? regime + Fmt.Colored("   |   ", Theme.Dim) + state : regime + state;
            regimeStrip.enabled = e.regime != 0;
            if (e.regime != 0) regimeStrip.color = e.regime < 0 ? Theme.Down : Theme.Up;
        }

        void ToggleSound()
        {
            Settings.Volume = Settings.Volume > 0.001f ? 0f : 0.7f;
            Settings.Save();
            RefreshTaskbar();
        }

        void UpdateTickerText()
        {
            if (tickerT == null) return;
            var list = gm.engine.news;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = list.Count - 1; i >= 0 && n < 6; i--, n++)
            {
                var item = list[i];
                Color c;
                string tone = NewsView.ToneLabel(item.tone, out c);
                sb.Append(Fmt.Colored(Fmt.Clock(item.minute), Theme.Dim)).Append("  ")
                  .Append(Fmt.Colored("[" + tone + "]", c)).Append(" ")
                  .Append(Fmt.Colored(item.headline, Theme.Text)).Append("          ");
            }
            if (n == 0) sb.Append("BUY OR DIE 마켓 속보 채널 — 하루 10건의 뉴스가 여기에 실시간으로 흘러갑니다.          ");
            tickerT.text = sb.ToString();
        }

        // ───────────────────────── 알림 ─────────────────────────

        public void Toast(string title, string body, Color c)
        {
            var frame = UI.Img("Toast", toastRoot, Theme.Line);
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
            rt.sizeDelta = new Vector2(380, 74);
            var cg = frame.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            UI.Img("Bg", rt, Theme.PanelHi).rectTransform.Fill(1, 1, 1, 1);
            UI.Img("Bar", rt, c).rectTransform.Left(1, 3, 1, 1);
            UI.Txt(rt, title, 14, Theme.Text, true).rectTransform.At(18, 10, 270, 20);
            UI.Txt(rt, Fmt.Clock(gm.engine.minute), 11, Theme.Dim, false, TextAlignmentOptions.MidlineRight).rectTransform.AtRight(14, 10, 60, 20);
            var bt = UI.Txt(rt, body, 12, Theme.Sub, false, TextAlignmentOptions.TopLeft);
            bt.textWrappingMode = TextWrappingModes.Normal;
            bt.maxVisibleLines = 2;
            bt.rectTransform.At(18, 34, 346, 36);

            toasts.Insert(0, new ToastItem { rt = rt, cg = cg, born = Time.unscaledTime, y = -40 });
            while (toasts.Count > 4)
            {
                var old = toasts[toasts.Count - 1];
                toasts.RemoveAt(toasts.Count - 1);
                Destroy(old.rt.gameObject);
            }
        }

        public void Shake() { shakeT = 0.35f; }

        void UpdateClock()
        {
            if (gm == null || gm.engine == null) return;
            if (tickFill != null) tickFill.rectTransform.anchorMax = new Vector2(gm.Speed == 0 ? 0 : gm.TickProgress, 1);
            int m = gm.state == GameState.Playing ? gm.DisplayMinute : gm.engine.minute;
            if (m == shownMinute) return;
            shownMinute = m;
            var e = gm.engine;
            int day = m / 1440 - MarketEngine.FirstDayIndex + 1;
            clockT.text = Fmt.Clock(m);
            dayT.text = Fmt.Date(m) + "  ·  DAY " + day;
            dateT.text = Fmt.Colored(Fmt.FullDate(m), Theme.Sub) + "     " + Fmt.Clock(m) + "     " + Fmt.Colored("DAY " + day, Theme.Accent);
        }

        void Update()
        {
            UpdateClock();
            float dt = Time.unscaledDeltaTime;

            if (tickerT != null)
            {
                float w = tickerT.preferredWidth;
                tickerX -= 70f * dt;
                if (tickerX < -w) tickerX = tickerMask.rect.width;
                tickerT.rectTransform.anchoredPosition = new Vector2(tickerX + 14, 0);
            }

            float yAcc = 0;
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                float age = Time.unscaledTime - t.born;
                if (age > toastLife)
                {
                    Destroy(t.rt.gameObject);
                    toasts.RemoveAt(i);
                    i--;
                    continue;
                }
                float inT = Mathf.Clamp01(age / 0.18f);
                float outT = Mathf.Clamp01((toastLife - age) / 0.4f);
                t.y = Mathf.Lerp(t.y, yAcc, 1 - Mathf.Exp(-dt * 14));
                float ease = 1 - Mathf.Pow(1 - inT, 3);
                t.rt.anchoredPosition = new Vector2((1 - ease) * 40, t.y);
                t.cg.alpha = Mathf.Min(ease, outT);
                yAcc += t.rt.sizeDelta.y + 8;
            }

            if (bannerT >= 0)
            {
                bannerT += dt;
                const float life = 2.8f;
                float a = bannerT < 0.3f ? bannerT / 0.3f : (bannerT > life - 0.5f ? Mathf.Clamp01((life - bannerT) / 0.5f) : 1);
                bannerCg.alpha = a;
                var rt = (RectTransform)bannerCg.transform;
                rt.anchoredPosition = new Vector2(0, 120 + (1 - Mathf.Min(1, bannerT / 0.3f)) * 16);
                if (bannerT > life) { bannerT = -1; bannerCg.alpha = 0; }
            }

            if (alertT >= 0)
            {
                alertT += dt;
                const float alife = 4.2f;
                float a = alertT < 0.25f ? alertT / 0.25f : (alertT > alife - 0.6f ? Mathf.Clamp01((alife - alertT) / 0.6f) : 1);
                alertCg.alpha = a;
                float s = 1f + 0.06f * (1 - Mathf.Min(1, alertT / 0.25f));
                alertCg.transform.localScale = new Vector3(s, s, 1);
                alertBar.color = Theme.A(alertBar.color, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(alertT * 6f)));
                if (alertT > alife) { alertT = -1; alertCg.alpha = 0; }
            }
            if (regimeStrip != null && regimeStrip.enabled)
                regimeStrip.color = Theme.A(regimeStrip.color, 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.6f)));

            if (shakeT > 0)
            {
                shakeT -= dt;
                float s = shakeT > 0 ? 6f * shakeT / 0.35f : 0;
                window.anchoredPosition = windowBase + new Vector2(UnityEngine.Random.Range(-s, s), UnityEngine.Random.Range(-s, s));
            }
        }

        // ───────────────────────── 대폭락장 / 대호황장 경보 ─────────────────────────

        void BuildAlert()
        {
            var b = UI.Img("RegimeAlert", root, Theme.Line);
            var rt = b.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(820, 190);
            rt.anchoredPosition = new Vector2(0, 90);
            var inner = UI.Img("Inner", rt, Theme.A(Theme.Window, 0.97f)).rectTransform.Fill(1, 1, 1, 1);
            alertBar = UI.Img("Bar", inner, Theme.Down);
            alertBar.rectTransform.Top(0, 4);
            alertTitle = UI.Txt(inner, "", 52, Theme.Text, true, TextAlignmentOptions.Center);
            alertTitle.overflowMode = TextOverflowModes.Overflow;
            alertTitle.rectTransform.Top(24, 64);
            alertSub = UI.Txt(inner, "", 16, Theme.Text, true, TextAlignmentOptions.Center);
            alertSub.rectTransform.Top(98, 24);
            alertTip = UI.Txt(inner, "", 13, Theme.Sub, false, TextAlignmentOptions.Center);
            alertTip.rectTransform.Top(132, 22);
            alertCg = b.gameObject.AddComponent<CanvasGroup>();
            alertCg.alpha = 0;
            alertCg.blocksRaycasts = false;
            alertCg.interactable = false;
        }

        public void ShowRegimeAlert(int regime)
        {
            if (regime == 0) return;
            bool crash = regime < 0;
            Color c = crash ? Theme.Down : Theme.Up;
            alertBar.color = c;
            alertTitle.color = c;
            alertTitle.text = crash ? "▼  대폭락장 발생" : "▲  대호황장 발생";
            alertSub.text = crash
                ? "모든 시장이 한꺼번에 무너집니다  ·  변동성 1.8배  ·  1~2일 지속"
                : "전 종목에 매수세가 몰립니다  ·  변동성 1.45배  ·  1~2일 지속";
            alertTip.text = crash
                ? "숏 포지션의 기회일 수도, 파산의 시작일 수도 있어요."
                : "올라탈 것인가, 꼭지에서 내릴 것인가.";
            alertT = 0;
            alertCg.transform.SetAsLastSibling();
            toastRoot.SetAsLastSibling();
            Shake();
            RefreshTaskbar();
        }

        public void OpenNews(NewsItem focus)
        {
            if (modalCount > 0 || pauseMenu != null || overlay != null || (tutorial != null && tutorial.Active)) return;
            modalCount++;
            NewsWindow.Open(root, gm, focus, () =>
            {
                modalCount--;
                lastModalCloseFrame = Time.frameCount;
                RefreshAll();
            });
        }

        public void OpenHistory()
        {
            if (modalCount > 0 || pauseMenu != null || overlay != null || (tutorial != null && tutorial.Active)) return;
            modalCount++;
            TradeHistoryWindow.Open(root, gm, () =>
            {
                modalCount--;
                lastModalCloseFrame = Time.frameCount;
                RefreshAll();
            });
        }

        // ───────────────────────── 메뉴 / 설정 ─────────────────────────

        public void OpenSettings()
        {
            if (modalCount > 0) return;
            modalCount++;
            SettingsPanel.Open(root, true, () =>
            {
                modalCount--;
                lastModalCloseFrame = Time.frameCount;
                RefreshAll();
            });
        }

        public void OpenPauseMenu()
        {
            if (pauseMenu != null || overlay != null || modalCount > 0 || Time.frameCount == lastModalCloseFrame || (tutorial != null && tutorial.Active)) return;
            RectTransform ov;
            var c = Modal.Open(root, 440, 420, out ov);
            pauseMenu = ov;
            UI.Txt(c, "일시정지", 22, Theme.Text, true).rectTransform.At(40, 32, 300, 30);
            UI.Txt(c, Fmt.FullDate(gm.engine.minute) + "  " + Fmt.Clock(gm.engine.minute) + "  ·  DAY " + gm.engine.Day, 13, Theme.Dim)
                .rectTransform.At(40, 66, 360, 20);
            UI.HLine(c, 100, 40, 40);
            float y = 120;
            MenuButton(c, "계속하기", y, ClosePauseMenu, true); y += 62;
            MenuButton(c, "저장하기", y, () => gm.SaveGame(true), false); y += 62;
            MenuButton(c, "설정", y, () => { ClosePauseMenu(); OpenSettings(); }, false); y += 62;
            MenuButton(c, "저장하고 타이틀로", y, () =>
            {
                ClosePauseMenu();
                gm.ExitToTitle();
            }, false);
        }

        void MenuButton(RectTransform c, string label, float y, Action a, bool primary)
        {
            var b = primary
                ? UI.Button(c, label, 16, a, Theme.Text, Color.white, Theme.Sub, Theme.Desktop)
                : UI.SolidButton(c, label, 16, a);
            b.rectTransform().At(40, y, 360, 50);
        }

        public void ClosePauseMenu()
        {
            if (pauseMenu != null) Destroy(pauseMenu.gameObject);
            pauseMenu = null;
        }

        public void TogglePauseMenu()
        {
            if (pauseMenu != null) ClosePauseMenu();
            else OpenPauseMenu();
        }

        // ───────────────────────── 승리 / 파산 ─────────────────────────

        RectTransform OpenOverlay(float w, float h)
        {
            HideOverlays();
            var c = Modal.Open(root, w, h, out overlay, 0.88f);
            return c;
        }

        public void HideOverlays()
        {
            if (overlay != null) Destroy(overlay.gameObject);
            overlay = null;
        }

        static void BigWord(RectTransform c, string word, Color color)
        {
            var t = UI.Txt(c, word, 120, color, true, TextAlignmentOptions.Center);
            t.overflowMode = TextOverflowModes.Overflow;
            t.characterSpacing = 4;
            t.rectTransform.Top(36, 150);
        }

        public void ShowGameOver()
        {
            var c = OpenOverlay(760, 500);
            BigWord(c, "DIE", Theme.Down);
            UI.Txt(c, "파산했습니다. 계좌 잔고가 1만 원 아래로 떨어졌어요.", 18, Theme.Text, true, TextAlignmentOptions.Center).rectTransform.Top(200, 28);
            UI.Txt(c, "저장 기록은 삭제됐어요. 시장은 내일도 열립니다.", 14, Theme.Sub, false, TextAlignmentOptions.Center).rectTransform.Top(234, 22);
            StatsBlock(c, 290);
            var b1 = UI.Button(c, "새 게임", 16, gm.Restart, Theme.Text, Color.white, Theme.Sub, Theme.Desktop);
            b1.rectTransform().Center(200, 52, -110, -180);
            var b2 = UI.SolidButton(c, "타이틀로", 16, gm.ExitToTitle);
            b2.rectTransform().Center(200, 52, 110, -180);
        }

        public void ShowWin()
        {
            var c = OpenOverlay(760, 520);
            BigWord(c, "BUY", Theme.Up);
            UI.Txt(c, "총자산 10억 달성 — 당신은 이제 '월가의 늑대'입니다", 18, Theme.Text, true, TextAlignmentOptions.Center).rectTransform.Top(200, 28);
            UI.Txt(c, "계속 거래해서 기록을 더 늘려도 되고, 새 게임을 시작해도 돼요.", 14, Theme.Sub, false, TextAlignmentOptions.Center).rectTransform.Top(234, 22);
            StatsBlock(c, 290);
            var b1 = UI.Button(c, "계속 거래하기", 16, gm.ContinueAfterGoal, Theme.Text, Color.white, Theme.Sub, Theme.Desktop);
            b1.rectTransform().Center(220, 52, -120, -190);
            var b2 = UI.SolidButton(c, "새 게임", 16, gm.Restart);
            b2.rectTransform().Center(200, 52, 120, -190);
        }

        void StatsBlock(RectTransform c, float y)
        {
            var pf = gm.portfolio;
            string[] labels = { "생존", "최고 자산", "거래 횟수", "강제 청산" };
            string[] values = { "DAY " + gm.engine.Day, Fmt.Short(pf.peakEquity) + "원", pf.tradeCount + "회", pf.liquidations + "회" };
            float w = 640f / labels.Length;
            UI.HLine(c, y, 60, 60);
            for (int i = 0; i < labels.Length; i++)
            {
                UI.Txt(c, labels[i], 12, Theme.Dim, false, TextAlignmentOptions.Center).rectTransform.At(60 + w * i, y + 18, w, 18);
                UI.Txt(c, values[i], 20, Theme.Text, true, TextAlignmentOptions.Center).rectTransform.At(60 + w * i, y + 40, w, 28);
            }
            UI.HLine(c, y + 84, 60, 60);
        }
    }
}

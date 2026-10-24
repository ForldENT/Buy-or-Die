using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 타이틀(시작) 화면.
    /// 앱을 처음 켜면: 회사 로고(FORLD) → 게임 로고 인트로 → 타이틀 메뉴.
    /// 타이틀은 컴퓨터 잠금화면 콘셉트: 왼쪽 게임 로고와 메뉴, 오른쪽 위 실제 시계,
    /// 뒤로는 실시간 캔들 차트, 아래로는 시세 티커.
    /// </summary>
    public class TitleUI : MonoBehaviour
    {
        public const string CompanyLogo = "Forld Logo New 2";
        public const string GameLogo = "Game Logo";
        static bool introShown;

        class MenuItem
        {
            public UIButton btn;
            public Image bar;
            public TextMeshProUGUI num, title, en, sub;
            public bool enabled;
            public Action action;
        }

        RectTransform root;
        readonly List<MenuItem> items = new List<MenuItem>();
        int focus = -1;
        TextMeshProUGUI clockT, dateT, tickerT;
        RectTransform tickerMask;
        float tickerX;
        bool modalOpen;
        bool introRunning;

        // 배경 차트
        CandleChartGraphic bgChart;
        RectTransform bgRT;
        readonly List<Candle> bgCandles = new List<Candle>();
        double bgPrice = 142350000, bgDrift;
        float bgTimer;
        readonly System.Random rng = new System.Random();
        int bgTime;

        void Awake()
        {
            Application.targetFrameRate = 60;
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Theme.Desktop; }
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var s = Sfx.I;
            root = (RectTransform)Desktop.MakeCanvas("Title Canvas").transform;
            Desktop.Build(root);
            BuildBackgroundChart();
            BuildLeft();
            BuildClock();
            BuildTicker();

            if (!introShown)
            {
                introShown = true;
                StartCoroutine(Intro());
            }
            else
            {
                MusicPlayer.Play();
                if (!GameLaunch.fromTitle) SceneFader.RevealOnStart();
            }
        }

        // ───────────────────────── 인트로: 회사 로고 → 게임 로고 ─────────────────────────

        IEnumerator Intro()
        {
            introRunning = true;
            var canvas = Desktop.MakeCanvas("Intro Canvas");
            canvas.sortingOrder = 500;
            var ir = (RectTransform)canvas.transform;
            var cg = canvas.gameObject.AddComponent<CanvasGroup>();
            UI.Img("Black", ir, Theme.Desktop, true).rectTransform.Fill();
            // 첫 프레임들은 로딩 직후라 멈칫하기 쉽다 → 몇 프레임 쉬고 시작
            for (int i = 0; i < 6; i++) yield return null;

            // 1) 회사 로고
            var company = MakeLogo(ir, CompanyLogo, 980);
            var companyCg = company.gameObject.AddComponent<CanvasGroup>();
            companyCg.alpha = 0;
            yield return Phase(companyCg, company.rectTransform, 0.7f, 1.4f, 0.6f, 0.97f, false);
            Destroy(company.gameObject);

            // 2) 게임 로고 (여기서 음악 시작)
            MusicPlayer.Play();
            var game = MakeLogo(ir, GameLogo, 640);
            var gameCg = game.gameObject.AddComponent<CanvasGroup>();
            gameCg.alpha = 0;
            yield return Phase(gameCg, game.rectTransform, 0.6f, 1.6f, 0f, 0.86f, true);

            // 3) 타이틀로 걷어내기
            float t = 0;
            while (t < 0.7f)
            {
                t += Dt;
                float k = Mathf.Clamp01(t / 0.7f);
                cg.alpha = 1 - k * k * (3 - 2 * k);
                game.rectTransform.localScale = Vector3.one * (1f + 0.06f * k);
                yield return null;
            }
            Destroy(canvas.gameObject);
            introRunning = false;
        }

        Image MakeLogo(RectTransform parent, string res, float size)
        {
            var img = UI.Img("Logo_" + res, parent, Color.white);
            img.sprite = Resources.Load<Sprite>(res);
            img.preserveAspect = true;
            img.rectTransform.Center(size, size);
            return img;
        }

        /// <summary>페이드 인 → 유지 → 페이드 아웃. 클릭이나 아무 키로 건너뛸 수 있다.</summary>
        IEnumerator Phase(CanvasGroup g, RectTransform rt, float fadeIn, float hold, float fadeOut, float fromScale, bool bounce)
        {
            float total = fadeIn + hold + fadeOut;
            float t = 0;
            yield return null;
            while (t < total)
            {
                if (SkipPressed())
                {
                    if (t < fadeIn + hold) t = fadeIn + hold;   // 바로 페이드 아웃으로
                    else break;
                }
                t += Dt;
                float a;
                if (t < fadeIn) a = t / fadeIn;
                else if (t < fadeIn + hold) a = 1;
                else a = fadeOut > 0 ? 1 - (t - fadeIn - hold) / fadeOut : 1;
                g.alpha = Mathf.Clamp01(a);

                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, fadeIn + hold * 0.5f));
                float e = bounce ? EaseOutBack(Mathf.Clamp01(t / (fadeIn * 1.3f))) : 1 - Mathf.Pow(1 - k, 3);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(fromScale, 1f, e);
                yield return null;
            }
            g.alpha = fadeOut > 0 ? 0 : 1;
        }

        /// <summary>프레임이 멈칫해도 로고가 건너뛰어지지 않게 한 프레임 시간을 1/30초로 제한.</summary>
        static float Dt { get { return Mathf.Min(Time.unscaledDeltaTime, 1f / 30f); } }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1;
            return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
        }

        static bool SkipPressed()
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame) || (ms != null && ms.leftButton.wasPressedThisFrame);
        }

        // ───────────────────────── 배경 차트 ─────────────────────────

        void BuildBackgroundChart()
        {
            bgRT = UI.Rect("BgChart", root);
            bgRT.anchorMin = new Vector2(0.38f, 0); bgRT.anchorMax = new Vector2(1, 1);
            bgRT.offsetMin = new Vector2(0, 110); bgRT.offsetMax = new Vector2(-60, -190);
            bgRT.pivot = Vector2.zero;
            bgRT.gameObject.AddComponent<CanvasRenderer>();
            bgChart = bgRT.gameObject.AddComponent<CandleChartGraphic>();
            bgChart.raycastTarget = false;
            bgChart.alpha = 0.32f;
            bgChart.slot = 14;
            for (int i = 0; i < 140; i++) AddBgCandle();
        }

        void AddBgCandle()
        {
            if (rng.NextDouble() < 0.04) bgDrift = (rng.NextDouble() - 0.5) * 0.004;
            double o = bgPrice, h = o, l = o, p = o;
            for (int s = 0; s < 6; s++)
            {
                p *= Math.Exp(bgDrift / 6 + Gauss() * 0.0018);
                if (p > h) h = p;
                if (p < l) l = p;
            }
            bgPrice = p;
            bgCandles.Add(new Candle { time = bgTime++, open = o, high = h, low = l, close = p, volume = 0.3 + rng.NextDouble() + Math.Abs(p / o - 1) * 300 });
            if (bgCandles.Count > 400) bgCandles.RemoveAt(0);
        }

        void RedrawBg()
        {
            Rect r = bgRT.rect;
            if (r.width < 10) return;
            bgChart.plot = new Rect(0, 0, r.width, r.height);
            int n = Mathf.Min(bgCandles.Count, Mathf.FloorToInt(r.width / bgChart.slot));
            bgChart.data.Clear();
            for (int i = bgCandles.Count - n; i < bgCandles.Count; i++) bgChart.data.Add(bgCandles[i]);
            double mn = double.MaxValue, mx = double.MinValue, vm = 0;
            foreach (var c in bgChart.data) { mn = Math.Min(mn, c.low); mx = Math.Max(mx, c.high); vm = Math.Max(vm, c.volume); }
            double pad = (mx - mn) * 0.1;
            bgChart.min = mn - pad; bgChart.max = mx + pad; bgChart.volMax = vm;
            bgChart.gridPrices.Clear();
            for (int i = 1; i < 5; i++) bgChart.gridPrices.Add(bgChart.min + (bgChart.max - bgChart.min) * i / 5.0);
            bgChart.Redraw();
        }

        double Gauss()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ───────────────────────── 로고 + 메뉴 ─────────────────────────

        void BuildLeft()
        {
            var left = UI.Rect("Left", root);
            left.anchorMin = new Vector2(0, 0.5f); left.anchorMax = new Vector2(0, 0.5f);
            left.pivot = new Vector2(0, 0.5f);
            left.anchoredPosition = new Vector2(120, 6);
            left.sizeDelta = new Vector2(720, 820);

            UI.Txt(left, "BuyOrDie OS " + Application.version + "   ·   ForldENT", 13, Theme.Dim).rectTransform.At(4, 0, 500, 20);

            var logo = UI.Img("GameLogo", left, Color.white);
            logo.sprite = Resources.Load<Sprite>(GameLogo);
            logo.preserveAspect = true;
            logo.rectTransform.At(-14, 22, 330, 330);

            UI.Txt(left, "사거나, 죽거나.", 24, Theme.Text, true).rectTransform.At(4, 352, 600, 34);
            UI.Txt(left, "국내 50 · 해외 50 · 코인 100 종목   ·   하루 10개의 뉴스   ·   그리고 상장폐지", 15, Theme.Sub)
                .rectTransform.At(4, 390, 700, 24);

            var data = SaveSystem.Load();
            string loadSub = data != null
                ? "DAY " + data.day + "  ·  총자산 " + Fmt.Short(data.equity) + "원  ·  " + data.savedAt + " 저장"
                : "저장된 게임이 없어요";

            float y = 440;
            AddItem(left, y, "01", "새 게임", "NEW GAME", "튜토리얼과 함께 1,000만 원으로 시작", true, OnNewGame); y += 72;
            AddItem(left, y, "02", "이어하기", "LOAD GAME", loadSub, data != null, OnLoadGame); y += 72;
            AddItem(left, y, "03", "설정", "SETTINGS", "효과음 · 배경음악 · 상승/하락 색 · 배속 · 알림 · 화면", true, OnSettings); y += 72;
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                AddItem(left, y, "04", "종료", "QUIT", "다음에 또 만나요", true, () => Application.Quit());
                y += 72;
            }

            double best = GameManager.BestEquity;
            string rec = best > 0
                ? "최고 기록   " + Fmt.Colored(Fmt.Won(best) + "원", Theme.Text) + "   ·   최장 생존 DAY " + GameManager.BestDay
                : "상승은 " + Fmt.Colored("빨강", Theme.Up) + ", 하락은 " + Fmt.Colored("파랑", Theme.Down) + ".  설정에서 해외식 색으로 바꿀 수 있어요.";
            UI.HLine(left, y + 12, 4, 300, Theme.LineSoft);
            UI.Txt(left, rec, 13, Theme.Sub).rectTransform.At(4, y + 26, 640, 22);

            SetFocus(data != null ? 1 : 0);
        }

        void AddItem(RectTransform parent, float y, string num, string title, string en, string sub, bool enabled, Action action)
        {
            var m = new MenuItem { enabled = enabled, action = action };
            int index = items.Count;
            m.btn = UI.Button(parent, "", 1, () => { if (m.enabled && !introRunning) m.action(); },
                Theme.A(Theme.PanelHi, 0), Theme.A(Theme.PanelHi, 0.55f), Theme.A(Theme.PanelHi2, 0.8f), Theme.Text);
            Destroy(m.btn.label.gameObject);
            m.btn.label = null;
            m.btn.interactable = enabled;
            m.btn.rectTransform().At(0, y, 520, 64);
            m.btn.onHover = on => { if (on && m.enabled) SetFocus(index); };
            var t = m.btn.transform;
            m.bar = UI.Img("Bar", t, Theme.Accent);
            m.bar.rectTransform.Left(0, 3, 10, 10);
            m.num = UI.Txt(t, num, 13, Theme.Dim, true);
            m.num.rectTransform.At(22, 11, 40, 22);
            m.title = UI.Txt(t, title, 22, enabled ? Theme.Text : Theme.Dim, true);
            m.title.rectTransform.At(62, 6, 200, 30);
            m.en = UI.Txt(t, en, 11, Theme.Dim, true);
            m.sub = UI.Txt(t, sub, 12, enabled ? Theme.Sub : Theme.Dim);
            m.sub.rectTransform.At(62, 38, 440, 18);
            m.en.rectTransform.At(62 + m.title.preferredWidth + 12, 14, 120, 18);
            items.Add(m);
        }

        void SetFocus(int i)
        {
            focus = i;
            for (int k = 0; k < items.Count; k++)
            {
                var m = items[k];
                bool on = k == focus;
                m.bar.enabled = on;
                m.num.color = on ? Theme.Accent : Theme.Dim;
                m.btn.normal = on ? Theme.A(Theme.PanelHi, 0.55f) : Theme.A(Theme.PanelHi, 0);
                m.btn.Refresh();
            }
        }

        void Move(int dir)
        {
            if (items.Count == 0) return;
            int i = focus;
            for (int n = 0; n < items.Count; n++)
            {
                i = (i + dir + items.Count) % items.Count;
                if (items[i].enabled) { SetFocus(i); Sfx.I.Click(); return; }
            }
        }

        // ───────────────────────── 동작 ─────────────────────────

        void OnNewGame()
        {
            if (SaveSystem.HasSave)
            {
                modalOpen = true;
                Modal.Confirm(root, "새 게임을 시작할까요?", "저장된 진행 기록이 사라지고 1,000만 원으로 처음부터 시작해요. 튜토리얼이 함께 진행됩니다.", "새로 시작", () =>
                {
                    modalOpen = false;
                    StartNew();
                });
                StartCoroutine(WatchModal());
                return;
            }
            StartNew();
        }

        IEnumerator WatchModal()
        {
            yield return null;
            while (root.Find("Modal") != null) yield return null;
            modalOpen = false;
        }

        void StartNew()
        {
            SaveSystem.Delete();
            GameLaunch.mode = LaunchMode.New;
            GameLaunch.fromTitle = true;
            Sfx.I.Buy();
            SceneFader.Load(SceneFader.GameScene, "새 시장을 여는 중  ·  종목 200개 상장");
        }

        void OnLoadGame()
        {
            if (!SaveSystem.HasSave) return;
            GameLaunch.mode = LaunchMode.Load;
            GameLaunch.fromTitle = true;
            Sfx.I.Buy();
            SceneFader.Load(SceneFader.GameScene, "저장된 시장을 불러오는 중");
        }

        void OnSettings()
        {
            modalOpen = true;
            SettingsPanel.Open(root, false, () =>
            {
                modalOpen = false;
                GameLaunch.fromTitle = true;
                SceneFader.Load(SceneFader.TitleScene, "BuyOrDie OS");
            });
        }

        // ───────────────────────── 시계 / 티커 ─────────────────────────

        void BuildClock()
        {
            var c = UI.Rect("Clock", root).AtRight(80, 70, 500, 120);
            clockT = UI.Txt(c, "", 64, Theme.Text, true, TextAlignmentOptions.TopRight);
            clockT.overflowMode = TextOverflowModes.Overflow;
            clockT.rectTransform.At(0, 0, 500, 72);
            dateT = UI.Txt(c, "", 16, Theme.Sub, false, TextAlignmentOptions.TopRight);
            dateT.rectTransform.At(0, 82, 500, 24);
        }

        void BuildTicker()
        {
            var bar = UI.Img("Ticker", root, Theme.Hex("0B0F15")).rectTransform.Bottom(0, 46);
            UI.Img("Line", bar, Theme.Line).rectTransform.Top(0, 1);
            var label = UI.Img("Label", bar, Theme.A(Theme.Accent, 0.12f)).rectTransform.Left(0, 110);
            UI.Txt(label, "MARKET", 12, Theme.Accent, true, TextAlignmentOptions.Center).rectTransform.Fill();
            tickerMask = UI.Rect("Mask", bar).Fill(110, 260, 0, 0);
            tickerMask.gameObject.AddComponent<RectMask2D>();
            tickerT = UI.Txt(tickerMask, "", 13, Theme.Sub);
            tickerT.overflowMode = TextOverflowModes.Overflow;
            var rt = tickerT.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(8000, 0);
            UI.Txt(bar, "↑↓ 이동   Enter 선택", 12, Theme.Dim, false, TextAlignmentOptions.MidlineRight).rectTransform.AtRight(24, 0, 230, 46);

            var all = AssetCatalog.Create();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 36; i++)
            {
                var a = all[rng.Next(all.Count)];
                double ch = Gauss() * (a.IsCoin ? 4 : 2);
                Color col = Theme.Change(ch);
                sb.Append(Fmt.Colored(a.name, Theme.Text)).Append(" ")
                  .Append(Fmt.Colored(Fmt.Price(a.price * (1 + ch / 100)), Theme.Sub)).Append(" ")
                  .Append(Fmt.Colored((ch > 0 ? "▲" : "▼") + Math.Abs(ch).ToString("0.00") + "%", col)).Append("        ");
            }
            tickerT.text = sb.ToString();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var now = DateTime.Now;
            clockT.text = now.ToString("HH:mm");
            dateT.text = now.Year + "년 " + now.Month + "월 " + now.Day + "일 " + MarketEngine.DayName(now.DayOfWeek) + "요일";

            bgTimer += dt;
            if (bgTimer > 0.35f) { bgTimer = 0; AddBgCandle(); }
            RedrawBg();

            tickerX -= 60f * dt;
            if (tickerX < -tickerT.preferredWidth) tickerX = tickerMask.rect.width;
            tickerT.rectTransform.anchoredPosition = new Vector2(tickerX + 16, 0);

            var kb = Keyboard.current;
            if (kb == null || modalOpen || introRunning || SceneFader.Busy) return;
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Move(-1);
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Move(1);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (focus >= 0 && items[focus].enabled) { Sfx.I.Click(); items[focus].action(); }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 새 게임 튜토리얼. 화면의 한 영역만 밝게 남기고(스포트라이트) 나머지를 어둡게 덮은 뒤,
    /// 옆에 설명 카드를 띄운다. 일부 단계는 실제로 해봐야 다음으로 넘어간다.
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        class Step
        {
            public Func<RectTransform> target;
            public string title, body, actionHint;
            public Func<bool> waitFor;
            public Func<string> hintFn;   // 상황에 따라 바뀌는 안내 (null 반환 시 기본 안내)
            public Action onEnter;
        }

        const float CardW = 430;

        readonly List<Step> steps = new List<Step>();
        int index = -1;
        RectTransform root, layer, card;
        readonly Image[] dims = new Image[4];
        readonly Image[] edges = new Image[4];
        TextMeshProUGUI stepT, titleT, bodyT, hintT;
        UIButton next, skip;
        Action onDone;
        float cardH = 220;

        public bool Active { get { return index >= 0; } }

        public void Begin(GameUI ui, GameManager gm, Action done)
        {
            root = ui.root;
            onDone = done;
            steps.Clear();
            int tradeBase = 0;

            Add(null, "BUY OR DIE에 오신 걸 환영해요",
                "시작 자금은 1,000만 원. 총자산 10억을 만들면 승리(BUY), 1만 원 아래로 떨어지면 파산(DIE)이에요.\n튜토리얼 동안은 시간이 멈춰 있어요.");
            Add(() => ui.summaryRT, "내 계좌 요약",
                "총자산 · 현금 · 현물 평가손익 · 레버리지 손익이 실시간으로 바뀌어요. 오른쪽 끝은 투자자 등급(개미 → 월가의 늑대)이에요.");
            Add(() => ui.marketStripRT, "시간이 흐르는 게 보여요",
                "국내장은 평일 09:00~15:30, 해외장은 22:30~05:00(한국 시간). 맨 아래 줄은 오늘 하루가 얼마나 지났는지예요.\n주말엔 주식이 쉬고 코인만 24시간 움직여요.");
            MarketAsset pickStart = null;
            var pickStep = Add(() => ui.watchlistRT, "종목 고르기",
                "처음엔 국내 50 · 해외 50 · 코인 100개, 매일 새 종목이 상장돼요.\n위쪽 '코인' 탭(또는 Tab 키)을 누르면 첫 코인이 선택돼요. 목록에서 다른 코인을 골라도 돼요. 고르면 자동으로 넘어가요.",
                () => gm.selected != null && gm.selected.IsCoin, "'코인' 탭을 누르거나 코인을 하나 선택해 보세요");
            pickStep.onEnter = () => pickStart = gm.selected;
            // 주식을 고르면 반응이 없는 것처럼 보이므로, 왜 코인이어야 하는지 바로 알려준다
            pickStep.hintFn = () => gm.selected != null && !gm.selected.IsCoin && gm.selected != pickStart
                ? "주식은 지금 장이 닫혀 있어요.\n24시간 거래되는 코인을 골라주세요" : null;
            Add(() => ui.chartRT, "차트 읽기",
                "빨간 캔들은 상승, 파란 캔들은 하락이에요. 마우스를 올리면 그 시점의 가격이 보이고, 오른쪽 위에서 1분 ~ 1시간 봉을 바꿀 수 있어요.");
            var buyStep = Add(() => ui.orderRT, "첫 매수",
                "'25%'를 누른 다음 '매수하기'를 눌러보세요. 코인은 24시간 거래돼서 지금 바로 살 수 있어요.",
                () => gm.portfolio.tradeCount > tradeBase, "직접 매수해 보세요");
            buyStep.onEnter = () => { tradeBase = gm.portfolio.tradeCount; ui.order.SelectMode(0); };
            var levStep = Add(() => ui.orderRT, "레버리지: 롱과 숏",
                "롱은 오르면, 숏은 내리면 수익이에요. 배율만큼 수익도 손실도 커지고, 가격이 청산가에 닿으면 증거금을 전부 잃어요.");
            levStep.onEnter = () => ui.order.SelectMode(1);
            var pfStep = Add(() => ui.portfolioRT, "보유 자산과 포지션",
                "산 자산과 열린 포지션은 여기에 쌓여요. '전량 매도'나 '청산'으로 정리할 수 있어요. 차트에는 내 평단과 청산가가 점선으로 표시돼요.");
            pfStep.onEnter = () => ui.order.SelectMode(0);
            Add(() => ui.newsRT, "하루 10개의 뉴스",
                "AI가 매일 새 뉴스를 써요. 회사 악재는 몇 시간 안에 반드시 떨어지고, 호재는 반드시 올라요.\n뉴스를 누르면 기사 본문이 열려요. 신규 상장 · 상장폐지 공시도 여기 떠요!");
            Add(() => ui.speedRT, "배속",
                "시세는 2초마다 한 번씩 바뀌어요(시계 아래 막대). 1x · 2x · 5x 로 빨리 돌릴 수 있고, Space 로 일시정지, 1~3 키로 배속 변경.");
            Add(() => ui.menuRT, "저장 · 설정 · 메뉴",
                "매일 자정 자동 저장돼요. 거래 내역(H), 뉴스(N), 설정도 여기서 열 수 있어요. Esc 는 메뉴.\n그럼 시장을 열어볼까요?");

            BuildLayer();
            Show(0);
        }

        Step Add(Func<RectTransform> target, string title, string body, Func<bool> waitFor = null, string hint = null)
        {
            var s = new Step { target = target, title = title, body = body, waitFor = waitFor, actionHint = hint };
            steps.Add(s);
            return s;
        }

        void BuildLayer()
        {
            if (layer != null) Destroy(layer.gameObject);
            layer = UI.Rect("Tutorial", root).Fill();
            layer.SetAsLastSibling();
            for (int i = 0; i < 4; i++)
            {
                dims[i] = UI.Img("Dim", layer, Theme.A(Theme.Desktop, 0.8f), true);
                Pin(dims[i].rectTransform);
            }
            for (int i = 0; i < 4; i++)
            {
                edges[i] = UI.Img("Edge", layer, Theme.Accent);
                Pin(edges[i].rectTransform);
            }

            var frame = UI.Img("Card", layer, Theme.Accent);
            card = frame.rectTransform;
            Pin(card);
            var inner = UI.Img("Inner", card, Theme.Window).rectTransform.Fill(1, 1, 1, 1);
            stepT = UI.Txt(inner, "", 12, Theme.Accent, true);
            stepT.rectTransform.At(26, 20, 200, 18);
            titleT = UI.Txt(inner, "", 19, Theme.Text, true);
            titleT.rectTransform.At(26, 42, CardW - 52, 28);
            bodyT = UI.Txt(inner, "", 14, Theme.Sub, false, TextAlignmentOptions.TopLeft);
            bodyT.textWrappingMode = TextWrappingModes.Normal;
            bodyT.lineSpacing = 8;
            bodyT.rectTransform.At(26, 80, CardW - 52, 100);

            skip = UI.GhostButton(inner, "건너뛰기", 12, Finish, false);
            skip.rectTransform().anchorMin = skip.rectTransform().anchorMax = skip.rectTransform().pivot = Vector2.zero;
            skip.rectTransform().anchoredPosition = new Vector2(14, 14);
            skip.rectTransform().sizeDelta = new Vector2(96, 38);

            hintT = UI.Txt(inner, "", 12, Theme.Accent, true, TextAlignmentOptions.MidlineRight);
            var hr = hintT.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1, 0);
            hr.anchoredPosition = new Vector2(-22, 14);
            hr.sizeDelta = new Vector2(260, 38);

            next = UI.Button(inner, "다음", 15, Next, Theme.Text, Color.white, Theme.Sub, Theme.Desktop);
            var nr = next.rectTransform();
            nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(1, 0);
            nr.anchoredPosition = new Vector2(-18, 14);
            nr.sizeDelta = new Vector2(130, 40);
        }

        static void Pin(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = Vector2.zero;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchoredPosition = new Vector2(x0, y0);
            rt.sizeDelta = new Vector2(Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        float autoT;

        void Show(int i)
        {
            autoT = 0;
            index = i;
            var s = steps[i];
            if (s.onEnter != null) s.onEnter();
            stepT.text = (i + 1) + " / " + steps.Count;
            titleT.text = s.title;
            bodyT.text = s.body;
            float bodyH = bodyT.GetPreferredValues(s.body, CardW - 52, 0).y + 4;
            bodyT.rectTransform.sizeDelta = new Vector2(CardW - 52, bodyH);
            cardH = 80 + bodyH + 76;
            next.SetLabel(i == steps.Count - 1 ? "시작하기  ▶" : "다음");
            skip.gameObject.SetActive(i < steps.Count - 1);
            Layout();
        }

        void Next()
        {
            if (index < 0) return;
            if (index + 1 >= steps.Count) { Finish(); return; }
            Show(index + 1);
        }

        void Finish()
        {
            index = -1;
            if (layer != null) Destroy(layer.gameObject);
            layer = null;
            if (onDone != null) onDone();
        }

        void Update()
        {
            if (index < 0 || layer == null) return;
            var s = steps[index];
            bool ok = s.waitFor == null || s.waitFor();
            // 직접 해보는 단계는 조건을 채우면 잠깐 칭찬한 뒤 자동으로 다음 단계로
            if (s.waitFor != null && ok)
            {
                autoT += Time.unscaledDeltaTime;
                next.gameObject.SetActive(false);
                hintT.gameObject.SetActive(true);
                hintT.text = Fmt.Colored("좋아요! 다음 단계로 넘어갑니다", Theme.Live);
                if (autoT > 0.9f) { Next(); return; }
            }
            else
            {
                next.gameObject.SetActive(ok);
                hintT.gameObject.SetActive(!ok);
                string h = s.hintFn != null ? s.hintFn() : null;
                hintT.text = !ok ? (h ?? s.actionHint) + "  →" : "";
            }
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
            foreach (var e in edges) e.color = Theme.A(Theme.Accent, ok ? 0.9f : pulse);
            Layout();
        }

        void Layout()
        {
            if (index < 0) return;
            Rect R = root.rect;
            var s = steps[index];
            RectTransform target = s.target != null ? s.target() : null;

            if (target == null)
            {
                Place(dims[0].rectTransform, R.xMin, R.yMin, R.xMax, R.yMax);
                for (int i = 1; i < 4; i++) Place(dims[i].rectTransform, 0, 0, 0, 0);
                foreach (var e in edges) Place(e.rectTransform, 0, 0, 0, 0);
                Place(card, -CardW / 2, -cardH / 2, CardW / 2, cardH / 2);
                return;
            }

            var c = new Vector3[4];
            target.GetWorldCorners(c);
            Vector3 a = root.InverseTransformPoint(c[0]);
            Vector3 b = root.InverseTransformPoint(c[2]);
            const float pad = 6;
            Rect T = Rect.MinMaxRect(a.x - pad, a.y - pad, b.x + pad, b.y + pad);

            Place(dims[0].rectTransform, R.xMin, R.yMin, R.xMax, T.yMin);
            Place(dims[1].rectTransform, R.xMin, T.yMax, R.xMax, R.yMax);
            Place(dims[2].rectTransform, R.xMin, T.yMin, T.xMin, T.yMax);
            Place(dims[3].rectTransform, T.xMax, T.yMin, R.xMax, T.yMax);
            Place(edges[0].rectTransform, T.xMin, T.yMin - 2, T.xMax, T.yMin);
            Place(edges[1].rectTransform, T.xMin, T.yMax, T.xMax, T.yMax + 2);
            Place(edges[2].rectTransform, T.xMin - 2, T.yMin - 2, T.xMin, T.yMax + 2);
            Place(edges[3].rectTransform, T.xMax, T.yMin - 2, T.xMax + 2, T.yMax + 2);

            const float gap = 22, m = 20;
            float x, y;
            float cy = Mathf.Clamp(T.center.y - cardH / 2, R.yMin + m, R.yMax - m - cardH);
            float cx = Mathf.Clamp(T.center.x - CardW / 2, R.xMin + m, R.xMax - m - CardW);
            if (T.xMax + gap + CardW < R.xMax - m) { x = T.xMax + gap; y = cy; }
            else if (T.xMin - gap - CardW > R.xMin + m) { x = T.xMin - gap - CardW; y = cy; }
            else if (T.yMin - gap - cardH > R.yMin + m) { x = cx; y = T.yMin - gap - cardH; }
            else if (T.yMax + gap + cardH < R.yMax - m) { x = cx; y = T.yMax + gap; }
            else { x = -CardW / 2; y = -cardH / 2; }
            Place(card, x, y, x + CardW, y + cardH);
        }
    }
}

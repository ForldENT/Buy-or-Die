using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BuyOrDie
{
    /// <summary>
    /// 뉴스 제목·본문을 만드는 내장 AI.
    /// 제목: 뉴스 300종(+ 플레이 중 스스로 만든 뉴스, AIMemory)을 학습 데이터로
    ///   ① 어절 단위 마르코프 모델로 새 문장 생성
    ///   ② 앞절/뒷절 재조합 ("○○ 발표… △△ 기대")
    ///   ③ 숫자가 들어가는 생성 문형
    /// 중 하나를 고르고, 문법·길이·중복 검사를 통과한 결과만 쓴다.
    /// 본문: 기사 문형(리드 → 세부 → 애널리스트 코멘트 → 시세 → 전망)에 실제 시세를 채워 넣는다.
    /// </summary>
    public static class NewsAI
    {
        public const string SrcInternal = "내장 AI";
        public const string SrcDesk = "편집국";

        class Corpus
        {
            public readonly List<string> lines = new List<string>();
            public readonly HashSet<string> set = new HashSet<string>();
            public readonly Dictionary<string, List<string>> markov = new Dictionary<string, List<string>>();
            public readonly List<string> heads = new List<string>();   // "{0} …" 앞절
            public readonly List<string> tails = new List<string>();   // "… ○○" 뒷절

            public void Add(string line)
            {
                if (string.IsNullOrEmpty(line) || line.IndexOf("{0}", StringComparison.Ordinal) < 0 || !set.Add(line)) return;
                lines.Add(line);
                var toks = line.Split(' ');
                string prev = "<s>";
                foreach (var t in toks) { Push(prev, t); prev = t; }
                Push(prev, "</s>");
                int i = line.IndexOf("… ", StringComparison.Ordinal);
                if (i > 0)
                {
                    string h = line.Substring(0, i), tl = line.Substring(i + 2);
                    if (h.Contains("{0}")) heads.Add(h);
                    if (!tl.Contains("{0}")) tails.Add(tl);
                }
                else heads.Add(line);
            }

            void Push(string a, string b)
            {
                List<string> l;
                if (!markov.TryGetValue(a, out l)) { l = new List<string>(); markov[a] = l; }
                l.Add(b);
            }
        }

        static Dictionary<NewsKind, Corpus> corpora;
        public static int LearnedCount { get; private set; }

        // 반응 뒷절 (재조합에 섞어 쓰는 짧은 꼬리)
        static readonly string[] GoodTails = { "주가 강세 기대", "투자심리 개선", "목표가 줄상향 전망", "개미들 환호", "외국인 매수세 유입", "52주 신고가 기대감", "실적 개선 신호", "시장 기대 웃돌아" };
        static readonly string[] BadTails = { "투자자 불안 확산", "주가 급락 우려", "신뢰도 타격", "개미들 '패닉'", "외국인 매도 행렬", "실적 악화 불가피", "시장 실망감 확산", "추가 하락 경고" };
        static readonly string[] CoinGoodTails = { "커뮤니티 축제 분위기", "김치 프리미엄 확대", "거래량 폭발", "고래 매수세 유입", "신고가 기대감", "홀더들 환호" };
        static readonly string[] CoinBadTails = { "투자자 '패닉셀'", "커뮤니티 공포 확산", "거래량 급감", "고래 이탈 우려", "신뢰도 추락", "추가 하락 경고" };

        // 숫자가 들어가는 생성 문형
        static readonly string[] GoodPatterns =
        {
            "{0}, {Q}분기 영업이익 {P}% 증가… 시장 예상 상회",
            "{0}, {C} 업체와 {M} 규모 공급 계약",
            "{0}, {M} 규모 자사주 매입 발표",
            "{0} 목표주가 {P}% 상향… \"저평가 국면\"",
            "{0}, {C} 시장 점유율 {N}%로 확대",
            "{0}, 신규 수주 {M}… 창사 이래 최대",
            "외국인, {0} {N}거래일 연속 순매수",
            "{0}, 연간 매출 가이던스 {P}% 상향",
        };
        static readonly string[] BadPatterns =
        {
            "{0}, {Q}분기 영업이익 {P}% 감소… 어닝 쇼크",
            "{0}, {C} 공장 가동 중단… {M} 손실 우려",
            "{0}, {M} 규모 유상증자 결정",
            "{0} 목표주가 {P}% 하향… \"눈높이 낮춰야\"",
            "기관, {0} {N}거래일 연속 순매도",
            "{0}, {C} 규제 당국 과징금 {M}",
            "{0}, 핵심 고객사 {N}곳 이탈",
            "{0}, 재고 {P}% 급증… 수익성 경고",
        };
        static readonly string[] CoinGoodPatterns =
        {
            "{0}, {C} 대형 거래소 상장 확정… 거래량 {N}배",
            "고래 지갑, {0} {M} 규모 매집",
            "{0} 생태계 펀드 {M} 조성",
            "{0} 활성 지갑 {N}만 개 돌파",
            "{0}, 공급량 {P}% 소각 결정",
        };
        static readonly string[] CoinBadPatterns =
        {
            "{0} 브릿지 해킹… 피해 {M}",
            "고래 지갑, {0} {M} 규모 거래소 입금",
            "{0}, {C} 규제 당국 조사 착수",
            "{0} 락업 물량 {P}% 해제 임박",
            "{0} 네트워크 {N}시간째 중단",
        };
        static readonly string[] Countries = { "미국", "유럽", "일본", "중국", "인도", "중동", "동남아", "독일", "영국", "브라질" };

        static void Ensure()
        {
            if (corpora != null) return;
            corpora = new Dictionary<NewsKind, Corpus>();
            foreach (NewsKind k in new[] { NewsKind.CompanyGood, NewsKind.CompanyBad, NewsKind.CoinGood, NewsKind.CoinBad })
                corpora[k] = new Corpus();
            foreach (var t in NewsCatalog.All)
                if (t.sectors == null && corpora.ContainsKey(t.kind)) corpora[t.kind].Add(t.text);
        }

        /// <summary>새 뉴스 제목을 학습 데이터에 추가. 회사 이름은 {0}으로 바꿔서 넣는다.</summary>
        public static bool Learn(NewsKind kind, string headline, string companyName)
        {
            Ensure();
            Corpus c;
            if (!corpora.TryGetValue(kind, out c) || string.IsNullOrEmpty(headline)) return false;
            string line = headline.Replace("[속보] ", "").Trim();
            if (!string.IsNullOrEmpty(companyName)) line = line.Replace(companyName, "{0}");
            if (CountOf(line, "{0}") != 1 || line.Length > 60) return false;
            int before = c.lines.Count;
            c.Add(line);
            if (c.lines.Count > before) LearnedCount++;
            return c.lines.Count > before;
        }

        // ───────────────────────── 제목 ─────────────────────────

        /// <summary>회사/코인 뉴스 제목 생성. template은 이 뉴스가 뽑힌 원래 문형 (가끔 그대로 쓴다).</summary>
        public static string Headline(NewsTemplate template, MarketAsset a, Random rng, out string source)
        {
            Ensure();
            Corpus c = corpora[template.kind];
            bool coin = template.IsCoin;
            bool good = template.IsGood;
            for (int tries = 0; tries < 40; tries++)
            {
                double r = rng.NextDouble();
                string line;
                if (r < 0.08 || template.sectors != null && r < 0.25) { line = Mutate(template.text, rng); source = SrcDesk; }
                else if (r < 0.42) { line = MarkovLine(c, rng); source = SrcInternal; }
                else if (r < 0.74) { line = Recombine(c, coin, good, rng); source = SrcInternal; }
                else { line = Pattern(coin, good, rng); source = SrcInternal; }
                if (line == null || !Valid(line)) continue;
                return line.Replace("{0}", a.name);
            }
            source = SrcDesk;
            return template.text.Replace("{0}", a.name);
        }

        static string MarkovLine(Corpus c, Random rng)
        {
            var sb = new StringBuilder();
            string cur = "<s>";
            int n = 0;
            while (n < 11)
            {
                List<string> next;
                if (!c.markov.TryGetValue(cur, out next)) return null;
                cur = next[rng.Next(next.Count)];
                if (cur == "</s>") break;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(cur);
                n++;
            }
            string s = sb.ToString();
            if (n < 4 || n > 10 || c.set.Contains(s)) return null;
            return Mutate(s, rng);
        }

        static string Recombine(Corpus c, bool coin, bool good, Random rng)
        {
            if (c.heads.Count == 0) return null;
            string head = c.heads[rng.Next(c.heads.Count)];
            if (head.Contains("…")) head = head.Substring(0, head.IndexOf('…'));
            string tail;
            string[] extra = coin ? (good ? CoinGoodTails : CoinBadTails) : (good ? GoodTails : BadTails);
            if (c.tails.Count > 0 && rng.NextDouble() < 0.5) tail = c.tails[rng.Next(c.tails.Count)];
            else tail = extra[rng.Next(extra.Length)];
            string s = head.TrimEnd() + "… " + tail;
            if (c.set.Contains(s)) return null;
            return Mutate(s, rng);
        }

        static string Pattern(bool coin, bool good, Random rng)
        {
            string[] pool = coin ? (good ? CoinGoodPatterns : CoinBadPatterns) : (good ? GoodPatterns : BadPatterns);
            string s = pool[rng.Next(pool.Length)];
            s = s.Replace("{Q}", (1 + rng.Next(4)).ToString())
                 .Replace("{P}", (good ? rng.Next(12, 180) : rng.Next(8, 70)).ToString())
                 .Replace("{N}", (2 + rng.Next(18)).ToString())
                 .Replace("{C}", Countries[rng.Next(Countries.Length)])
                 .Replace("{M}", Money(rng));
            return s;
        }

        // 뒤에 '주/년/분기/위/대'가 오는 숫자는 고유 표현이라 바꾸지 않는다 (52주 신고가, 3년 만에)
        static readonly Regex Digits = new Regex("(?<![0-9])[0-9]+(?![0-9]|주|년|분기|위|대|월)");

        /// <summary>문장 속 숫자를 그럴듯한 다른 숫자로 바꿔 매번 조금씩 다르게.</summary>
        static string Mutate(string s, Random rng)
        {
            return Digits.Replace(s, m =>
            {
                int v;
                if (!int.TryParse(m.Value, out v)) return m.Value;
                if (v <= 1) return m.Value;
                int nv = Math.Max(2, (int)Math.Round(v * (0.5 + rng.NextDouble())));
                return nv.ToString();
            });
        }

        static bool Valid(string s)
        {
            if (CountOf(s, "{0}") != 1) return false;
            if (s.Length < 10 || s.Length > 48) return false;
            if (CountOf(s, ",") > 1 || CountOf(s, "…") > 1) return false;
            if (s.EndsWith(",") || s.EndsWith("…")) return false;
            if (CountOf(s, "\"") % 2 != 0) return false;
            return true;
        }

        static int CountOf(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }

        public static string Money(Random rng)
        {
            double r = rng.NextDouble();
            if (r < 0.5) return (rng.Next(1, 99) * 100) + "억 원";
            if (r < 0.85) return rng.Next(1, 9) + "조 원";
            return rng.Next(1, 9) + "조 " + rng.Next(1, 9) + "천억 원";
        }

        // ───────────────────────── 본문 ─────────────────────────

        static readonly string[] Firms = { "BOD투자증권", "한빛증권", "미래알파증권", "대양증권", "KS리서치", "글로벌마켓리서치", "누리증권", "청람자산운용" };
        static readonly string[] CoinFirms = { "크립토인사이트", "블록체인리서치랩", "코인메트릭랩", "온체인애널리틱스", "디지털자산연구소" };
        static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권" };
        static readonly string[] Given = { "민준", "서연", "도윤", "하은", "지호", "수아", "현우", "지민", "준혁", "예린", "태윤", "가영", "성민", "다은", "재원", "소희" };

        static readonly string[] GoodDetail =
        {
            "회사 측은 이번 성과로 연간 매출이 약 {P}% 늘어날 것으로 내다봤다.",
            "업계에서는 이번 발표가 그동안 부진했던 실적의 반전 신호라는 평가가 나온다.",
            "공시 직후 매수 주문이 몰리며 거래량이 평소의 {N}배 수준으로 치솟았다.",
            "증권가는 내년 영업이익 추정치를 일제히 {P}%가량 올려 잡았다.",
            "회사 관계자는 \"올해 하반기부터 본격적인 성과가 숫자로 확인될 것\"이라고 밝혔다.",
            "경쟁사 대비 기술 격차가 벌어지고 있다는 분석도 잇따랐다.",
        };
        static readonly string[] BadDetail =
        {
            "회사 측은 이번 사태로 연간 이익이 약 {P}% 줄어들 수 있다고 밝혔다.",
            "시장에서는 그동안 제기돼 온 우려가 현실화됐다는 반응이 나온다.",
            "공시 직후 매도 주문이 쏟아지며 거래량이 평소의 {N}배로 불어났다.",
            "증권가는 실적 추정치를 줄줄이 {P}%가량 낮추고 있다.",
            "회사 관계자는 \"사태 수습에 최선을 다하겠다\"는 짧은 입장만 내놨다.",
            "일부 기관은 보유 물량을 정리하기 시작한 것으로 알려졌다.",
        };
        static readonly string[] CoinGoodDetail =
        {
            "온체인 데이터에 따르면 최근 {N}일간 대형 지갑의 보유량이 꾸준히 늘었다.",
            "커뮤니티에서는 '이번엔 다르다'는 기대 섞인 글이 쏟아지고 있다.",
            "주요 거래소의 거래량이 하루 만에 {N}배 가까이 불어났다.",
            "재단 측은 \"생태계 확장이 이제 막 시작됐다\"고 밝혔다.",
        };
        static readonly string[] CoinBadDetail =
        {
            "온체인 데이터에 따르면 대형 지갑에서 거래소로의 이체가 급증했다.",
            "커뮤니티에서는 '탈출해야 한다'는 공포 섞인 글이 이어지고 있다.",
            "일부 거래소는 해당 코인에 대한 투자 유의 안내를 띄웠다.",
            "재단 측은 \"사실관계를 확인 중\"이라는 입장만 밝혔다.",
        };
        static readonly string[] GoodQuote =
        {
            "단기 모멘텀뿐 아니라 중장기 체력도 좋아지고 있다",
            "이번 이슈로 밸류에이션 재평가가 불가피하다",
            "지금 주가는 아직 이번 호재를 다 반영하지 못했다",
            "업황 회복과 맞물려 실적 개선 속도가 빨라질 것",
        };
        static readonly string[] BadQuote =
        {
            "불확실성이 해소되기 전까지는 보수적으로 접근할 필요가 있다",
            "단기 반등보다는 추가 조정 가능성에 무게를 둔다",
            "이번 이슈가 실적에 미칠 영향은 생각보다 클 수 있다",
            "신뢰 회복에는 상당한 시간이 걸릴 것",
        };
        static readonly string[] CoinGoodQuote = { "수급이 바뀌는 초입 단계로 보인다", "변동성은 크겠지만 추세는 위쪽이다" };
        static readonly string[] CoinBadQuote = { "레버리지 포지션의 연쇄 청산을 경계해야 한다", "신뢰가 무너진 자산은 회복이 더디다" };
        static readonly string[] GoodClose = { "시장은 후속 발표에 주목하고 있다.", "당분간 매수세가 이어질 것이라는 전망이 우세하다.", "다만 단기 급등에 따른 차익 실현 매물에는 주의가 필요하다." };
        static readonly string[] BadClose = { "시장은 회사의 추가 대응을 지켜보고 있다.", "당분간 변동성이 커질 수 있어 주의가 필요하다.", "저가 매수 기회라는 의견도 있지만 신중론이 우세하다." };

        /// <summary>뉴스 상세 본문. 시세 부분은 열 때마다 현재 가격으로 다시 계산된다.</summary>
        public static string Body(NewsItem n, MarketEngine e, Random rng)
        {
            var a = n.asset;
            string date = Fmt.FullDate(n.minute) + " " + Fmt.Clock(n.minute);
            var sb = new StringBuilder();
            string core = Core(n.headline, a);
            bool good = n.tone == NewsTone.Good;

            switch (n.cat)
            {
                case NewsCat.Company:
                case NewsCat.Coin:
                {
                    bool coin = a != null && a.IsCoin;
                    sb.Append(a.name).Append('(').Append(a.ticker).Append(')').Append(Josa(a.name, "이", "가"))
                      .Append(" ‘").Append(core).Append("’ 소식을 전했다. ")
                      .Append(date).Append(" 전해진 이번 소식에 ").Append(coin ? "코인 시장" : a.sector + " 업종")
                      .Append(" 투자자들의 시선이 쏠리고 있다.\n\n");
                    var pool = coin ? (good ? CoinGoodDetail : CoinBadDetail) : (good ? GoodDetail : BadDetail);
                    int i1 = rng.Next(pool.Length), i2 = (i1 + 1 + rng.Next(pool.Length - 1)) % pool.Length;
                    sb.Append(Fill(pool[i1], rng)).Append(' ').Append(Fill(pool[i2], rng)).Append("\n\n");
                    string firm = (coin ? CoinFirms : Firms)[rng.Next(coin ? CoinFirms.Length : Firms.Length)];
                    var q = coin ? (good ? CoinGoodQuote : CoinBadQuote) : (good ? GoodQuote : BadQuote);
                    sb.Append(firm).Append(' ').Append(Person(rng)).Append(coin ? " 애널리스트는 \"" : " 연구원은 \"")
                      .Append(q[rng.Next(q.Length)]).Append("\"라고 말했다.\n\n");
                    AppendPrice(sb, n, e);
                    sb.Append((good ? GoodClose : BadClose)[rng.Next(3)]);
                    break;
                }
                case NewsCat.Sector:
                case NewsCat.Macro:
                case NewsCat.Regime:
                {
                    sb.Append(date).Append(", ").Append(core).Append(Josa(core, "이라는", "라는")).Append(" 소식이 시장 전체를 흔들고 있다.\n\n");
                    sb.Append(n.tone == NewsTone.Bad || n.headline.Contains("폭락") || n.headline.Contains("긴급")
                        ? "투자자들은 위험자산 비중을 빠르게 줄이는 모습이다. 시장 지수의 변동성도 평소보다 커졌다.\n\n"
                        : "위험자산 선호 심리가 살아나며 매수세가 넓게 퍼지고 있다. 거래대금도 평소보다 크게 늘었다.\n\n");
                    double ch;
                    double dom = e.Index(AssetKind.Domestic, out ch);
                    sb.Append("현재 BOD 국내지수는 ").Append(dom.ToString("#,0.00")).Append(" (").Append(Fmt.Pct(ch)).Append(")");
                    double ovs = e.Index(AssetKind.Overseas, out ch);
                    sb.Append(", GLX 해외지수는 ").Append(ovs.ToString("#,0.00")).Append(" (").Append(Fmt.Pct(ch)).Append(")");
                    double cry = e.Index(AssetKind.Coin, out ch);
                    sb.Append(", 크립토 지수는 ").Append(cry.ToString("#,0.00")).Append(" (").Append(Fmt.Pct(ch)).Append(")를 기록하고 있다.\n\n");
                    sb.Append(Person(rng)).Append(" ").Append(Firms[rng.Next(Firms.Length)]).Append(" 시황 담당은 \"")
                      .Append(n.cat == NewsCat.Regime ? "이런 장에서는 레버리지를 낮추고 현금 비중을 지키는 것이 최선" : "업종별로 온도 차가 클 수 있어 종목별 대응이 필요하다")
                      .Append("\"고 조언했다.");
                    break;
                }
                case NewsCat.Listing:
                {
                    if (a == null)
                    {
                        // 상장 예고 (아직 거래 전)
                        sb.Append(core).Append(".\n\n")
                          .Append("상장 당일에는 가격 제한 없이 거래가 시작돼 변동성이 매우 크다. 공모가 대비 몇 배로 뛰는 경우도, 반대로 급락하는 경우도 있다.\n\n")
                          .Append(Person(rng)).Append(" ").Append(Firms[rng.Next(Firms.Length)])
                          .Append(" 연구원은 \"첫날은 수급 싸움이라 분할 매수로 접근하는 것이 좋다\"고 조언했다.");
                        break;
                    }
                    sb.Append(a.name).Append('(').Append(a.ticker).Append(')').Append(Josa(a.name, "이", "가")).Append(' ')
                      .Append(a.IsCoin ? "거래소에 신규 상장돼 거래를 시작했다." : a.KindLabel.Replace("주식", "") + " 시장에 새로 상장했다.")
                      .Append(" 섹터는 ").Append(a.sector).Append(", 시작가는 ").Append(Fmt.Price(a.initPrice)).Append("원이다.\n\n");
                    if (!a.IsCoin)
                        sb.Append("공모 청약에는 ").Append(rng.Next(300, 2500).ToString("#,0")).Append(" 대 1의 경쟁률이 몰렸고, 증거금으로만 ")
                          .Append(Money(rng)).Append("이 들어온 것으로 집계됐다.\n\n");
                    else
                        sb.Append("상장 전부터 커뮤니티에서 화제가 됐던 프로젝트로, 상장 직후 거래량이 빠르게 늘고 있다.\n\n");
                    sb.Append("신규 상장 종목은 첫날 가격 제한이 없어 변동성이 매우 크다. 공모가 대비 급등하는 경우도, 급락하는 경우도 흔하다.\n\n");
                    AppendPrice(sb, n, e);
                    break;
                }
                default:
                {
                    if (a != null)
                    {
                        sb.Append(core).Append(".\n\n");
                        if (a.status == ListingStatus.Warning || n.headline.Contains("지정"))
                            sb.Append("거래소는 ").Append(a.name).Append("의 재무 건전성과 공시 위반 여부를 심사할 예정이다. 심사 결과에 따라 상장이 유지되거나 정리매매에 들어간다.\n\n");
                        else if (a.Delisted || n.headline.Contains("상장폐지"))
                            sb.Append("상장폐지가 확정되면 보유 주식(코인)은 시장에서 더 이상 거래할 수 없다. 게임에서는 남은 보유분이 0원으로 처리된다. 숏 포지션은 마지막 가격으로 정산된다.\n\n");
                        AppendPrice(sb, n, e);
                    }
                    else sb.Append(core).Append(".\n\n특별한 시장 영향은 없는 생활·시사 소식이다.");
                    break;
                }
            }
            return sb.ToString().TrimEnd();
        }

        static void AppendPrice(StringBuilder sb, NewsItem n, MarketEngine e)
        {
            var a = n.asset;
            if (a == null) return;
            if (a.Delisted)
            {
                sb.Append("현재 ").Append(a.name).Append("은(는) 상장폐지되어 거래가 종료된 상태다.\n\n");
                return;
            }
            double ch = n.priceAt > 0 ? (a.price / n.priceAt - 1) * 100 : 0;
            sb.Append("보도 당시 ").Append(a.name).Append("의 가격은 ").Append(Fmt.Price(n.priceAt)).Append("원이었고, 현재는 ")
              .Append(Fmt.Price(a.price)).Append("원으로 보도 이후 ").Append(Fmt.Pct(ch)).Append(Math.Abs(ch) < 0.05 ? " 움직였다" : (ch > 0 ? " 올랐다" : " 내렸다"))
              .Append(".\n\n");
        }

        static string Fill(string s, Random rng)
        {
            return s.Replace("{P}", rng.Next(5, 60).ToString()).Replace("{N}", (2 + rng.Next(9)).ToString());
        }

        static string Person(Random rng) { return Surnames[rng.Next(Surnames.Length)] + Given[rng.Next(Given.Length)]; }

        /// <summary>제목에서 말머리와 회사 이름을 걷어내 본문에 넣을 핵심 구절만 남긴다.</summary>
        static string Core(string headline, MarketAsset a)
        {
            string s = Regex.Replace(headline ?? "", "^\\[[^\\]]+\\]\\s*", "");
            if (a != null)
            {
                if (s.StartsWith(a.name + ", ")) s = s.Substring(a.name.Length + 2);
                else if (s.StartsWith(a.name + " ")) s = s.Substring(a.name.Length + 1);
            }
            return s.Trim();
        }

        /// <summary>받침 유무에 따라 조사 선택 (이/가, 은/는, 이라는/라는).</summary>
        public static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word)) return without;
            char c = word[word.Length - 1];
            bool b;
            if (c >= 0xAC00 && c <= 0xD7A3) b = (c - 0xAC00) % 28 != 0;
            else if (char.IsDigit(c)) b = "013678".IndexOf(c) >= 0;
            else b = "LMNRlmnr".IndexOf(c) >= 0;
            return b ? withBatchim : without;
        }

        /// <summary>테스트용: 제목 샘플.</summary>
        public static string Sample(NewsKind kind, string company, int n, int seed)
        {
            var rng = new Random(seed);
            NewsTemplate t = null;
            foreach (var x in NewsCatalog.All) if (x.kind == kind && x.sectors == null) { t = x; break; }
            var a = new MarketAsset(0, company, "TEST", "반도체", kind == NewsKind.CoinGood || kind == NewsKind.CoinBad ? AssetKind.Coin : AssetKind.Domestic, 10000, 0.002, 1);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string src;
                sb.Append(Headline(t, a, rng, out src)).Append("  [").Append(src).Append("]\n");
            }
            return sb.ToString();
        }
    }
}

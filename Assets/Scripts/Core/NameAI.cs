using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BuyOrDie
{
    /// <summary>
    /// 신규 상장 종목 이름을 만드는 내장 AI.
    /// 1) 실제 기업·코인 이름(학습 데이터)으로 글자 단위 n-gram 언어 모델을 학습하고,
    /// 2) 모델이 만든 이름 + 섹터 어미 조합을 만든 뒤,
    /// 3) 게임 속 다른 종목들처럼 한 글자를 비트는 '패러디 변형'을 거친다 (삼성→삼송).
    /// 실제 이름과 완전히 같은 결과는 버린다. 게임에서 상장된 이름도 학습 데이터에 쌓인다 (AIMemory).
    /// </summary>
    public static class NameAI
    {
        // ───────────────────────── 학습 데이터 (실제 이름 · 출력에는 그대로 쓰지 않음) ─────────────────────────

        const string DomesticCorpus =
            "삼성전자 SK하이닉스 LG에너지솔루션 현대자동차 기아 셀트리온 포스코홀딩스 네이버 카카오 KB금융 신한지주 현대모비스 LG전자 삼성물산 " +
            "SK이노베이션 삼성SDI LG화학 하나금융지주 한국전력 SK텔레콤 삼성전기 한화에어로스페이스 HD현대중공업 두산에너빌리티 크래프톤 " +
            "엔씨소프트 넷마블 하이브 에스엠 JYP엔터테인먼트 CJ제일제당 오리온 농심 아모레퍼시픽 대한항공 삼성중공업 한미반도체 에코프로 " +
            "에코프로비엠 알테오젠 유한양행 고려아연 카카오뱅크 삼성SDS 현대건설 위메이드 롯데케미칼 롯데쇼핑 한화솔루션 한화오션 한국가스공사 " +
            "한국항공우주 현대글로비스 현대제철 대우건설 GS건설 삼성화재 삼성생명 미래에셋증권 키움증권 한국금융지주 우리금융지주 기업은행 " +
            "메리츠금융지주 삼성바이오로직스 SK바이오팜 한미약품 대웅제약 녹십자 셀트리온제약 휴젤 펄어비스 컴투스 카카오게임즈 데브시스터즈 " +
            "스튜디오드래곤 와이지엔터테인먼트 엘앤에프 포스코퓨처엠 천보 솔브레인 동진쎄미켐 리노공업 이오테크닉스 원익IPS 주성엔지니어링 " +
            "피에스케이 하나마이크론 레인보우로보틱스 두산로보틱스 현대미포조선 LIG넥스원 한화시스템 풍산 효성중공업 LS일렉트릭 대한전선 " +
            "일진전기 코웨이 이마트 BGF리테일 GS리테일 한샘 하이트진로 롯데칠성 빙그레 삼양식품 오뚜기 대상 동원산업 한섬 신세계 현대백화점 " +
            "호텔신라 파라다이스 강원랜드 하나투어 제주항공 진에어 티웨이항공 팬오션 대한해운 씨젠 에스디바이오센서 클래시스 파마리서치 덴티움 " +
            "오스템임플란트 루닛 뷰노 더존비즈온 안랩 다우기술 한글과컴퓨터 포스코DX 현대오토에버 삼성엔지니어링";

        const string OverseasCorpus =
            "애플 마이크로소프트 엔비디아 아마존 알파벳 메타 테슬라 브로드컴 버크셔해서웨이 일라이릴리 비자 제이피모건 월마트 엑손모빌 마스터카드 " +
            "넷플릭스 코스트코 오라클 어도비 세일즈포스 인텔 퀄컴 디즈니 나이키 코카콜라 맥도날드 스타벅스 보잉 우버 에어비앤비 팔란티어 쇼피파이 " +
            "페이팔 로블록스 코인베이스 마이크로스트래티지 소니 토요타 닌텐도 알리바바 텐센트 리비안 루시드 크라우드스트라이크 스노우플레이크 시스코 " +
            "아이비엠 텍사스인스트루먼트 마이크론 어플라이드머티리얼즈 램리서치 케이엘에이 시놉시스 케이던스 서비스나우 워크데이 데이터독 몽고디비 " +
            "도큐사인 스포티파이 핀터레스트 스냅 리프트 도어대시 엣시 이베이 부킹홀딩스 익스피디아 메리어트 힐튼 펩시코 프록터앤드갬블 " +
            "존슨앤드존슨 화이자 머크 애브비 모더나 길리어드 암젠 버텍스 유나이티드헬스 골드만삭스 모건스탠리 뱅크오브아메리카 씨티그룹 웰스파고 " +
            "블랙록 아메리칸익스프레스 캐터필러 디어 허니웰 록히드마틴 레이시온 제너럴일렉트릭 제너럴모터스 포드 페라리 폭스바겐 지멘스 " +
            "에이에스엠엘 노보노디스크 네슬레 로레알 루이비통 에르메스 샤오미 바이두 비야디 니오 징둥닷컴 핀둬둬 소프트뱅크 키엔스 " +
            "도쿄일렉트론 패스트리테일링 히타치 파나소닉 캐논";

        const string CoinCorpus =
            "비트코인 이더리움 바이낸스코인 리플 솔라나 도지코인 카르다노 트론 아발란체 시바이누 체인링크 폴카닷 톤코인 비트코인캐시 " +
            "니어프로토콜 라이트코인 폴리곤 유니스왑 페페 앱토스 수이 인터넷컴퓨터 이더리움클래식 스텔라루멘 코스모스 파일코인 아비트럼 " +
            "옵티미즘 헤데라 크로노스 메이커 알고랜드 더그래프 에이브 샌드박스 디센트럴랜드 엑시인피니티 테조스 이오스 쎄타 렌더 페치에이아이 " +
            "월드코인 봉크 도그위프햇 플로키 이뮤터블 갈라 엔진코인 커브 세럼 웨이브스 질리카 퀀텀 네오 아이오타 대시 지캐시 모네로 비체인 " +
            "하모니 셀로 칠리즈 스택스 카스파 인젝티브 세이 셀레스티아 스타크넷 지케이싱크 레이어제로 주피터 피스네트워크 온도 에테나 웜홀 " +
            "지토 다이 스토리 무브먼트 버추얼 펭구 무뎅 포켓네트워크 오아시스 아르위브 헬륨 아카시 골렘 오션프로토콜 싱귤래리티넷 브렛 터보";

        // 섹터별 어미 (조합형 이름용)
        static readonly Dictionary<string, string[]> Suffix = new Dictionary<string, string[]>
        {
            { "반도체", new[] { "반도체", "세미콘", "마이크론", "테크", "실리콘" } },
            { "2차전지", new[] { "에너지", "배터리", "머티리얼즈", "솔루션", "케미칼" } },
            { "바이오", new[] { "바이오", "제약", "파마", "바이오텍", "헬스케어", "메디칼" } },
            { "헬스케어", new[] { "헬스", "파마슈티컬스", "바이오사이언스", "메디컬" } },
            { "자동차", new[] { "모터스", "오토", "모빌리티", "자동차" } },
            { "전기차", new[] { "모터스", "EV", "모빌리티", "오토" } },
            { "게임", new[] { "게임즈", "소프트", "스튜디오", "인터랙티브" } },
            { "엔터", new[] { "엔터테인먼트", "미디어", "뮤직", "엔터" } },
            { "플랫폼", new[] { "플랫폼", "넷", "닷컴", "랩스" } },
            { "금융", new[] { "금융지주", "증권", "캐피탈", "은행", "파이낸셜" } },
            { "핀테크", new[] { "페이", "파이낸셜", "뱅크", "핀테크" } },
            { "조선", new[] { "중공업", "조선", "마린" } },
            { "방산", new[] { "에어로", "디펜스", "시스템" } },
            { "항공", new[] { "항공", "에어", "에어로" } },
            { "화학", new[] { "케미칼", "화학", "소재" } },
            { "에너지", new[] { "에너지", "파워", "전력", "가스" } },
            { "건설", new[] { "건설", "개발", "E&C" } },
            { "식품", new[] { "식품", "푸드", "제과" } },
            { "화장품", new[] { "코스메틱", "뷰티" } },
            { "철강", new[] { "스틸", "제철", "금속" } },
            { "통신", new[] { "텔레콤", "네트웍스" } },
            { "전자", new[] { "전자", "일렉트릭", "디스플레이" } },
            { "소프트웨어", new[] { "소프트", "시스템즈", "클라우드", "테크놀로지스" } },
            { "빅테크", new[] { "테크놀로지스", "시스템즈", "랩스", "그룹" } },
            { "중국테크", new[] { "그룹", "테크", "홀딩스" } },
            { "소비재", new[] { "브랜즈", "컴퍼니", "그룹" } },
            { "해운", new[] { "해운", "마린", "쉬핑" } },
            { "메이저", new[] { "코인", "캐시" } },
            { "레이어1", new[] { "체인", "네트워크", "프로토콜" } },
            { "레이어2", new[] { "롤업", "링크", "레이어" } },
            { "디파이", new[] { "스왑", "파이낸스", "볼트", "랜드" } },
            { "밈", new[] { "코인", "이누", "도그", "캣" } },
            { "AI", new[] { "에이아이", "뉴럴", "마인드" } },
            { "메타버스", new[] { "버스", "랜드", "월드" } },
        };

        static Dictionary<AssetKind, Model> models;
        static readonly HashSet<string> realNames = new HashSet<string>();
        static readonly List<string> learned = new List<string>();

        class Model
        {
            public readonly Dictionary<string, List<string>> o2 = new Dictionary<string, List<string>>();
            public readonly Dictionary<string, List<string>> o1 = new Dictionary<string, List<string>>();
            public readonly List<string> stems = new List<string>();
        }

        static readonly Regex Tok = new Regex("[A-Z&]+|[가-힣]");

        static List<string> Tokens(string name)
        {
            var list = new List<string>();
            foreach (Match m in Tok.Matches(name)) list.Add(m.Value);
            return list;
        }

        static void Ensure()
        {
            if (models != null) return;
            models = new Dictionary<AssetKind, Model>();
            models[AssetKind.Domestic] = Train(DomesticCorpus);
            models[AssetKind.Overseas] = Train(OverseasCorpus);
            models[AssetKind.Coin] = Train(CoinCorpus);
        }

        static Model Train(string corpus)
        {
            var m = new Model();
            foreach (var n in corpus.Split(' '))
            {
                if (n.Length == 0) continue;
                realNames.Add(n);
                Learn(m, n);
            }
            return m;
        }

        static void Learn(Model m, string name)
        {
            var t = Tokens(name);
            if (t.Count == 0) return;
            var seq = new List<string> { "^", "^" };
            seq.AddRange(t);
            seq.Add("$");
            for (int i = 2; i < seq.Count; i++)
            {
                Add(m.o2, seq[i - 2] + "|" + seq[i - 1], seq[i]);
                Add(m.o1, seq[i - 1], seq[i]);
            }
            if (t.Count >= 3) m.stems.Add(t[0] + t[1]);
        }

        static void Add(Dictionary<string, List<string>> d, string k, string v)
        {
            List<string> l;
            if (!d.TryGetValue(k, out l)) { l = new List<string>(); d[k] = l; }
            l.Add(v);
        }

        /// <summary>새 이름(게임에서 상장된 종목 등)을 모델에 학습시킨다.</summary>
        public static void LearnName(AssetKind kind, string name)
        {
            Ensure();
            if (string.IsNullOrEmpty(name) || learned.Contains(name)) return;
            learned.Add(name);
            Learn(models[kind], name);
        }

        // ───────────────────────── 생성 ─────────────────────────

        public static void Generate(AssetKind kind, string sector, Random rng, ICollection<string> taken, ICollection<string> takenTickers,
            out string name, out string ticker)
        {
            Ensure();
            var m = models[kind];
            name = null;
            for (int tries = 0; tries < 200 && name == null; tries++)
            {
                string cand = rng.NextDouble() < 0.45 ? Walk(m, rng, true) : Compose(m, sector, rng);
                if (cand == null) continue;
                // 항상 한 글자를 비튼다 → 실제 회사 이름이 그대로 나오지 않게
                string parody = Parody(cand, rng);
                if (parody == cand) continue;
                cand = parody;
                if (cand.Length < 3 || cand.Length > 9) continue;
                if (realNames.Contains(cand) || taken.Contains(cand) || HasAwkward(cand)) continue;
                name = cand;
            }
            if (name == null) name = "뉴" + Parody("상장" + rng.Next(10, 99), rng);
            ticker = MakeTicker(name, kind, rng, takenTickers);
        }

        static string Walk(Model m, Random rng, bool order2)
        {
            var sb = new StringBuilder();
            string a = "^", b = "^";
            for (int i = 0; i < 9; i++)
            {
                List<string> next;
                if (order2) m.o2.TryGetValue(a + "|" + b, out next); else m.o1.TryGetValue(b, out next);
                if (next == null || next.Count == 0) break;
                string c = next[rng.Next(next.Count)];
                if (c == "$") return sb.Length >= 2 ? sb.ToString() : null;
                sb.Append(c);
                a = b; b = c;
            }
            return null;
        }

        static string Compose(Model m, string sector, Random rng)
        {
            string stem;
            if (m.stems.Count > 0 && rng.NextDouble() < 0.6) stem = m.stems[rng.Next(m.stems.Count)];
            else
            {
                var w = Walk(m, rng, true);
                if (w == null) return null;
                var t = Tokens(w);
                stem = t.Count >= 2 ? t[0] + t[1] : w;
            }
            string[] suf;
            if (sector == null || !Suffix.TryGetValue(sector, out suf)) suf = new[] { "홀딩스", "그룹", "테크" };
            return stem + suf[rng.Next(suf.Length)];
        }

        // ───────────────────────── 패러디 변형 (한 글자 비틀기) ─────────────────────────

        // 비슷하게 들리는 모음으로 바꾸기: ㅓ→ㅗ, ㅕ→ㅓ, ㅏ→ㅑ 등
        static readonly int[] VowelShift = { 4, 5, 0, 1, 8, 1, 4, 5, 13, 0, 1, 5, 8, 8, 4, 5, 20, 13, 13, 20, 20 };

        static string Parody(string s, Random rng)
        {
            var chars = s.ToCharArray();
            var idx = new List<int>();
            for (int i = 0; i < chars.Length; i++) if (chars[i] >= 0xAC00 && chars[i] <= 0xD7A3) idx.Add(i);
            if (idx.Count == 0) return s;
            // 앞쪽 두 글자를 주로 비튼다 (삼성→삼송, 현대→헌대)
            int pick = idx[rng.NextDouble() < 0.7 ? rng.Next(Math.Min(2, idx.Count)) : rng.Next(idx.Count)];
            int code = chars[pick] - 0xAC00;
            int cho = code / (21 * 28), jung = (code % (21 * 28)) / 28, jong = code % 28;
            double r = rng.NextDouble();
            if (r < 0.55 || (jong == 0 && r < 0.8)) jung = VowelShift[jung];
            else if (r < 0.8)
            {
                // 받침 바꾸기: ㄴ↔ㅇ, 없음↔ㅇ, ㄹ↔ㄴ
                if (jong == 4) jong = 21; else if (jong == 21) jong = 4; else if (jong == 8) jong = 4; else jong = 0;
            }
            else
            {
                // 예사소리 ↔ 거센소리: ㄱ↔ㅋ, ㄷ↔ㅌ, ㅂ↔ㅍ, ㅈ↔ㅊ (된소리는 예사소리로)
                int[] map = { 15, 0, 2, 16, 3, 5, 6, 17, 7, 9, 9, 11, 14, 12, 12, 0, 3, 7, 18 };
                cho = map[cho];
            }
            chars[pick] = (char)(0xAC00 + (cho * 21 + jung) * 28 + jong);
            return new string(chars);
        }

        /// <summary>어색한 글자(ㅡ 단독 모음 시작, 된소리 등)가 들어간 이름은 버린다.</summary>
        static bool HasAwkward(string s)
        {
            foreach (char ch in s)
            {
                if (ch < 0xAC00 || ch > 0xD7A3) continue;
                int code = ch - 0xAC00;
                int cho = code / (21 * 28), jung = (code % (21 * 28)) / 28;
                if (cho == 1 || cho == 4 || cho == 8 || cho == 10 || cho == 13) return true;   // ㄲㄸㅃㅆㅉ
                if (jung == 18 && cho == 11) return true;                                        // 으
                if (jung == 3 || jung == 7 || jung == 10 || jung == 19) return true;              // ㅒㅖㅙㅢ
            }
            return false;
        }

        // ───────────────────────── 티커 ─────────────────────────

        static readonly string[] ChoRoman = { "G", "K", "N", "D", "T", "R", "M", "B", "P", "S", "S", "", "J", "J", "C", "K", "T", "P", "H" };
        static readonly string[] JungRoman = { "A", "A", "Y", "Y", "E", "E", "Y", "Y", "O", "W", "W", "O", "Y", "U", "W", "W", "W", "Y", "E", "U", "I" };

        static string MakeTicker(string name, AssetKind kind, Random rng, ICollection<string> taken)
        {
            var sb = new StringBuilder();
            foreach (var t in Tokens(name))
            {
                if (t[0] < 0xAC00) { sb.Append(t.Replace("&", "")); continue; }
                int code = t[0] - 0xAC00;
                int cho = code / (21 * 28), jung = (code % (21 * 28)) / 28;
                string c = ChoRoman[cho];
                sb.Append(c.Length > 0 ? c : JungRoman[jung]);
            }
            string baseT = sb.ToString();
            int len = kind == AssetKind.Coin ? 3 + rng.Next(2) : 4;
            if (baseT.Length > len) baseT = baseT.Substring(0, len);
            while (baseT.Length < len) baseT += (char)('A' + rng.Next(26));
            string tk = baseT;
            for (int i = 0; taken.Contains(tk) && i < 60; i++)
                tk = baseT.Substring(0, len - 1) + (char)('A' + rng.Next(26));
            if (taken.Contains(tk)) tk = baseT.Substring(0, Math.Max(2, len - 2)) + rng.Next(10, 99);
            return tk;
        }

        /// <summary>테스트/디버그용: 섹터별 이름 샘플.</summary>
        public static string Sample(AssetKind kind, string sector, int n, int seed)
        {
            var rng = new Random(seed);
            var names = new List<string>();
            var tickers = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string name, tk;
                Generate(kind, sector, rng, names, tickers, out name, out tk);
                names.Add(name);
                tickers.Add(tk);
                sb.Append(name).Append('(').Append(tk).Append(") ");
            }
            return sb.ToString();
        }
    }
}

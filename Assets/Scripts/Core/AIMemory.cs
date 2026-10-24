using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuyOrDie
{
    /// <summary>
    /// 내장 AI의 학습 기억.
    /// 검사를 통과해 실제로 발표된 AI 뉴스 제목, 새로 상장된 종목 이름을 학습 데이터에 추가하고
    /// PlayerPrefs에 저장한다. 다음 실행 때 다시 학습시켜, 플레이할수록 표현이 다양해진다.
    /// 외부 서버나 API는 쓰지 않는다 (완전 오프라인 · 무료).
    /// </summary>
    public static class AIMemory
    {
        [Serializable] class LearnedLine { public int kind; public string line; }
        [Serializable] class LearnedName { public int kind; public string name; }
        [Serializable] class Store { public List<LearnedLine> lines = new List<LearnedLine>(); public List<LearnedName> names = new List<LearnedName>(); }

        const string Key = "bod_ai_memory";
        const int MaxLines = 400, MaxNames = 200;

        static Store store;
        static bool dirty;

        public static int Lines { get { Load(); return store.lines.Count; } }
        public static int Names { get { Load(); return store.names.Count; } }

        /// <summary>저장된 학습 데이터를 내장 AI에 다시 학습시킨다 (게임 시작 시 1회).</summary>
        public static void Init()
        {
            Load();
            foreach (var l in store.lines) NewsAI.Learn((NewsKind)l.kind, l.line, null);
            foreach (var n in store.names) NameAI.LearnName((AssetKind)n.kind, n.name);
        }

        static void Load()
        {
            if (store != null) return;
            store = new Store();
            try
            {
                var s = PlayerPrefs.GetString(Key, "");
                if (s.Length > 0) JsonUtility.FromJsonOverwrite(s, store);
            }
            catch { store = new Store(); }
        }

        /// <summary>발표된 뉴스를 보고, 내장 AI가 새로 쓴 기업/코인 제목이면 학습한다.</summary>
        public static void Observe(NewsItem n)
        {
            if (n == null || n.asset == null || n.source != NewsAI.SrcInternal) return;
            if (n.cat != NewsCat.Company && n.cat != NewsCat.Coin) return;
            NewsKind kind = n.cat == NewsCat.Coin
                ? (n.tone == NewsTone.Good ? NewsKind.CoinGood : NewsKind.CoinBad)
                : (n.tone == NewsTone.Good ? NewsKind.CompanyGood : NewsKind.CompanyBad);
            if (!NewsAI.Learn(kind, n.headline, n.asset.name)) return;
            Load();
            string line = n.headline.Replace("[속보] ", "").Trim().Replace(n.asset.name, "{0}");
            store.lines.Add(new LearnedLine { kind = (int)kind, line = line });
            dirty = true;
        }

        /// <summary>새로 상장된 종목 이름을 학습한다.</summary>
        public static void ObserveListing(MarketAsset a)
        {
            if (a == null) return;
            NameAI.LearnName(a.kind, a.name);
            Load();
            store.names.Add(new LearnedName { kind = (int)a.kind, name = a.name });
            dirty = true;
        }

        public static void Save()
        {
            if (!dirty || store == null) return;
            if (store.lines.Count > MaxLines) store.lines.RemoveRange(0, store.lines.Count - MaxLines);
            if (store.names.Count > MaxNames) store.names.RemoveRange(0, store.names.Count - MaxNames);
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(store));
            dirty = false;
        }
    }
}

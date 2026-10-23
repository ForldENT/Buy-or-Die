using UnityEngine;

namespace BuyOrDie
{
    /// <summary>외부 사운드 파일 없이 짧은 효과음을 코드로 합성한다. 씬이 바뀌어도 유지되는 싱글톤.</summary>
    public class Sfx : MonoBehaviour
    {
        static Sfx inst;
        public static Sfx I
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("Sfx");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<Sfx>();
                }
                return inst;
            }
        }

        AudioSource src;
        AudioClip click, buy, sell, profit, news, error, liq, win, lose, day;
        const int Rate = 44100;

        public bool muted { get { return Settings.Volume <= 0.001f; } }

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            click = Tone("click", new[] { 1800f }, 0.018f, 0.12f, 0);
            buy = Tone("buy", new[] { 660f, 990f }, 0.06f, 0.22f, 0);
            sell = Tone("sell", new[] { 880f, 587f }, 0.06f, 0.22f, 0);
            profit = Tone("profit", new[] { 784f, 988f, 1319f }, 0.06f, 0.2f, 0);
            news = Tone("news", new[] { 1046f, 1046f }, 0.05f, 0.14f, 0);
            error = Tone("error", new[] { 220f, 196f }, 0.07f, 0.2f, 1);
            liq = Tone("liq", new[] { 160f, 120f, 90f }, 0.12f, 0.28f, 1);
            win = Tone("win", new[] { 523f, 659f, 784f, 1046f, 1319f }, 0.09f, 0.22f, 0);
            lose = Tone("lose", new[] { 392f, 330f, 262f, 196f }, 0.16f, 0.24f, 1);
            day = Tone("day", new[] { 587f, 880f }, 0.09f, 0.16f, 0);
        }

        static AudioClip Tone(string name, float[] notes, float noteLen, float vol, int wave)
        {
            int per = Mathf.CeilToInt(Rate * noteLen);
            int total = per * notes.Length;
            var data = new float[total];
            for (int n = 0; n < notes.Length; n++)
            {
                float f = notes[n];
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / Rate;
                    float env = Mathf.Min(1f, i / (Rate * 0.004f)) * Mathf.Pow(1f - (float)i / per, 2f);
                    float s = Mathf.Sin(2 * Mathf.PI * f * t);
                    if (wave == 1) s = Mathf.Clamp(s * 2.2f, -1f, 1f) * 0.6f;
                    data[n * per + i] = s * env * vol;
                }
            }
            var clip = AudioClip.Create(name, total, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip c)
        {
            if (muted || c == null || src == null) return;
            src.volume = Settings.Volume;
            src.PlayOneShot(c);
        }

        public void Click() { Play(click); }
        public void Buy() { Play(buy); }
        public void Sell() { Play(sell); }
        public void Profit() { Play(profit); }
        public void News() { Play(news); }
        public void Error() { Play(error); }
        public void Liquidated() { Play(liq); }
        public void Win() { Play(win); }
        public void Lose() { Play(lose); }
        public void DayChime() { Play(day); }
    }
}

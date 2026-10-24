using System.Collections;
using UnityEngine;

namespace BuyOrDie
{
    /// <summary>
    /// 배경음악 싱글톤. 씬이 바뀌어도 끊기지 않고 계속 반복 재생된다.
    /// 볼륨은 설정의 '배경음악' 값을 따른다.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        static MusicPlayer inst;
        AudioSource src;
        float fade = 1f;

        public static void Play()
        {
            if (inst == null)
            {
                var go = new GameObject("MusicPlayer");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<MusicPlayer>();
            }
            inst.Begin();
        }

        public static void ApplyVolume()
        {
            if (inst != null && inst.src != null) inst.src.volume = Settings.MusicVolume * inst.fade;
        }

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0;
            src.priority = 0;
            src.clip = Resources.Load<AudioClip>("Audio/BGM_Calm");
        }

        void Begin()
        {
            if (src.clip == null || src.isPlaying) return;
            src.Play();
            StartCoroutine(FadeIn(2.5f));
        }

        IEnumerator FadeIn(float dur)
        {
            fade = 0;
            float t = 0;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                fade = Mathf.Clamp01(t / dur);
                ApplyVolume();
                yield return null;
            }
            fade = 1;
            ApplyVolume();
        }
    }
}

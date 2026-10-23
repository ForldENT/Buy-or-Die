using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>
    /// 씬 전환: 화면을 어둡게 덮고(페이드 아웃) → 비동기 로드 → 다시 걷어낸다(페이드 인).
    /// 씬이 바뀌어도 살아남는 오버레이 캔버스 하나로 처리한다.
    /// </summary>
    public class SceneFader : MonoBehaviour
    {
        public const string TitleScene = "Title";
        public const string GameScene = "MainGame";

        static SceneFader inst;
        CanvasGroup cg;
        TextMeshProUGUI label;
        RectTransform bar;
        bool busy;

        public static bool Busy { get { return inst != null && inst.busy; } }

        public static void Load(string scene, string message = null)
        {
            Ensure();
            if (inst.busy) return;
            inst.StartCoroutine(inst.Run(scene, message));
        }

        /// <summary>씬이 직접 시작됐을 때 검은 화면에서 부드럽게 열리게.</summary>
        public static void RevealOnStart()
        {
            Ensure();
            if (inst.busy) return;
            inst.cg.alpha = 1;
            inst.StartCoroutine(inst.Fade(1, 0, 0.5f));
        }

        static void Ensure()
        {
            if (inst != null) return;
            var go = new GameObject("SceneFader", typeof(RectTransform));
            DontDestroyOnLoad(go);
            inst = go.AddComponent<SceneFader>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            inst.cg = go.AddComponent<CanvasGroup>();
            inst.cg.alpha = 0;
            inst.cg.blocksRaycasts = false;

            var bg = UI.Img("Black", go.transform, Theme.Desktop, true);
            bg.rectTransform.Fill();
            inst.label = UI.Txt(go.transform, "", 13, Theme.Sub, false, TextAlignmentOptions.Center);
            inst.label.rectTransform.Center(600, 24, 0, 18);
            var track = UI.Img("Track", go.transform, Theme.Line);
            track.rectTransform.Center(240, 2, 0, -10);
            inst.bar = UI.Img("Bar", track.transform, Theme.Accent).rectTransform;
            inst.bar.anchorMin = Vector2.zero;
            inst.bar.anchorMax = new Vector2(0, 1);
            inst.bar.offsetMin = inst.bar.offsetMax = Vector2.zero;
        }

        IEnumerator Fade(float from, float to, float dur)
        {
            float t = 0;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                k = k * k * (3 - 2 * k);
                cg.alpha = Mathf.Lerp(from, to, k);
                yield return null;
            }
            cg.alpha = to;
        }

        IEnumerator Run(string scene, string message)
        {
            busy = true;
            cg.blocksRaycasts = true;
            label.text = message ?? "BuyOrDie OS  ·  불러오는 중";
            bar.anchorMax = new Vector2(0, 1);
            yield return Fade(cg.alpha, 1, 0.35f);

            var op = SceneManager.LoadSceneAsync(scene);
            while (!op.isDone)
            {
                bar.anchorMax = new Vector2(Mathf.Clamp01(op.progress / 0.9f), 1);
                yield return null;
            }
            bar.anchorMax = Vector2.one;
            yield return null;
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Fade(1, 0, 0.45f);
            cg.blocksRaycasts = false;
            busy = false;
        }
    }
}

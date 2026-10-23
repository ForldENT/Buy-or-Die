using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>바탕화면(어두운 배경 + 옅은 격자). 타이틀과 게임 씬이 같이 쓴다.</summary>
    public static class Desktop
    {
        public static void Build(Transform root)
        {
            UI.Img("Desktop", root, Theme.Desktop).rectTransform.Fill();
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    px[y * 32 + x] = (x == 0 || y == 0) ? new Color32(255, 255, 255, 6) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px);
            tex.Apply();
            var wp = UI.Rect("Wallpaper", root).Fill();
            var raw = wp.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            wp.gameObject.AddComponent<GridTiler>();
        }

        public static Canvas MakeCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }
    }

    public class GridTiler : MonoBehaviour
    {
        RawImage img;
        void Update()
        {
            if (img == null) img = GetComponent<RawImage>();
            var r = ((RectTransform)transform).rect;
            img.uvRect = new Rect(0, 0, r.width / 32f, r.height / 32f);
        }
    }

    /// <summary>화면을 어둡게 덮고 가운데 카드 하나를 띄우는 모달.</summary>
    public static class Modal
    {
        public static RectTransform Open(Transform root, float w, float h, out RectTransform overlay, float dim = 0.86f)
        {
            var bg = UI.Img("Modal", root, Theme.A(Theme.Desktop, dim), true);
            overlay = bg.rectTransform.Fill();
            var frame = UI.Img("Card", overlay, Theme.Line).rectTransform.Center(w, h);
            return UI.Img("Inner", frame, Theme.Window).rectTransform.Fill(1, 1, 1, 1);
        }

        public static void Confirm(Transform root, string title, string body, string ok, Action onOk, bool danger = false)
        {
            RectTransform ov;
            var c = Open(root, 520, 250, out ov, 0.7f);
            ov.SetAsLastSibling();
            UI.Txt(c, title, 20, Theme.Text, true).rectTransform.At(36, 32, 450, 30);
            var b = UI.Txt(c, body, 14, Theme.Sub, false, TextAlignmentOptions.TopLeft);
            b.textWrappingMode = TextWrappingModes.Normal;
            b.rectTransform.At(36, 76, 450, 70);
            var cancel = UI.SolidButton(c, "취소", 15, () => UnityEngine.Object.Destroy(ov.gameObject));
            cancel.rectTransform().AtRight(36 + 150 + 10, 176, 130, 46);
            Color col = danger ? Theme.Hex("E5484D") : Theme.Text;
            var okBtn = UI.Button(c, ok, 15, () =>
            {
                UnityEngine.Object.Destroy(ov.gameObject);
                if (onOk != null) onOk();
            }, col, Theme.A(col, 0.85f), Theme.A(col, 0.7f), danger ? Color.white : Theme.Desktop);
            okBtn.rectTransform().AtRight(36, 176, 150, 46);
        }
    }

    /// <summary>여러 개 중 하나를 고르는 버튼 묶음.</summary>
    public class Segmented
    {
        public readonly List<UIButton> buttons = new List<UIButton>();
        public int index = -1;
        readonly Action<int> onChange;

        public Segmented(Transform parent, string[] labels, float x, float y, float w, float h, float size, Action<int> onChange)
        {
            this.onChange = onChange;
            float gap = 6;
            float bw = (w - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                var b = UI.SolidButton(parent, labels[i], size, () => Select(k, true));
                b.rectTransform().At(x + (bw + gap) * i, y, bw, h);
                buttons.Add(b);
            }
        }

        public void Select(int i, bool notify)
        {
            index = i;
            for (int k = 0; k < buttons.Count; k++)
            {
                bool on = k == i;
                buttons[k].SetColors(on ? Theme.A(Theme.Accent, 0.16f) : Theme.PanelHi, on ? Theme.A(Theme.Accent, 0.22f) : Theme.PanelHi2,
                    Theme.Line, on ? Theme.Accent : Theme.Sub);
            }
            if (notify && onChange != null) onChange(i);
        }
    }

    /// <summary>설정 창 (타이틀과 게임 공용).</summary>
    public static class SettingsPanel
    {
        static readonly float[] Volumes = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        public static void Open(Transform root, bool inGame, Action onClose)
        {
            RectTransform ov;
            var c = Modal.Open(root, 660, inGame ? 520 : 580, out ov);
            ov.SetAsLastSibling();
            Action close = () =>
            {
                Settings.Save();
                UnityEngine.Object.Destroy(ov.gameObject);
                if (onClose != null) onClose();
            };

            UI.Txt(c, "설정", 22, Theme.Text, true).rectTransform.At(40, 30, 300, 32);
            UI.Txt(c, "변경 사항은 바로 저장됩니다", 12, Theme.Dim).rectTransform.At(40, 64, 300, 18);
            var x = UI.GhostButton(c, "×", 22, close, false);
            x.rectTransform().AtRight(20, 20, 44, 44);
            UI.HLine(c, 98, 40, 40);

            float cx = 230, cw = 390, y = 118;

            Row(c, "효과음", "버튼·체결·뉴스 알림음", y);
            int vi = 0;
            for (int i = 0; i < Volumes.Length; i++) if (Mathf.Abs(Volumes[i] - Settings.Volume) < 0.13f) vi = i;
            var vol = new Segmented(c, new[] { "끔", "25", "50", "75", "100" }, cx, y, cw, 38, 13, i =>
            {
                Settings.Volume = Volumes[i];
                Sfx.I.Click();
            });
            vol.Select(vi, false);
            y += 64;

            Row(c, "상승 / 하락 색", "국내식: 상승 빨강 · 하락 파랑", y);
            var col = new Segmented(c, new[] { "국내식  ▲빨강 ▼파랑", "해외식  ▲초록 ▼빨강" }, cx, y, cw, 38, 13, i => Settings.GreenUp = i == 1);
            col.Select(Settings.GreenUp ? 1 : 0, false);
            y += 64;

            Row(c, "기본 배속", "게임을 시작할 때의 속도", y);
            int si = Mathf.Clamp(Settings.DefaultSpeed, 1, 3) - 1;
            var sp = new Segmented(c, new[] { "1x", "2x", "5x" }, cx, y, cw, 38, 13, i => Settings.DefaultSpeed = i + 1);
            sp.Select(si, false);
            y += 64;

            Row(c, "뉴스 알림", "오른쪽 아래 팝업 알림", y);
            var al = new Segmented(c, new[] { "모든 속보", "보유 종목만", "끄기" }, cx, y, cw, 38, 13, i => Settings.NewsAlerts = i);
            al.Select(Mathf.Clamp(Settings.NewsAlerts, 0, 2), false);
            y += 64;

            Row(c, "자동 저장", "매일 자정에 저장", y);
            var au = new Segmented(c, new[] { "켜기", "끄기" }, cx, y, cw, 38, 13, i => Settings.AutoSave = i == 0);
            au.Select(Settings.AutoSave ? 0 : 1, false);
            y += 64;

            if (Application.platform != RuntimePlatform.WebGLPlayer || !inGame)
            {
                Row(c, "화면", "창 모드 / 전체 화면", y);
                var fs = new Segmented(c, new[] { "창 모드", "전체 화면" }, cx, y, cw, 38, 13, i => Settings.Fullscreen = i == 1);
                fs.Select(Screen.fullScreen ? 1 : 0, false);
                y += 64;
            }

            if (!inGame)
            {
                UI.HLine(c, y + 2, 40, 40);
                var del = UI.Button(c, "저장 데이터 삭제", 13, () =>
                    Modal.Confirm(root, "저장 데이터를 삭제할까요?", "이어하기 기록과 최고 기록이 모두 지워지며 되돌릴 수 없어요.", "삭제", () =>
                    {
                        SaveSystem.Delete();
                        PlayerPrefs.DeleteKey("bod_best_equity");
                        PlayerPrefs.DeleteKey("bod_best_day");
                        PlayerPrefs.Save();
                        close();
                    }, true),
                    Theme.A(Theme.Hex("E5484D"), 0f), Theme.A(Theme.Hex("E5484D"), 0.12f), Theme.A(Theme.Hex("E5484D"), 0.2f), Theme.Hex("E5484D"), false);
                del.rectTransform().At(32, y + 18, 150, 40);
            }

            var done = UI.Button(c, "닫기", 15, close, Theme.Text, Color.white, Theme.Sub, Theme.Desktop);
            done.rectTransform().AtRight(40, y + 14, 140, 46);
            ((RectTransform)c.parent).sizeDelta = new Vector2(660, y + 84);
        }

        static void Row(Transform c, string title, string sub, float y)
        {
            UI.Txt(c, title, 15, Theme.Text, true).rectTransform.At(40, y, 180, 20);
            UI.Txt(c, sub, 11, Theme.Dim).rectTransform.At(40, y + 21, 190, 16);
        }
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace BuyOrDie
{
    /// <summary>색상 / 폰트. 국내 증시 관례대로 상승 = 빨강, 하락 = 파랑.</summary>
    public static class Theme
    {
        public static readonly Color Desktop = Hex("080B10");
        public static readonly Color Window = Hex("0E1218");
        public static readonly Color Panel = Hex("121720");
        public static readonly Color PanelHi = Hex("18202B");
        public static readonly Color PanelHi2 = Hex("1F2836");
        public static readonly Color Line = Hex("222A36");
        public static readonly Color LineSoft = Hex("19202A");
        public static readonly Color Text = Hex("E9EDF3");
        public static readonly Color Sub = Hex("8C96A8");
        public static readonly Color Dim = Hex("566073");
        static readonly Color RedC = Hex("FF4757"), BlueC = Hex("3B82F6"), GreenC = Hex("22C55E");
        /// <summary>상승색. 설정에서 '해외식(초록 상승)'을 고르면 초록, 기본은 국내식 빨강.</summary>
        public static Color Up { get { return Settings.GreenUp ? GreenC : RedC; } }
        public static Color Down { get { return Settings.GreenUp ? RedC : BlueC; } }
        public static readonly Color Accent = Hex("F5B83D");
        public static readonly Color Liq = Hex("B48CFF");
        public static readonly Color Live = Hex("2FD38A");

        public static Color Hex(string h)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + h, out c);
            return c;
        }

        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Change(double v) { return v > 1e-9 ? Up : (v < -1e-9 ? Down : Sub); }
        public static string HexOf(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        static TMP_FontAsset regular, bold;
        public static TMP_FontAsset Regular { get { Load(); return regular; } }
        public static TMP_FontAsset Bold { get { Load(); return bold; } }

        static void Load()
        {
            if (regular != null) return;
            regular = Make("Fonts/NanumGothic");
            bold = Make("Fonts/NanumGothicBold");
            if (bold == null) bold = regular;
        }

        static TMP_FontAsset Make(string path)
        {
            var f = Resources.Load<Font>(path);
            if (f == null) return TMP_Settings.defaultFontAsset;
            var fa = TMP_FontAsset.CreateFontAsset(f, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = f.name + " SDF";
            return fa;
        }
    }

    /// <summary>숫자 / 시간 포맷.</summary>
    public static class Fmt
    {
        public static string Price(double p)
        {
            if (p >= 100) return p.ToString("#,0");
            if (p >= 1) return p.ToString("#,0.0#");
            return p.ToString("0.####");
        }

        public static string Won(double v) { return Math.Round(v).ToString("#,0"); }
        public static string SignedWon(double v) { double r = Math.Round(v); return (r > 0 ? "+" : "") + r.ToString("#,0"); }
        public static string Pct(double v) { return (v > 0.004 ? "+" : "") + v.ToString("0.00") + "%"; }

        public static string Short(double v)
        {
            double a = Math.Abs(v);
            string s = v < 0 ? "-" : "";
            if (a >= 1e8) return s + (a / 1e8).ToString(a >= 1e10 ? "#,0" : "#,0.00") + "억";
            if (a >= 1e4) return s + (a / 1e4).ToString("#,0") + "만";
            return s + a.ToString("#,0");
        }

        public static string Qty(double q, int dec)
        {
            if (dec <= 0) return q.ToString("#,0");
            return q.ToString("#,0." + new string('#', dec));
        }

        public static string Volume(double v)
        {
            if (v >= 1e9) return (v / 1e9).ToString("0.0") + "B";
            if (v >= 1e6) return (v / 1e6).ToString("0.0") + "M";
            if (v >= 1e3) return (v / 1e3).ToString("0.0") + "K";
            return v.ToString("0");
        }

        public static string Clock(int minute)
        {
            int m = ((minute % 1440) + 1440) % 1440;
            return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
        }

        public static string Colored(string s, Color c) { return "<color=" + Theme.HexOf(c) + ">" + s + "</color>"; }

        /// <summary>"1/4(월)"</summary>
        public static string Date(int minute)
        {
            var d = MarketEngine.Epoch.AddMinutes(minute);
            return d.Month + "/" + d.Day + "(" + MarketEngine.DayName(d.DayOfWeek) + ")";
        }

        /// <summary>"2027.01.04 (월)"</summary>
        public static string FullDate(int minute)
        {
            var d = MarketEngine.Epoch.AddMinutes(minute);
            return d.ToString("yyyy.MM.dd") + " (" + MarketEngine.DayName(d.DayOfWeek) + ")";
        }

        /// <summary>"2시간 10분", "1일 3시간"</summary>
        public static string Duration(int mins)
        {
            if (mins < 60) return mins + "분";
            if (mins < 1440) return (mins / 60) + "시간" + (mins % 60 > 0 ? " " + (mins % 60) + "분" : "");
            return (mins / 1440) + "일 " + ((mins % 1440) / 60) + "시간";
        }
    }

    /// <summary>코드로 UI를 조립하는 작은 헬퍼 모음.</summary>
    public static class UI
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Img(string name, Transform parent, Color c, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, bool bold = false,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? Theme.Bold : Theme.Regular;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = true;
            t.text = text;
            return t;
        }

        // ───── 앵커 헬퍼 (값은 픽셀, 기준 해상도 1920x1080) ─────

        public static RectTransform Fill(this RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Top(this RectTransform rt, float top, float height, float l = 0, float r = 0)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(l, -top - height); rt.offsetMax = new Vector2(-r, -top);
            return rt;
        }

        public static RectTransform Bottom(this RectTransform rt, float bottom, float height, float l = 0, float r = 0)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(l, bottom); rt.offsetMax = new Vector2(-r, bottom + height);
            return rt;
        }

        public static RectTransform Left(this RectTransform rt, float left, float width, float t = 0, float b = 0)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(left, b); rt.offsetMax = new Vector2(left + width, -t);
            return rt;
        }

        public static RectTransform Right(this RectTransform rt, float right, float width, float t = 0, float b = 0)
        {
            rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f);
            rt.offsetMin = new Vector2(-right - width, b); rt.offsetMax = new Vector2(-right, -t);
            return rt;
        }

        /// <summary>좌상단 기준 절대 배치.</summary>
        public static RectTransform At(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>우상단 기준 절대 배치 (x = 오른쪽 여백).</summary>
        public static RectTransform AtRight(this RectTransform rt, float rightX, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-rightX, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Center(this RectTransform rt, float w, float h, float x = 0, float y = 0)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image HLine(Transform parent, float y, float l = 0, float r = 0, Color? c = null)
        {
            var img = Img("Line", parent, c ?? Theme.Line);
            img.rectTransform.Top(y, 1, l, r);
            return img;
        }

        public static Image VLine(Transform parent, float x, float t = 0, float b = 0, Color? c = null)
        {
            var img = Img("Line", parent, c ?? Theme.Line);
            img.rectTransform.Left(x, 1, t, b);
            return img;
        }

        public static UIButton Button(Transform parent, string label, float size, Action onClick,
            Color normal, Color hover, Color press, Color text, bool bold = true)
        {
            var img = Img("Btn_" + label, parent, normal, true);
            var b = img.gameObject.AddComponent<UIButton>();
            b.bg = img;
            b.label = Txt(img.transform, label, size, text, bold, TextAlignmentOptions.Center, "Label");
            b.label.rectTransform.Fill();
            b.normal = normal; b.hover = hover; b.press = press; b.textColor = text;
            b.onClick = onClick;
            b.Refresh();
            return b;
        }

        public static UIButton GhostButton(Transform parent, string label, float size, Action onClick, bool bold = false)
        {
            return Button(parent, label, size, onClick, Theme.A(Theme.PanelHi, 0f), Theme.PanelHi, Theme.PanelHi2, Theme.Sub, bold);
        }

        public static UIButton SolidButton(Transform parent, string label, float size, Action onClick)
        {
            return Button(parent, label, size, onClick, Theme.PanelHi, Theme.PanelHi2, Theme.Line, Theme.Text, true);
        }
    }

    /// <summary>호버/클릭 색 변화를 직접 제어하는 가벼운 버튼.</summary>
    public class UIButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Image bg;
        public TextMeshProUGUI label;
        public Color normal, hover, press, textColor;
        public Color disabledText = Theme.Dim;
        public Color disabledBg = Color.clear;
        public bool useDisabledBg;
        public Action onClick;
        public Action<bool> onHover;
        public bool interactable = true;
        bool over, down;

        public void SetColors(Color n, Color h, Color p, Color text)
        {
            normal = n; hover = h; press = p; textColor = text;
            Refresh();
        }

        public void SetInteractable(bool v)
        {
            if (interactable == v) return;
            interactable = v;
            Refresh();
        }

        public void SetLabel(string s) { if (label != null && label.text != s) label.text = s; }

        public void Refresh()
        {
            if (bg != null)
            {
                if (!interactable) bg.color = useDisabledBg ? disabledBg : normal;
                else bg.color = down ? press : (over ? hover : normal);
            }
            if (label != null) label.color = interactable ? textColor : disabledText;
        }

        public void OnPointerEnter(PointerEventData e) { over = true; Refresh(); if (onHover != null) onHover(true); }
        public void OnPointerExit(PointerEventData e) { over = false; down = false; Refresh(); if (onHover != null) onHover(false); }
        public void OnPointerDown(PointerEventData e) { down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (!interactable || e.button != PointerEventData.InputButton.Left) return;
            Sfx.I.Click();
            if (onClick != null) onClick();
        }

        void OnDisable() { over = down = false; }
    }

    /// <summary>밑줄형 탭 바.</summary>
    public class TabBar
    {
        public readonly List<UIButton> buttons = new List<UIButton>();
        readonly List<Image> bars = new List<Image>();
        public int index = -1;
        public Action<int> onChange;
        public Color activeColor = Theme.Text;

        public TabBar(Transform parent, string[] labels, float x, float y, float w, float h, float size, Action<int> onChange)
        {
            this.onChange = onChange;
            float bw = w / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                var b = UI.GhostButton(parent, labels[i], size, () => Select(k, true), true);
                b.rectTransform().At(x + bw * i, y, bw, h);
                var bar = UI.Img("Underline", b.transform, Theme.Text);
                bar.rectTransform.Bottom(0, 2, 10, 10);
                buttons.Add(b);
                bars.Add(bar);
            }
            Select(0, false);
        }

        public void Select(int i, bool notify)
        {
            index = i;
            for (int k = 0; k < buttons.Count; k++)
            {
                bool on = k == i;
                buttons[k].textColor = on ? activeColor : Theme.Dim;
                buttons[k].Refresh();
                bars[k].enabled = on;
                bars[k].color = activeColor;
            }
            if (notify && onChange != null) onChange(i);
        }

        public void SetLabel(int i, string s) { buttons[i].SetLabel(s); }
    }

    public static class ComponentExt
    {
        public static RectTransform rectTransform(this Component c) { return (RectTransform)c.transform; }
    }

    // ───────────────────────── 메시 드로잉 ─────────────────────────

    public static class UIDraw
    {
        public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, col, Vector2.zero);
            vh.AddVert(b, col, Vector2.zero);
            vh.AddVert(c, col, Vector2.zero);
            vh.AddVert(d, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public static void Box(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 col)
        {
            Quad(vh, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0), col);
        }

        public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color32 col)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            Quad(vh, a - n, a + n, b + n, b - n, col);
        }

        public static void DashedH(VertexHelper vh, float x0, float x1, float y, float dash, float gap, float w, Color32 col)
        {
            for (float x = x0; x < x1; x += dash + gap)
                Box(vh, x, y - w * 0.5f, Mathf.Min(x + dash, x1), y + w * 0.5f, col);
        }
    }

    /// <summary>관심종목 옆 작은 추세선.</summary>
    public class Sparkline : MaskableGraphic
    {
        public readonly List<double> values = new List<double>();
        public float thickness = 1.6f;
        public Color lineColor = Theme.Sub;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = values.Count;
            if (n < 2) return;
            Rect r = rectTransform.rect;
            double mn = double.MaxValue, mx = double.MinValue;
            for (int i = 0; i < n; i++) { if (values[i] < mn) mn = values[i]; if (values[i] > mx) mx = values[i]; }
            if (mx - mn < mx * 1e-6) { mx += mx * 0.001 + 1e-9; mn -= mn * 0.001 + 1e-9; }
            Color32 c = lineColor;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                float x = r.xMin + r.width * i / (n - 1);
                float y = r.yMin + 2 + (float)((values[i] - mn) / (mx - mn)) * (r.height - 4);
                var p = new Vector2(x, y);
                if (i > 0) UIDraw.Line(vh, prev, p, thickness, c);
                prev = p;
            }
        }
    }
}

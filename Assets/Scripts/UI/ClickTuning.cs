using UnityEngine;
using UnityEngine.EventSystems;

namespace BuyOrDie
{
    /// <summary>
    /// 스크롤 목록 안의 버튼(종목 줄 등)은 누르는 동안 마우스가 조금만 움직여도
    /// 유니티가 "스크롤 드래그"로 판정해서 클릭을 버린다. 기본 기준(10px)은 고해상도 화면·
    /// 터치패드에서 너무 작으므로, 화면 높이에 비례해 기준을 늘려 클릭이 사라지지 않게 한다.
    /// </summary>
    public static class ClickTuning
    {
        public static void Apply()
        {
            var es = EventSystem.current;
            if (es == null) return;
            int want = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 1080f * 18f));
            if (es.pixelDragThreshold != want) es.pixelDragThreshold = want;
        }
    }
}

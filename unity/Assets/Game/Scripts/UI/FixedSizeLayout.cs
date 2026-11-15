using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 너비를 정한 자식이 있는 줄·묶음 (Ui.Size width): 레이아웃의 "남는 너비 똑같이 나누기"(childForceExpandWidth)를 끄고,
    /// 너비를 정하지 않은 자식(flexibleWidth 미설정)은 flexibleWidth 1로 채워 예전처럼 늘어나게 한다.
    /// 너비를 정한 자식(flexibleWidth 0)은 정한 너비 그대로 (동그라미가 타원이 되지 않게, 2026-11-15 캡처 확인).
    /// 자식이 나중에 붙어도 OnTransformChildrenChanged로 다시 맞춘다.
    /// </summary>
    public sealed class FixedSizeLayout : MonoBehaviour
    {
        public static void Attach(Transform group)
        {
            if (group == null) return;
            var g = group.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (g == null) return;
            g.childForceExpandWidth = false;
            var f = group.GetComponent<FixedSizeLayout>() ?? group.gameObject.AddComponent<FixedSizeLayout>();
            f.Apply();
        }

        private void OnTransformChildrenChanged() => Apply();

        public void Apply()
        {
            foreach (Transform child in transform)
            {
                var le = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                if (le.ignoreLayout) continue;
                if (le.flexibleWidth < 0f) le.flexibleWidth = 1f; // 정하지 않은 것은 예전처럼 채운다
            }
        }
    }
}

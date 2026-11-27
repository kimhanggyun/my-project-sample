using Game.Battle.Model.Crowd;
using Game.Foundation.Math;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 진형 아이콘 (R-141, 2026-11-22 사용자: 이름 대신 모양으로): 규칙이 만드는 실제 대열 자리를 점으로 찍는다 (앞 = 위).
    /// 그림 파일 없이 코드로 — 진형 모양을 바꾸면 아이콘도 따라 바뀐다.
    /// </summary>
    public static class FormationIcon
    {
        private const int Dots = 28;

        /// <param name="size">아이콘 한 변 (픽셀, 가운데에 놓임).</param>
        public static RectTransform Add(Transform parent, string formation, Color color, float size = 60f, float dot = 6f)
        {
            var root = Ui.Rect(parent, "FormationIcon");
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(size, size);
            var slots = CrowdFormations.Slots(formation, Dots, Fix.OneValue, 12, CrowdFormations.DefaultRows); // 아이콘은 작은 길이로 (모양이 보이게, R-157)
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in slots)
            {
                float x = (float)s.X.ToDisplayDouble(), y = (float)s.Y.ToDisplayDouble();
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float span = Mathf.Max(1f, Mathf.Max(maxX - minX, maxY - minY));
            float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
            // 줄 사이가 너무 붙으면 1줄 · 2줄 · 두툼이 같아 보임 (캡처 39b) → 앞뒤(위아래)는 줄 사이 최소 dot × 1.6 픽셀
            float unitX = size * 0.9f / span;
            float unitY = Mathf.Min(Mathf.Max(unitX, dot * 1.6f), size * 0.9f / Mathf.Max(0.01f, maxY - minY));
            foreach (var s in slots)
            {
                float x = (float)s.X.ToDisplayDouble(), y = (float)s.Y.ToDisplayDouble();
                var d = Ui.Rect(root, "Dot");
                d.anchorMin = d.anchorMax = new Vector2(0.5f + (x - cx) * unitX / size, 0.5f + (y - cy) * unitY / size);
                d.pivot = new Vector2(0.5f, 0.5f);
                d.sizeDelta = new Vector2(dot, dot);
                var img = d.gameObject.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            return root;
        }
    }
}

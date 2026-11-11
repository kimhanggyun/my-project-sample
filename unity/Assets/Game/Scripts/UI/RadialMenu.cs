using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 원형 메뉴 (battle.md 4.3.3): 가운데 버튼(닫기·뒤로) 둘레로 둥근 버튼을 펼친다.
    /// 놓일 자리는 부모 영역 안으로 당겨 잘리지 않게 하고, 열 때 가운데에서 바깥으로 퍼지는 움직임을 준다.
    /// 어떤 항목을 둘지·눌렀을 때 할 일은 쓰는 쪽이 정한다 (메뉴는 규칙을 모른다).
    /// </summary>
    public sealed class RadialMenu
    {
        public sealed class Item
        {
            public string Label;
            public bool Enabled = true;
            /// <summary>지금 상태인 항목 (예: 지금 진형) — 강조 색.</summary>
            public bool Active;
            public Action OnPick;
        }

        private const float ItemSize = 128f;
        private const float CenterSize = 104f;
        private const float OpenSeconds = 0.14f;

        private readonly RectTransform _area;
        private readonly RectTransform _root;
        private readonly List<(RectTransform rt, Vector2 target)> _items = new List<(RectTransform, Vector2)>();
        private Button _center;
        private float _radius;
        private float _openTime;

        public bool IsOpen => _root.gameObject.activeSelf;

        /// <param name="area">메뉴가 놓일 영역 (그 안의 좌표로 따라다닌다).</param>
        public RadialMenu(RectTransform area)
        {
            _area = area;
            _root = Ui.Rect(area, "RadialMenu");
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = Vector2.zero;
            _root.gameObject.SetActive(false);
        }

        /// <summary>항목을 새로 펼친다. 항목 수에 따라 반지름이 커진다 (버튼이 겹치지 않게).</summary>
        public void Open(string centerLabel, Action onCenter, IList<Item> items)
        {
            Clear();
            int n = Mathf.Max(1, items.Count);
            // 둘레에 버튼 지름 + 틈이 들어가는 반지름, 최소 170
            _radius = Mathf.Max(170f, (ItemSize + 18f) * n / (2f * Mathf.PI));
            // 고리 뒤 옅은 판: 전장 위에서도 글자가 읽히게
            var ring = Ui.Rect(_root, "Ring");
            var ringImg = Ui.Background(ring, new Color(0.08f, 0.07f, 0.06f, 0.45f));
            ringImg.sprite = UiShapes.Circle;
            ringImg.raycastTarget = false;
            ring.sizeDelta = Vector2.one * (_radius * 2f + ItemSize);
            _items.Add((ring, Vector2.zero));

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                // 12시 방향부터 시계 방향
                float a = Mathf.PI / 2f - 2f * Mathf.PI * i / n;
                var target = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _radius;
                var b = Round(it.Label, it.OnPick, ItemSize, it.Active ? Palette.ButtonActive : Palette.Button, 26);
                if (!it.Enabled) Ui.SetEnabled(b, false);
                _items.Add(((RectTransform)b.transform, target));
            }
            _center = Round(centerLabel, onCenter, CenterSize, Palette.Card, 24);
            _items.Add(((RectTransform)_center.transform, Vector2.zero));
            _root.gameObject.SetActive(true);
            _openTime = 0f;
            Animate(0f);
        }

        public void Close()
        {
            Clear();
            _root.gameObject.SetActive(false);
        }

        /// <summary>메뉴 가운데를 영역 안의 한 점(영역 가운데 기준 로컬 좌표)에 둔다. 고리가 영역 밖으로 나가지 않게 당긴다.</summary>
        public void Follow(Vector2 local)
        {
            var r = _area.rect;
            float m = _radius + ItemSize / 2f;
            float x = r.width > 2 * m ? Mathf.Clamp(local.x, r.xMin + m, r.xMax - m) : r.center.x;
            float y = r.height > 2 * m ? Mathf.Clamp(local.y, r.yMin + m, r.yMax - m) : r.center.y;
            _root.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>매 프레임 (실제 시간): 펼치는 움직임.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsOpen || _openTime >= OpenSeconds) return;
            _openTime += deltaTime;
            Animate(Mathf.Clamp01(_openTime / OpenSeconds));
        }

        private void Animate(float t)
        {
            float e = 1f - (1f - t) * (1f - t); // 감속
            foreach (var (rt, target) in _items)
            {
                rt.anchoredPosition = target * e;
                rt.localScale = Vector3.one * (0.6f + 0.4f * e);
            }
        }

        private Button Round(string label, Action onClick, float size, Color color, int fontSize)
        {
            var b = Ui.Button(_root, label, onClick, size, color, fontSize);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * size;
            var img = (Image)b.targetGraphic;
            img.sprite = UiShapes.Circle;
            img.type = Image.Type.Simple;
            var text = b.GetComponentInChildren<Text>();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.rectTransform.offsetMin = new Vector2(10, 6);
            text.rectTransform.offsetMax = new Vector2(-10, -6);
            return b;
        }

        private void Clear()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_root.GetChild(i).gameObject);
            _items.Clear();
        }
    }
}

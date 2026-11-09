using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Unity.UI
{
    /// <summary>
    /// 전장 영역의 손가락 조작 구분 (battle.md 4.1): 짧게 떼면 누름, 한 손가락 끌기 = 이동, 두 손가락 = 확대·축소, 마우스 휠 = 확대·축소.
    /// 끌기 판정 거리는 EventSystem의 drag threshold. 좌표는 화면 픽셀.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GestureArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IScrollHandler
    {
        /// <summary>누른 화면 위치.</summary>
        public event Action<Vector2> Tapped;
        /// <summary>한 손가락 끌기: (이전 위치, 지금 위치).</summary>
        public event Action<Vector2, Vector2> Dragged;
        /// <summary>확대 비율 (1보다 크면 가까이).</summary>
        public event Action<float> Zoomed;

        private const float WheelStep = 0.1f;
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private bool _multiTouched;

        public void OnPointerDown(PointerEventData e)
        {
            _pointers[e.pointerId] = e.position;
            if (_pointers.Count > 1) _multiTouched = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _pointers.Remove(e.pointerId);
            if (_pointers.Count == 0) _multiTouched = false;
        }

        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (!_pointers.ContainsKey(e.pointerId)) _pointers[e.pointerId] = e.position - e.delta;
            if (_pointers.Count >= 2)
            {
                float before = Spread();
                _pointers[e.pointerId] = e.position;
                float after = Spread();
                if (before > 1f && after > 1f) Zoomed?.Invoke(after / before);
                return;
            }
            var prev = _pointers[e.pointerId];
            _pointers[e.pointerId] = e.position;
            if (!_multiTouched) Dragged?.Invoke(prev, e.position);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging || _multiTouched) return;
            Tapped?.Invoke(e.position);
        }

        public void OnScroll(PointerEventData e)
        {
            if (Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
            Zoomed?.Invoke(e.scrollDelta.y > 0 ? 1f + WheelStep : 1f / (1f + WheelStep));
        }

        /// <summary>앞의 두 손가락 사이 거리.</summary>
        private float Spread()
        {
            Vector2 a = default, b = default;
            int n = 0;
            foreach (var p in _pointers.Values)
            {
                if (n == 0) a = p; else if (n == 1) b = p; else break;
                n++;
            }
            return n < 2 ? 0f : Vector2.Distance(a, b);
        }
    }
}

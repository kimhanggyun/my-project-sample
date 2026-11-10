using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 끌어서 놓기 (편성 드래그 탭): 누른 채 끌면 반투명 복사본이 손가락을 따라오고, 놓으면 Dropped(값, 화면 위치)를 부른다.
    /// 놓을 곳 판정은 받는 쪽이 화면 위치로 한다 (<see cref="DropTargets"/>). 화면 픽셀 좌표 (Screen Space Overlay 캔버스).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string Payload;
        /// <summary>복사본을 올릴 곳 (화면 루트).</summary>
        public Transform GhostParent;
        public event Action<string, Vector2> Dropped;

        private RectTransform _ghost;

        public void OnBeginDrag(PointerEventData e)
        {
            var src = (RectTransform)transform;
            _ghost = Ui.Rect(GhostParent != null ? GhostParent : transform.root, "DragGhost");
            _ghost.sizeDelta = src.rect.size;
            Ui.Background(_ghost, new Color(0.55f, 0.42f, 0.20f, 0.85f)).raycastTarget = false;
            var text = src.GetComponentInChildren<Text>();
            if (text != null)
            {
                var label = Ui.Label(_ghost, text.text, text.fontSize, TextAnchor.MiddleCenter);
                label.raycastTarget = false;
                Ui.Fill(label.rectTransform);
            }
            _ghost.SetAsLastSibling();
            _ghost.position = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_ghost != null) _ghost.position = e.position;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            Dropped?.Invoke(Payload, e.position);
        }
    }

    /// <summary>놓을 곳 목록: 화면 위치가 어느 영역 안인지 (나중에 넣은 것 먼저).</summary>
    public sealed class DropTargets<T>
    {
        private readonly System.Collections.Generic.List<(RectTransform rect, T value)> _list = new System.Collections.Generic.List<(RectTransform, T)>();

        public void Add(RectTransform rect, T value) => _list.Add((rect, value));

        public bool Find(Vector2 screen, out T value)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
                if (_list[i].rect != null && RectTransformUtility.RectangleContainsScreenPoint(_list[i].rect, screen, null))
                {
                    value = _list[i].value;
                    return true;
                }
            value = default;
            return false;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Unity.UI
{
    /// <summary>
    /// 이 RectTransform 안을 눌렀을 때 정규화 좌표(왼쪽 아래 0,0 ~ 오른쪽 위 1,1)를 알린다. 끌기(드래그)로 끝난 누름은 무시한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PointerTap : MonoBehaviour, IPointerClickHandler
    {
        public event Action<Vector2> Tapped;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging) return;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out var local)) return;
            var r = rt.rect;
            if (r.width <= 0 || r.height <= 0) return;
            Tapped?.Invoke(new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height));
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 길게 누르면 되풀이 (수량 조절 [−][+], 공통부품.md 수량 조절): 0.45초 누르고 있으면 그때부터 0.12초마다 onRepeat.
    /// 짧게 누르면 버튼의 원래 onClick만.
    /// </summary>
    public sealed class RepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private const float Delay = 0.45f, Every = 0.12f;
        private Action _onRepeat;
        private float _held = -1f, _next;
        private bool _repeated;
        private Button _button;

        public static RepeatButton Add(Button b, Action onRepeat)
        {
            var r = b.gameObject.AddComponent<RepeatButton>();
            r._onRepeat = onRepeat;
            r._button = b;
            return r;
        }

        public void OnPointerDown(PointerEventData e) { _held = 0f; _next = Delay; _repeated = false; }
        public void OnPointerUp(PointerEventData e) => Stop();
        public void OnPointerExit(PointerEventData e) => Stop();

        private void Stop()
        {
            // 되풀이가 있었으면 손을 뗄 때의 한 번 누름(onClick)은 막는다
            if (_repeated && _button != null) { _button.interactable = false; _button.interactable = true; }
            _held = -1f;
        }

        private void Update()
        {
            if (_held < 0f || _onRepeat == null) return;
            _held += Time.unscaledDeltaTime;
            if (_held < _next) return;
            _next = _held + Every;
            _repeated = true;
            _onRepeat();
        }
    }
}

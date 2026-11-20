using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 공통 로딩 표시 (backlog H5, 2026-11-20 사용자: API 통신 때 답답함 — 예: 지도 깃발을 누르고 토스트가 뜰 때까지).
    /// 서버에 묻고 답을 기다리는 동안 화면 전체를 살짝 어둡게 하고 가운데 도는 고리를 보인다. 그동안 다른 누름은 막는다 (두 번 누름 방지).
    /// - 0.25초 안에 끝나면 아무것도 안 보인다 (깜빡임 없음).
    /// - 여러 개가 겹치면 모두 끝날 때까지 하나로 보인다.
    /// - 끝 알림이 안 와도 35초 뒤 저절로 끝난다 (통신 대기 30초 + 여유) — 화면이 영영 막히지 않게.
    ///
    /// 쓰는 법 (콜백 하나 감싸기):
    /// <code>ctx.CollectFlag(id, Loading.Wrap&lt;MailListResponse&gt;(r =&gt; { ... }));</code>
    /// 콜백이 여럿이거나 직접 끝내야 하면 <c>var end = Loading.Begin(); ... end();</c> (여러 번 불러도 한 번만 끝).
    /// 사용자가 누른 일에만 쓴다 — 뒤에서 도는 동기화 · 이름 받기 · 상태 확인에는 쓰지 않는다.
    /// </summary>
    public static class Loading
    {
        public const float ShowAfter = 0.25f;
        public const float GiveUpAfter = 35f;

        private static LoadingView _view;

        /// <summary>캔버스 맨 위에 한 번 붙인다 (GameApp). 화면이 바뀌어도 남는다.</summary>
        public static void Attach(Transform canvas)
        {
            if (_view != null) return;
            var rt = Ui.Panel(canvas, "Loading", new Color(0f, 0f, 0f, 0.28f));
            rt.SetAsLastSibling();
            _view = rt.gameObject.AddComponent<LoadingView>();
            _view.Build();
        }

        /// <summary>시작. 돌려준 동작을 부르면 끝 (여러 번 불러도 한 번).</summary>
        public static Action Begin()
        {
            if (_view == null) return () => { };
            int id = _view.Add();
            bool ended = false;
            return () => { if (ended) return; ended = true; if (_view != null) _view.Remove(id); };
        }

        /// <summary>콜백을 감싼다: 콜백이 불리면 먼저 로딩을 끝내고 원래 콜백을 부른다.</summary>
        public static Action<T> Wrap<T>(Action<T> done)
        {
            var end = Begin();
            return r => { end(); done?.Invoke(r); };
        }

        /// <summary>인자 없는 콜백용.</summary>
        public static Action Wrap(Action done)
        {
            var end = Begin();
            return () => { end(); done?.Invoke(); };
        }

        /// <summary>지금 보이는 중인지 (캡처 · 시험용).</summary>
        public static bool Visible => _view != null && _view.Shown;
    }

    /// <summary>로딩 겹침 화면: 어두운 바탕(누름 막기) + 가운데 카드의 도는 고리.</summary>
    public sealed class LoadingView : MonoBehaviour
    {
        private readonly System.Collections.Generic.Dictionary<int, float> _started = new System.Collections.Generic.Dictionary<int, float>();
        private int _next;
        private RectTransform _ring;
        private Image _dim;
        public bool Shown { get; private set; }
        /// <summary>카드 색 = 밝은 한지 (2026-11-20 캡처: Palette.Card는 어두워 다른 화면과 안 맞음).</summary>
        private static readonly Color Paper = new Color(0.96f, 0.93f, 0.86f);

        public void Build()
        {
            _dim = GetComponent<Image>();
            _dim.raycastTarget = true; // 보이는 동안 뒤 화면 누름 막기
            var card = Ui.Rect(transform, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(180f, 180f);
            var bg = Ui.Background(card, Paper);
            bg.sprite = UiShapes.Rounded(24);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            // 도는 고리: 둥근 판을 3/4만 채우고(방사형) 안쪽을 카드 색으로 덮어 고리처럼
            _ring = Ui.Rect(card, "Ring");
            _ring.anchorMin = _ring.anchorMax = _ring.pivot = new Vector2(0.5f, 0.5f);
            _ring.sizeDelta = new Vector2(96f, 96f);
            var arc = Ui.Background(_ring, Palette.Seal);
            arc.sprite = UiShapes.Circle;
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillAmount = 0.75f;
            arc.raycastTarget = false;
            var hole = Ui.Rect(_ring, "Hole");
            hole.anchorMin = hole.anchorMax = hole.pivot = new Vector2(0.5f, 0.5f);
            hole.sizeDelta = new Vector2(70f, 70f);
            var hb = Ui.Background(hole, Paper);
            hb.sprite = UiShapes.Circle;
            hb.raycastTarget = false;
            Show(false);
        }

        public int Add()
        {
            int id = ++_next;
            _started[id] = Time.unscaledTime;
            return id;
        }

        public void Remove(int id) => _started.Remove(id);

        private void Show(bool on)
        {
            Shown = on;
            _dim.enabled = on;
            for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(on);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float oldest = float.MaxValue;
            var expired = new System.Collections.Generic.List<int>();
            foreach (var kv in _started)
            {
                if (now - kv.Value > Loading.GiveUpAfter) expired.Add(kv.Key);
                else if (kv.Value < oldest) oldest = kv.Value;
            }
            foreach (var id in expired) _started.Remove(id);
            bool want = _started.Count > 0 && now - oldest >= Loading.ShowAfter;
            if (want != Shown) Show(want);
            if (Shown && _ring != null) _ring.Rotate(0f, 0f, -360f * Time.unscaledDeltaTime);
            transform.SetAsLastSibling(); // 확인 창 등 뒤에 붙는 것보다 늘 위
        }
    }
}

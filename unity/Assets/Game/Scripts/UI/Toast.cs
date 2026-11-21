using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 공통 결과 알림 (토스트, 2026-11-20 사용자: 서버와 통신하는 동안은 공통 로딩, 결과는 토스트 — 화면 위에 글줄이 생겨 자리가 튀지 않게).
    /// 어두운 둥근 띠 + 흰 글씨, 화면 아래쪽, 3초 뒤 사라짐. 실패(bad)는 붉은 띠.
    /// 화면 안이 아니라 캔버스 맨 위 층(로딩과 같은 층, 로딩 바로 아래)에 붙어 화면을 다시 그려도 남는다. 연달아 띄우면 앞의 것을 바꾼다 (쌓이지 않음).
    /// 쓰는 법: <code>Toast.Show(ctx.T("ui.shop.exchanged"));</code> · 실패면 <code>Toast.Show(text, bad: true);</code>
    /// </summary>
    public static class Toast
    {
        public const float Seconds = 3f;
        private static readonly Color Dark = new Color(0.16f, 0.13f, 0.1f, 0.88f);
        private static readonly Color Red = new Color(0.55f, 0.13f, 0.1f, 0.92f);

        private static RectTransform _layer;
        private static ToastView _view;

        /// <summary>캔버스에 한 번 붙인다 (GameApp, Loading보다 먼저 → 로딩이 위). 화면이 바뀌어도 남는다.</summary>
        public static void Attach(Transform canvas)
        {
            if (_layer != null) return;
            _layer = Ui.Rect(canvas, "Toasts");
            Ui.Fill(_layer);
            _layer.SetAsLastSibling();
        }

        /// <summary>띄운다 (빈 글이면 아무것도 안 함). 이미 떠 있으면 그 자리의 글을 바꾸고 3초를 다시 센다.</summary>
        public static void Show(string text, bool bad = false)
        {
            if (string.IsNullOrEmpty(text) || _layer == null) return;
            if (_view == null)
            {
                var t = Ui.Rect(_layer, "Toast");
                t.anchorMin = new Vector2(0.08f, 0f);
                t.anchorMax = new Vector2(0.92f, 0f);
                t.pivot = new Vector2(0.5f, 0f);
                t.anchoredPosition = new Vector2(0, 300); // 아래 탭 줄 위
                t.sizeDelta = new Vector2(0, 84);
                var bg = Ui.Background(t, Dark);
                bg.sprite = UiShapes.Rounded(14);
                bg.type = Image.Type.Sliced;
                bg.raycastTarget = false;
                var l = Ui.Label(t, "", 24, TextAnchor.MiddleCenter, Color.white);
                l.raycastTarget = false;
                Ui.Fill(l.rectTransform);
                l.rectTransform.offsetMin = new Vector2(18, 4);
                l.rectTransform.offsetMax = new Vector2(-18, -4);
                _view = t.gameObject.AddComponent<ToastView>();
                _view.Background = bg;
                _view.Text = l;
            }
            _view.Background.color = bad ? Red : Dark;
            _view.Text.text = text;
            _view.Restart(Seconds);
        }

        /// <summary>바로 숨긴다 (아래 탭을 눌러 다른 탭으로 갈 때 — 앞 탭의 안내가 다음 탭에 남지 않게).</summary>
        public static void Hide()
        {
            if (_view != null) _view.gameObject.SetActive(false);
        }

        /// <summary>지금 떠 있는 글 (캡처 · 시험용, 없으면 null).</summary>
        public static string Current => _view != null && _view.gameObject.activeSelf ? _view.Text.text : null;
    }

    /// <summary>토스트 하나: 정한 시간 동안 보이고 끝에 0.3초 흐려지며 숨는다.</summary>
    public sealed class ToastView : MonoBehaviour
    {
        public Image Background;
        public Text Text;
        private float _left;

        public void Restart(float seconds)
        {
            _left = seconds;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            SetAlpha(1f);
        }

        private void Update()
        {
            _left -= Time.unscaledDeltaTime;
            if (_left <= 0f) { gameObject.SetActive(false); return; }
            if (_left < 0.3f) SetAlpha(_left / 0.3f);
        }

        private void SetAlpha(float a)
        {
            var g = GetComponent<CanvasGroup>();
            if (g == null) g = gameObject.AddComponent<CanvasGroup>(); // Unity 객체는 ?? 를 쓰지 않는다
            g.alpha = a;
            g.blocksRaycasts = false;
            g.interactable = false;
        }
    }
}

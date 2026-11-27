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
        private Image _dim;
        public bool Shown { get; private set; }
        /// <summary>카드 색 = 밝은 한지 (2026-11-20 캡처: Palette.Card는 어두워 다른 화면과 안 맞음).</summary>
        private static readonly Color Paper = new Color(0.96f, 0.93f, 0.86f);

        private int _builtVersion = -1;

        public void Build()
        {
            _dim = GetComponent<Image>();
            _dim.raycastTarget = true; // 보이는 동안 뒤 화면 누름 막기
            Show(false);
        }

        /// <summary>
        /// 로딩 카드 (anim.loading, ui-kit.md 4.9): ring (도는 고리) · swordClash (검과 검이 맞부딪쳐 불꽃) · horseRun (말이 달림) · flagWave (깃발 펄럭).
        /// 부품 꾸미기 미리보기도 이것을 그대로 쓴다.
        /// </summary>
        public static RectTransform BuildCard(Transform parent)
        {
            var spec = UiKit.Spec("anim.loading");
            string v = spec.Variant ?? "ring";
            Color back = spec.Col("color", Palette.IsLight ? Paper : Palette.Card);
            Color fx = spec.Col("accent", v == "ring" ? Palette.Seal : Palette.AccentLight);
            var card = Ui.Rect(parent, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(180f, 180f);
            var bg = Ui.Background(card, back);
            bg.sprite = UiShapes.Rounded(24);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            card.gameObject.AddComponent<RectMask2D>();
            var anim = card.gameObject.AddComponent<KitAnim>();
            anim.Speed = spec.Num("speed", 1f);
            RectTransform Pic(string name, string icon, float size, Vector2 pos, Vector2 pivot)
            {
                var r = Ui.Rect(card, name);
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = pivot;
                r.sizeDelta = new Vector2(size, size);
                r.anchoredPosition = pos;
                var im = r.gameObject.AddComponent<Image>();
                im.sprite = SegmentArt.Icon(icon); im.preserveAspect = true; im.color = fx; im.raycastTarget = false;
                return r;
            }
            switch (v)
            {
                case "swordClash":
                    anim.SwordL = Pic("SwordL", "ico.sword", 110, new Vector2(-46, -60), new Vector2(0.5f, 0.05f));
                    anim.SwordR = Pic("SwordR", "ico.sword", 110, new Vector2(46, -60), new Vector2(0.5f, 0.05f));
                    var spark = Ui.Rect(card, "Spark");
                    spark.anchorMin = spark.anchorMax = new Vector2(0.5f, 0.5f);
                    spark.sizeDelta = new Vector2(110, 110);
                    spark.anchoredPosition = new Vector2(0, 18);
                    anim.Spark = spark.gameObject.AddComponent<Image>();
                    anim.Spark.sprite = SegmentArt.Glow; anim.Spark.color = new Color(fx.r, fx.g, fx.b, 0f); anim.Spark.raycastTarget = false;
                    break;
                case "horseRun":
                    {
                        var road = Ui.Rect(card, "Road");
                        road.anchorMin = new Vector2(0f, 0f); road.anchorMax = new Vector2(1f, 0f);
                        road.pivot = new Vector2(0f, 0f);
                        road.offsetMin = new Vector2(0f, 34f); road.offsetMax = new Vector2(56f, 38f);
                        var ri = road.gameObject.AddComponent<Image>();
                        ri.sprite = UiKit.Dash; ri.type = Image.Type.Tiled; ri.color = new Color(fx.r, fx.g, fx.b, 0.5f); ri.raycastTarget = false;
                        anim.Road = road;
                        var horse = Ui.Rect(card, "Horse");
                        horse.anchorMin = horse.anchorMax = new Vector2(0f, 0.5f);
                        horse.sizeDelta = new Vector2(96, 96);
                        var hi = horse.gameObject.AddComponent<Image>();
                        hi.sprite = SegmentArt.Icon("ico.horse"); hi.preserveAspect = true; hi.color = fx; hi.raycastTarget = false;
                        anim.Runner = horse;
                        break;
                    }
                case "flagWave":
                    anim.Flag = Pic("Flag", "ico.army", 110, Vector2.zero, new Vector2(0.15f, 0.5f));
                    break;
                default:
                    {
                        // 도는 고리: 둥근 판을 3/4만 채우고(방사형) 안쪽을 카드 색으로 덮어 고리처럼
                        var ring = Ui.Rect(card, "Ring");
                        ring.anchorMin = ring.anchorMax = ring.pivot = new Vector2(0.5f, 0.5f);
                        ring.sizeDelta = new Vector2(96f, 96f);
                        var arc = Ui.Background(ring, fx);
                        arc.sprite = UiShapes.Circle;
                        arc.type = Image.Type.Filled;
                        arc.fillMethod = Image.FillMethod.Radial360;
                        arc.fillAmount = 0.75f;
                        arc.raycastTarget = false;
                        var hole = Ui.Rect(ring, "Hole");
                        hole.anchorMin = hole.anchorMax = hole.pivot = new Vector2(0.5f, 0.5f);
                        hole.sizeDelta = new Vector2(70f, 70f);
                        var hb = Ui.Background(hole, back);
                        hb.sprite = UiShapes.Circle;
                        hb.raycastTarget = false;
                        anim.Spin = ring;
                        break;
                    }
            }
            return card;
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
            if (on && _builtVersion != UiKit.Version)
            {
                // 부품 꾸미기 값이 바뀌었으면 카드를 다시 만든다 (처음 보일 때 · 꾸미기 화면에서 바꾼 뒤)
                for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
                BuildCard(transform);
                _builtVersion = UiKit.Version;
            }
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
            transform.SetAsLastSibling(); // 확인 창 등 뒤에 붙는 것보다 늘 위
        }
    }
}

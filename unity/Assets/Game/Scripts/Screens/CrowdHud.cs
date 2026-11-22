using System;
using System.Collections.Generic;
using Game.Battle.Model.Crowd;
using Game.Unity.Battle3D;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 샘플전투2 전장 위 표시 (R-129, 기존 전투의 조작 화면을 옮김): 장수 이름표 (부대 이름 · 병종, 사기 막대 · 병력 막대) · 미니맵.
    /// 미니맵은 병사 점 (파랑 · 빨강)과 카메라가 보는 곳을 0.2초마다 그리고, 누르면 그곳으로 카메라를 옮긴다.
    /// </summary>
    public sealed class CrowdHud
    {
        private readonly CrowdBattle _battle;
        private readonly CrowdField3D _field;
        private readonly RectTransform _area;
        /// <summary>이름표 · 미니맵을 담는 판 (전장 영역을 채움, 지우면 함께 사라짐).</summary>
        private readonly RectTransform _root;
        private readonly Func<CrowdArmy, string> _name;
        private readonly Dictionary<int, (RectTransform root, Text label, RectTransform morale, Image moraleImg, RectTransform troops)> _tags =
            new Dictionary<int, (RectTransform, Text, RectTransform, Image, RectTransform)>();

        // 미니맵
        private const int MapPixels = 64;
        private const float MapSize = 200f;
        private readonly Texture2D _map;
        /// <summary>미니맵 픽셀 크기 (지형 맵이면 전장 1칸 = 1픽셀, 60 × 100 — R-133) · 바탕 (지형 색).</summary>
        private readonly int _mw, _mh;
        private readonly Color32[] _pixels, _base;
        private float _mapTimer;
        private static readonly Color32 Grass = new Color32(96, 128, 72, 230);
        private static readonly Color32 AllyDot = new Color32(70, 120, 235, 255);
        private static readonly Color32 EnemyDot = new Color32(225, 70, 55, 255);
        private static readonly Color32 ViewDot = new Color32(255, 255, 255, 255);

        public CrowdHud(CrowdBattle battle, CrowdField3D field, RectTransform area, Func<CrowdArmy, string> name)
        {
            _battle = battle;
            _field = field;
            _area = area;
            _name = name;
            _root = Ui.Rect(area, "Hud");
            Ui.Fill(_root);
            _root.SetAsFirstSibling(); // 원형 메뉴 · 버튼보다 아래
            foreach (var a in battle.Armies) _tags[a.Id] = Tag(a);

            var holder = Ui.Rect(_root, "MiniMap");
            holder.anchorMin = holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(1f, 1f);
            holder.anchoredPosition = new Vector2(-12f, -12f);
            _base = field.Ground.MiniColors;
            _mw = _base != null ? field.Ground.MiniW : MapPixels;
            _mh = _base != null ? field.Ground.MiniH : MapPixels;
            _pixels = new Color32[_mw * _mh];
            holder.sizeDelta = _mh > _mw ? new Vector2(MapSize * _mw / _mh, MapSize) : new Vector2(MapSize, MapSize * _mh / _mw);
            var frame = Ui.Background(holder, new Color(0.1f, 0.09f, 0.08f, 0.8f));
            frame.raycastTarget = true;
            var img = Ui.Rect(holder, "Picture");
            Ui.Fill(img);
            img.offsetMin = new Vector2(4f, 4f);
            img.offsetMax = new Vector2(-4f, -4f);
            _map = new Texture2D(_mw, _mh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var raw = img.gameObject.AddComponent<RawImage>();
            raw.texture = _map;
            raw.raycastTarget = false;
            holder.gameObject.AddComponent<MiniMapTap>().Tapped = uv =>
            {
                float w = (float)_battle.Rules.FieldWidth.ToDisplayDouble(), d = (float)_battle.Rules.FieldDepth.ToDisplayDouble();
                _field.Rig.LookAt(new Vector3(uv.x * w, 0f, uv.y * d));
            };
            DrawMap();
        }

        public void Destroy()
        {
            if (_map != null) UnityEngine.Object.Destroy(_map);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }

        private (RectTransform, Text, RectTransform, Image, RectTransform) Tag(CrowdArmy a)
        {
            var root = Ui.Rect(_root, "Tag " + a.Id);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(200f, 56f);
            var teamCol = a.Team == CrowdBattle.Ally ? Palette.Ally : Palette.Enemy;
            var label = Ui.Label(root, "", 22, TextAnchor.LowerCenter, Palette.IsLight ? Color.Lerp(teamCol, Color.white, 0.55f) : teamCol);
            Ui.Outlined(label);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0f, 0.45f); lr.anchorMax = new Vector2(1f, 1f);
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var morale = Bar(root, "Morale", 0.22f, new Color(0.97f, 0.66f, 0.2f), out var mImg);
            var troops = Bar(root, "Troops", 0f, new Color(0.3f, 0.78f, 0.36f), out _);
            return (root, label, morale, mImg, troops);
        }

        /// <summary>가는 막대 (뒤 어두운 판 + 채움), 이름표 아래쪽 bottom 높이에.</summary>
        private static RectTransform Bar(RectTransform parent, string name, float bottom, Color fillColor, out Image fillImg)
        {
            var bar = Ui.Rect(parent, name);
            bar.anchorMin = new Vector2(0.2f, bottom); bar.anchorMax = new Vector2(0.8f, bottom + 0.18f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            Ui.Background(bar, new Color(0.12f, 0.12f, 0.11f, 0.85f)).raycastTarget = false;
            var fill = Ui.Rect(bar, "Fill");
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fillImg = Ui.Background(fill, fillColor);
            fillImg.raycastTarget = false;
            return fill;
        }

        /// <summary>매 프레임: 이름표를 장수 머리 위에 (화면 밖 · 무너져 사라진 부대는 숨김), 미니맵은 0.2초마다.</summary>
        public void Tick(float dt)
        {
            var cam = _field.Rig.Camera;
            foreach (var a in _battle.Armies)
            {
                var t = _tags[a.Id];
                bool show = _battle.Standing(a);
                Vector3 sp = Vector3.zero;
                if (show)
                {
                    sp = cam.WorldToScreenPoint(_field.TagAnchor(a) + Vector3.up * 1.4f);
                    show = sp.z > 0 && cam.pixelRect.Contains(sp);
                }
                if (t.root.gameObject.activeSelf != show) t.root.gameObject.SetActive(show);
                if (!show) continue;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out var local)) t.root.anchoredPosition = local;
                t.label.text = _name(a);
                float m = Mathf.Clamp01((float)(a.Morale / _battle.Rules.MoraleStart).ToDisplayDouble());
                t.morale.anchorMax = new Vector2(m, 1f);
                t.moraleImg.color = a.Routed || a.Wavering ? new Color(0.85f, 0.3f, 0.22f) : new Color(0.97f, 0.66f, 0.2f);
                t.troops.anchorMax = new Vector2(a.StartCount > 0 ? Mathf.Clamp01(a.AliveCount / (float)a.StartCount) : 0f, 1f);
            }
            _mapTimer -= dt;
            if (_mapTimer <= 0f) { _mapTimer = 0.2f; DrawMap(); }
        }

        private void DrawMap()
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = _base != null ? _base[i] : Grass;
            float w = (float)_battle.Rules.FieldWidth.ToDisplayDouble(), d = (float)_battle.Rules.FieldDepth.ToDisplayDouble();
            foreach (var s in _battle.Soldiers)
            {
                if (!s.Alive) continue;
                int x = Mathf.Clamp((int)((float)s.Pos.X.ToDisplayDouble() / w * _mw), 0, _mw - 1);
                int y = Mathf.Clamp((int)((float)s.Pos.Y.ToDisplayDouble() / d * _mh), 0, _mh - 1);
                _pixels[y * _mw + x] = s.Team == CrowdBattle.Ally ? AllyDot : EnemyDot;
            }
            // 카메라가 보는 곳: 흰 네모
            var f = _field.Rig.Focus;
            int fx = Mathf.Clamp((int)(f.x / w * _mw), 1, _mw - 2), fy = Mathf.Clamp((int)(f.z / d * _mh), 1, _mh - 2);
            for (int k = -2; k <= 2; k++)
            {
                SetPx(fx + k, fy - 2); SetPx(fx + k, fy + 2); SetPx(fx - 2, fy + k); SetPx(fx + 2, fy + k);
            }
            _map.SetPixels32(_pixels);
            _map.Apply(false);
        }

        private void SetPx(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _mw || y >= _mh) return;
            _pixels[y * _mw + x] = ViewDot;
        }
    }

    /// <summary>미니맵 누름 → 그 자리 (0~1, 왼쪽 아래 기준).</summary>
    public sealed class MiniMapTap : MonoBehaviour, IPointerClickHandler
    {
        public Action<Vector2> Tapped;

        public void OnPointerClick(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local)) return;
            var r = rt.rect;
            Tapped?.Invoke(new Vector2(Mathf.InverseLerp(r.xMin, r.xMax, local.x), Mathf.InverseLerp(r.yMin, r.yMax, local.y)));
        }
    }
}

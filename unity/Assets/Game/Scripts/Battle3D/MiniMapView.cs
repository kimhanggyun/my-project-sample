using System.Collections.Generic;
using Game.Battle.Model.Control;
using Game.Battle.Model.Simulation;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 미니맵 (MinimapView 자리): 전장 텍스처(안개 반영) + 보이는 군단 점 + 카메라가 보는 곳. 누르면 카메라가 그곳으로 간다.
    /// </summary>
    public sealed class MiniMapView
    {
        private const float Dot = 10f;

        private readonly BattleControl _ctl;
        private readonly BattleField3D _field;
        private readonly RectTransform _rect;
        private readonly Dictionary<CorpsState, Image> _dots = new Dictionary<CorpsState, Image>();
        private readonly Image _view;

        public MiniMapView(RectTransform parent, BattleControl ctl, BattleField3D field, float width)
        {
            _ctl = ctl;
            _field = field;
            _rect = Ui.Rect(parent, "MiniMap");
            _rect.anchorMin = _rect.anchorMax = new Vector2(1f, 1f);
            _rect.pivot = new Vector2(1f, 1f);
            _rect.anchoredPosition = new Vector2(-12f, -64f);
            _rect.sizeDelta = new Vector2(width, width * field.Height / Mathf.Max(1, field.Width));

            var frame = Ui.Panel(_rect, "Frame", new Color(0f, 0f, 0f, 0.6f));
            frame.offsetMin = new Vector2(-4f, -4f);
            frame.offsetMax = new Vector2(4f, 4f);
            frame.GetComponent<Image>().raycastTarget = false;
            var raw = _rect.gameObject.AddComponent<RawImage>();
            raw.texture = field.MapTexture;
            var tap = _rect.gameObject.AddComponent<GestureArea>();
            tap.Tapped += OnTap;
            tap.Dragged += (a, b) => OnTap(b);

            _view = Mark(new Color(1f, 1f, 1f, 0.9f), 14f);
            foreach (var c in ctl.Context.Corps) _dots[c] = Mark(c.Team == ctl.Team ? Palette.Ally : Palette.Enemy, Dot);
        }

        public void Refresh()
        {
            var ctx = _ctl.Context;
            foreach (var kv in _dots)
            {
                var c = kv.Key;
                bool shown = c.InBattle && ctx.IsVisibleTo(_ctl.Team, c);
                kv.Value.gameObject.SetActive(shown);
                if (shown) kv.Value.rectTransform.anchoredPosition = Local(_field.ToWorld(c.Anchor));
            }
            _view.rectTransform.anchoredPosition = Local(_field.Rig.Focus);
        }

        private void OnTap(Vector2 screen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, null, out var local)) return;
            var r = _rect.rect;
            float u = Mathf.Clamp01((local.x - r.xMin) / r.width), v = Mathf.Clamp01((local.y - r.yMin) / r.height);
            _field.Rig.LookAt(new Vector3(u * _field.Width, 0f, v * _field.Height));
        }

        /// <summary>월드 → 미니맵 안 위치 (왼쪽 아래 기준).</summary>
        private Vector2 Local(Vector3 world)
        {
            var size = _rect.rect.size;
            return new Vector2(world.x / _field.Width * size.x, world.z / _field.Height * size.y);
        }

        private Image Mark(Color color, float size)
        {
            var rt = Ui.Rect(_rect, "Dot");
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}

using System.Collections.Generic;
using Game.Battle.Model.Control;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Foundation.Math;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// (임시) 위에서 본 2D 전장: 지형 색 + 안개 + 부대원 점 + 군단 이름. 3D 전장 전까지 명령 입력을 확인하는 용도.
    /// 누르면 칸 좌표로 바꿔 <see cref="BattleControl.Tap"/>에 넘긴다. 보이지 않는 적은 그리지 않는다.
    /// </summary>
    public sealed class BattleMapView
    {
        private const float DotSize = 16f;

        /// <summary>임시 지형 색 (화면 디자인 단계에서 3D 전장으로 바뀐다). 모르는 지형은 회색.</summary>
        private static readonly Dictionary<string, Color32> TerrainColors = new Dictionary<string, Color32>
        {
            { "plain", new Color32(120, 150, 80, 255) },
            { "grass", new Color32(110, 160, 80, 255) },
            { "road", new Color32(170, 150, 110, 255) },
            { "forest", new Color32(40, 95, 45, 255) },
            { "burnt", new Color32(60, 50, 45, 255) },
            { "hill", new Color32(150, 140, 85, 255) },
            { "mountain", new Color32(115, 105, 95, 255) },
            { "cliff", new Color32(80, 75, 70, 255) },
            { "pass", new Color32(140, 125, 100, 255) },
            { "river", new Color32(55, 95, 165, 255) },
            { "ford", new Color32(90, 130, 175, 255) },
            { "bridge", new Color32(140, 100, 60, 255) },
            { "marsh", new Color32(85, 105, 80, 255) },
            { "building", new Color32(150, 120, 110, 255) },
        };
        private static readonly Color32 FireColor = new Color32(230, 110, 30, 255);
        private static readonly Color Selected = new Color(1f, 0.9f, 0.3f);
        private static readonly Color TargetMark = new Color(1f, 1f, 1f, 0.9f);

        private readonly BattleControl _ctl;
        private readonly Boot.GameContext _game;
        private readonly RectTransform _rect;
        private readonly Texture2D _texture;
        private readonly Color32[] _pixels;
        private readonly Dictionary<SubUnitState, Image> _dots = new Dictionary<SubUnitState, Image>();
        private readonly Dictionary<CorpsState, Text> _labels = new Dictionary<CorpsState, Text>();
        private readonly Image _target;

        public RectTransform Rect => _rect;

        public BattleMapView(Transform parent, BattleControl ctl, Boot.GameContext game, System.Action<TapResult> onTap)
        {
            _ctl = ctl;
            _game = game;
            var field = ctl.Context.Field;
            _texture = new Texture2D(field.Width, field.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _pixels = new Color32[field.Width * field.Height];

            _rect = Ui.Rect(parent, "Map");
            var raw = _rect.gameObject.AddComponent<RawImage>();
            raw.texture = _texture;
            var tap = _rect.gameObject.AddComponent<PointerTap>();
            tap.Tapped += uv => onTap(_ctl.Tap(ToCell(uv)));

            _target = MakeMark("Target", 26f, TargetMark);
            _target.gameObject.SetActive(false);

            foreach (var c in ctl.Context.Corps)
            {
                bool ally = c.Team == ctl.Team;
                foreach (var u in c.Units) _dots[u] = MakeMark("Unit", DotSize, ally ? Palette.Ally : Palette.Enemy);
                var label = Ui.Label(_rect, "", 22, TextAnchor.LowerCenter, ally ? Palette.Ally : Palette.Enemy);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
                label.rectTransform.pivot = new Vector2(0.5f, 0f);
                label.rectTransform.sizeDelta = new Vector2(200, 30);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.text = c.Setup.General != null ? game.GeneralName(c.Setup.General.Id) : c.Id;
                _labels[c] = label;
            }
        }

        /// <summary>전장 칸 비율에 맞는 높이 (가로 폭 기준).</summary>
        public float HeightFor(float width) => width * _ctl.Context.Field.Height / Mathf.Max(1, _ctl.Context.Field.Width);

        public void Close()
        {
            if (_texture != null) Object.Destroy(_texture);
        }

        public void Refresh()
        {
            var ctx = _ctl.Context;
            var field = ctx.Field;
            int w = field.Width, h = field.Height;
            ctx.Vision.TryGetValue(_ctl.Team, out var vision);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color32 c = TerrainColors.TryGetValue(field.TerrainAt(new Cell(x, y)), out var tc) ? tc : new Color32(128, 128, 128, 255);
                    if (ctx.Environment != null && ctx.Environment.Burning.ContainsKey(i)) c = FireColor;
                    float shade = vision?.VisibleCells == null || vision.ExploredCells == null ? 1f : vision.VisibleCells[i] ? 1f : vision.ExploredCells[i] ? 0.55f : 0.25f;
                    // 텍스처는 아래 줄부터 → 전장 y=0(위)을 맨 위에 그린다.
                    _pixels[(h - 1 - y) * w + x] = new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
                }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            var size = _rect.rect.size;
            foreach (var kv in _labels)
            {
                var corps = kv.Key;
                bool shown = corps.InBattle && ctx.IsVisibleTo(_ctl.Team, corps);
                bool selected = corps == _ctl.Selected;
                foreach (var u in corps.Units)
                {
                    var dot = _dots[u];
                    bool on = shown && u.Alive;
                    dot.gameObject.SetActive(on);
                    if (!on) continue;
                    dot.rectTransform.anchoredPosition = ToLocal(u.Position, size);
                    dot.color = selected ? Selected : corps.Team == _ctl.Team ? Palette.Ally : Palette.Enemy;
                }
                kv.Value.gameObject.SetActive(shown);
                if (shown)
                {
                    var p = ToLocal(corps.Anchor, size);
                    kv.Value.rectTransform.anchoredPosition = new Vector2(p.x, p.y + DotSize);
                    kv.Value.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            var sel = _ctl.Selected;
            bool hasTarget = sel != null && sel.InBattle && sel.Order.Kind == OrderKinds.MoveTo;
            _target.gameObject.SetActive(hasTarget);
            if (hasTarget) _target.rectTransform.anchoredPosition = ToLocal(sel.Order.Point, size);
        }

        private Image MakeMark(string name, float size, Color color)
        {
            var rt = Ui.Rect(_rect, name);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>전장 좌표(칸) → 지도 안 위치 (왼쪽 아래 기준).</summary>
        private Vector2 ToLocal(Vec p, Vector2 size)
        {
            var f = _ctl.Context.Field;
            float x = (float)p.X.ToDisplayDouble() / f.Width * size.x;
            float y = (1f - (float)p.Y.ToDisplayDouble() / f.Height) * size.y;
            return new Vector2(x, y);
        }

        /// <summary>
        /// 누른 위치(정규화) → 전장 좌표. 입력 경계에서 한 번만 고정소수점으로 바꾸고, 명령 기록에는 그 값이 남는다 (리플레이 결정성).
        /// </summary>
        private Vec ToCell(Vector2 uv)
        {
            var f = _ctl.Context.Field;
            long rx = (long)Mathf.Round(uv.x * f.Width * Fix.One);
            long ry = (long)Mathf.Round((1f - uv.y) * f.Height * Fix.One);
            return new Vec(Fix.FromRaw(rx), Fix.FromRaw(ry));
        }
    }
}

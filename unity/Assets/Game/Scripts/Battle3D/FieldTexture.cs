using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 전장 칸 하나 = 픽셀 하나인 지형 텍스처 + 안개 3단계(보임·탐색됨·미탐색) + 불 (battle.md 2.10, FogOfWarView 자리).
    /// 텍스처 아래 줄이 전장 y = 높이-1 (맨 아래), 위 줄이 y = 0.
    /// </summary>
    public sealed class FieldTexture
    {
        /// <summary>임시 지형 색 (아트 전까지). 모르는 지형은 회색.</summary>
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
        private static readonly Color32 Unknown = new Color32(128, 128, 128, 255);
        private static readonly Color32 FireColor = new Color32(230, 110, 30, 255);
        private const float ExploredShade = 0.55f;
        private const float HiddenShade = 0.25f;

        private readonly BattleContext _ctx;
        private readonly string _team;
        private readonly Color32[] _pixels;

        public Texture2D Texture { get; }

        public FieldTexture(BattleContext ctx, string team)
        {
            _ctx = ctx;
            _team = team;
            var f = ctx.Field;
            Texture = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _pixels = new Color32[f.Width * f.Height];
        }

        /// <summary>안개 끄고 보기 (녹화 재생): 모든 칸을 밝게.</summary>
        public bool RevealAll { get; set; }

        public void Refresh()
        {
            var field = _ctx.Field;
            int w = field.Width, h = field.Height;
            _ctx.Vision.TryGetValue(_team, out var vision);
            bool fog = !RevealAll && vision?.VisibleCells != null && vision.ExploredCells != null;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color32 c = TerrainColors.TryGetValue(field.TerrainAt(new Cell(x, y)), out var tc) ? tc : Unknown;
                    if (_ctx.Environment != null && _ctx.Environment.Burning.ContainsKey(i)) c = FireColor;
                    float shade = !fog || vision.VisibleCells[i] ? 1f : vision.ExploredCells[i] ? ExploredShade : HiddenShade;
                    _pixels[(h - 1 - y) * w + x] = new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
                }
            Texture.SetPixels32(_pixels);
            Texture.Apply(false);
        }

        public void Destroy() => Object.Destroy(Texture);
    }
}

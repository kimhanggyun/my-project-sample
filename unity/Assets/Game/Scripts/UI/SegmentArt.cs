using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 세그먼트 탭 뒷배경 그림 (2026-11-26 사용자 시안 B): 그림 파일 없이 코드로 그리는 두세 색 실루엣.
    /// general = 투구 쓴 장군 얼굴, troop = 창 든 병사 5명, formation = 장군이 병사 5명을 가리킴.
    /// 색 셋: k(몸) · m(얼굴 · 중간) · x(깃 · 창날 · 가리키는 선). 고른 칸 = 강조색 짙은 쪽 + 밝은 강조, 안 고른 칸 = 카드색과 흐린 글자색 사이 회색.
    /// 진짜 그림이 생기면 Resources/Ui/seg_&lt;id&gt;_on · seg_&lt;id&gt;_off 에 넣으면 그것을 먼저 쓴다.
    /// </summary>
    public static class SegmentArt
    {
        private const float Scale = 4f; // 그림 좌표 1 = 4px (높이 44 → 176px)

        private enum Ink { K, M, X }

        private readonly struct Shape
        {
            public readonly Ink Ink;
            public readonly List<List<Vector2>> Rings;
            public Shape(Ink ink, List<List<Vector2>> rings) { Ink = ink; Rings = rings; }
        }

        private sealed class Scene
        {
            public float W, H;
            public readonly List<Shape> Shapes = new List<Shape>();
        }

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static Sprite _glow;

        public static bool Has(string id) => id == "general" || id == "troop" || id == "formation";

        /// <summary>그 칸의 그림 (on = 고른 칸 색). 그림이 없는 id면 null.</summary>
        public static Sprite Get(string id, bool on)
        {
            if (!Has(id)) return null;
            Color k, m, x;
            if (on)
            {
                var a = Palette.ButtonActive;
                k = Color.Lerp(Color.black, a, 0.5f);
                m = Color.Lerp(a, Palette.AccentLight, 0.3f);
                x = Palette.AccentLight;
            }
            else
            {
                k = Color.Lerp(Palette.Card, Palette.TextDim, 0.3f);
                m = Color.Lerp(Palette.Card, Palette.TextDim, 0.5f);
                x = Color.Lerp(Palette.Card, Palette.TextDim, 0.65f);
            }
            string key = id + (on ? "_on" : "_off") + ColorUtility.ToHtmlStringRGB(k) + ColorUtility.ToHtmlStringRGB(x);
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var loaded = Resources.Load<Sprite>("Ui/seg_" + id + (on ? "_on" : "_off"));
            if (loaded != null) return Cache[key] = loaded;
            return Cache[key] = Render(SceneOf(id), k, m, x);
        }

        /// <summary>글자 뒤에 까는 둥근 그림자 (가운데 진하고 가장자리로 사라짐, 흰색 — Image 색으로 물듦).</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int w = 128, h = 64;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f - w / 2f) / (w / 2f), dy = (y + 0.5f - h / 2f) / (h / 2f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3 - 2 * a);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * a));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                return _glow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // ---------------- 그림 (시안 SVG 좌표 그대로) ----------------

        private static Scene SceneOf(string id)
        {
            var s = new Scene { H = 44 };
            switch (id)
            {
                case "general":
                    s.W = 80;
                    Add(s, Ink.K, "M16 44 Q20 35 31 35 L49 35 Q60 35 64 44Z");
                    AddEllipse(s, Ink.M, 40, 25, 10.5f, 11.5f);
                    Add(s, Ink.K, "M26.5 24 Q25.5 7.5 40 6.5 Q54.5 7.5 53.5 24 L49.5 24 L49.5 17 L30.5 17 L30.5 24Z");
                    Add(s, Ink.K, "M26.5 21 L26 33 L32 31 L31 21Z M53.5 21 L54 33 L48 31 L49 21Z");
                    Add(s, Ink.X, "M40 6.5 Q37 0.5 46 0.5 Q42 3 42.5 6.5Z");
                    AddRect(s, Ink.X, 29, 16, 22, 1.6f);
                    AddRect(s, Ink.K, 34, 21.5f, 4.5f, 1.6f);
                    AddRect(s, Ink.K, 41.5f, 21.5f, 4.5f, 1.6f);
                    Add(s, Ink.K, "M33 31 Q40 42 47 31 Q40 35 33 31Z");
                    break;
                case "troop":
                    s.W = 84;
                    Soldier(s, 14, 16, 1);
                    Soldier(s, 28, 14, 1);
                    Soldier(s, 42, 16, 1);
                    Soldier(s, 56, 14, 1);
                    Soldier(s, 70, 16, 1);
                    break;
                default: // formation
                    s.W = 84;
                    const float g = 1.25f, gx = 13, gy = 12;
                    AddEllipse(s, Ink.M, gx, gy, 3.4f * g, 3.4f * g);
                    Add(s, Ink.K, "M-3.9 -0.2 Q0 -6 3.9 -0.2Z", g, gx, gy);
                    Add(s, Ink.X, "M0 -4.6 Q-1 -8 3.5 -8 Q1.5 -6 1.2 -4.4Z", g, gx, gy);
                    Add(s, Ink.K, "M-4.5 4 L4.5 4 L5.5 16 L-5.5 16Z", g, gx, gy);
                    Add(s, Ink.K, "M3 5 L15 0.5 L15.8 2.8 L4 8.5Z", g, gx, gy);
                    AddEllipse(s, Ink.M, gx + 16.3f * g, gy + 1.4f * g, 1.3f * g, 1.3f * g);
                    AddRect(s, Ink.K, gx - 4.5f * g, gy + 16 * g, 3.2f * g, 8 * g);
                    AddRect(s, Ink.K, gx + 1.3f * g, gy + 16 * g, 3.2f * g, 8 * g);
                    AddRect(s, Ink.X, 36, 14.3f, 4, 1.4f);
                    AddRect(s, Ink.X, 43, 14.3f, 4, 1.4f);
                    const float q = 0.78f;
                    Soldier(s, 67 * q, 23 * q, q);
                    Soldier(s, 83 * q, 23 * q, q);
                    Soldier(s, 99 * q, 23 * q, q);
                    Soldier(s, 75 * q, 35 * q, q);
                    Soldier(s, 91 * q, 35 * q, q);
                    break;
            }
            return s;
        }

        /// <summary>창 든 병사 하나 (머리 가운데 = ox, oy).</summary>
        private static void Soldier(Scene s, float ox, float oy, float k)
        {
            Add(s, Ink.X, "M5.6 -13 L4.3 -8.5 L6.9 -8.5Z", k, ox, oy);
            AddRect(s, Ink.X, ox + 5 * k, oy - 9 * k, 1.2f * k, 33 * k);
            AddEllipse(s, Ink.M, ox, oy, 3.2f * k, 3.2f * k);
            Add(s, Ink.K, "M-3.7 -0.3 Q0 -5.8 3.7 -0.3Z", k, ox, oy);
            Add(s, Ink.K, "M-4 4 L4 4 L5 16 L-5 16Z", k, ox, oy);
            AddRect(s, Ink.K, ox - 4 * k, oy + 16 * k, 3 * k, 8 * k);
            AddRect(s, Ink.K, ox + 1 * k, oy + 16 * k, 3 * k, 8 * k);
        }

        private static void AddRect(Scene s, Ink ink, float x, float y, float w, float h) =>
            s.Shapes.Add(new Shape(ink, new List<List<Vector2>> { new List<Vector2> { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) } }));

        private static void AddEllipse(Scene s, Ink ink, float cx, float cy, float rx, float ry)
        {
            var ring = new List<Vector2>();
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2f / 40;
                ring.Add(new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry));
            }
            s.Shapes.Add(new Shape(ink, new List<List<Vector2>> { ring }));
        }

        /// <summary>SVG 경로 (M · L · Q · Z, 절대 좌표만) → 다각형. 점마다 k배 후 (ox, oy)만큼 옮김.</summary>
        private static void Add(Scene s, Ink ink, string d, float k = 1, float ox = 0, float oy = 0)
        {
            var rings = new List<List<Vector2>>();
            List<Vector2> cur = null;
            var tok = d.Replace("M", " M ").Replace("L", " L ").Replace("Q", " Q ").Replace("Z", " Z ").Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            Vector2 P() { var p = new Vector2(float.Parse(tok[i], CultureInfo.InvariantCulture), float.Parse(tok[i + 1], CultureInfo.InvariantCulture)); i += 2; return new Vector2(ox + p.x * k, oy + p.y * k); }
            while (i < tok.Length)
            {
                string c = tok[i++];
                if (c == "M") { cur = new List<Vector2> { P() }; rings.Add(cur); }
                else if (c == "L") cur.Add(P());
                else if (c == "Q")
                {
                    var p0 = cur[cur.Count - 1];
                    var c1 = P();
                    var p1 = P();
                    for (int t = 1; t <= 10; t++)
                    {
                        float u = t / 10f;
                        cur.Add((1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * c1 + u * u * p1);
                    }
                }
                // Z: 다각형은 늘 닫힌 것으로 본다
            }
            s.Shapes.Add(new Shape(ink, rings));
        }

        // ---------------- 칠하기 (가로는 정확한 덮임, 세로는 4줄 표본) ----------------

        private static Sprite Render(Scene s, Color k, Color m, Color x)
        {
            int w = Mathf.CeilToInt(s.W * Scale), h = Mathf.CeilToInt(s.H * Scale);
            var rgba = new Color[w * h]; // 곱하지 않은 색, a = 덮임
            var cov = new float[w * h];
            var xs = new List<float>();
            const int Sub = 4;
            foreach (var shape in s.Shapes)
            {
                System.Array.Clear(cov, 0, cov.Length);
                for (int sy = 0; sy < h * Sub; sy++)
                {
                    float y = (sy + 0.5f) / Sub / Scale; // 그림 좌표
                    xs.Clear();
                    foreach (var ring in shape.Rings)
                        for (int e = 0; e < ring.Count; e++)
                        {
                            var a = ring[e];
                            var b = ring[(e + 1) % ring.Count];
                            if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                                xs.Add((a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x)) * Scale);
                        }
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    int row = (h - 1 - sy / Sub) * w; // 텍스처는 아래가 0
                    for (int j = 0; j + 1 < xs.Count; j += 2)
                    {
                        float x0 = Mathf.Clamp(xs[j], 0, w), x1 = Mathf.Clamp(xs[j + 1], 0, w);
                        for (int px = (int)x0; px < w && px < x1; px++)
                        {
                            float o = Mathf.Min(x1, px + 1) - Mathf.Max(x0, px);
                            if (o > 0) cov[row + px] += o / Sub;
                        }
                    }
                }
                var col = shape.Ink == Ink.K ? k : shape.Ink == Ink.M ? m : x;
                for (int p = 0; p < cov.Length; p++)
                {
                    float a = Mathf.Clamp01(cov[p]);
                    if (a <= 0) continue;
                    var d = rgba[p];
                    float oa = a + d.a * (1 - a);
                    var c = (col * a + d * d.a * (1 - a)) / oa;
                    c.a = oa;
                    rgba[p] = c;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(rgba);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}

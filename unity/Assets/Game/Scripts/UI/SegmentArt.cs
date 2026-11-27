using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 코드로 그리는 공용 그림 (docs/design/ui-kit.md 2장, data/Ui/uiKit.json arts): 두세 색 실루엣 · 흐린 배경 그림 · 아이콘.
    /// fig.* = 인물 (투구 장군 얼굴 · 병사 5명 · 장군이 가리킴), scene.* = 넓은 배경 (끝없는 대열 · 깃발 진군 · 성과 산), ico.* = 아이콘 (흰색, Image 색으로 물듦).
    /// 색 셋: k(몸) · m(중간 · 얼굴 · 먼 산) · x(깃 · 창날 · 강조). 진짜 그림이 생기면 Resources/Ui/&lt;id&gt; 를 먼저 쓴다.
    /// 세그먼트 탭(2026-11-26 시안 B)은 <see cref="Get"/> (general · troop · formation).
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
            public float W, H, Scale = SegmentArt.Scale;
            public readonly List<Shape> Shapes = new List<Shape>();
        }

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static Sprite _glow;

        /// <summary>세그먼트 id → 인물 그림 id.</summary>
        public static string FigureOf(string segmentId) =>
            segmentId == "general" ? "fig.general" : segmentId == "troop" ? "fig.troop5" : segmentId == "formation" ? "fig.command" : null;

        public static bool Has(string id) => FigureOf(id) != null;

        /// <summary>코드로 그릴 수 있는 그림 id인가 (fig · scene · ico).</summary>
        public static bool Knows(string artId) => artId != null && SceneOf(artId) != null;

        /// <summary>그 칸의 그림 (on = 고른 칸 색). 그림이 없는 id면 null.</summary>
        public static Sprite Get(string id, bool on) => Get(id, on, Palette.ButtonActive);

        /// <summary>그 칸의 그림, 고른 칸 바탕색 기준 (부품 꾸미기의 onColor).</summary>
        public static Sprite Get(string id, bool on, Color onBack)
        {
            string art = FigureOf(id);
            if (art == null) return null;
            Color k, m, x;
            if (on)
            {
                k = Color.Lerp(Color.black, onBack, 0.5f);
                m = Color.Lerp(onBack, Palette.AccentLight, 0.3f);
                x = Palette.AccentLight;
            }
            else
            {
                k = Color.Lerp(Palette.Card, Palette.TextDim, 0.3f);
                m = Color.Lerp(Palette.Card, Palette.TextDim, 0.5f);
                x = Color.Lerp(Palette.Card, Palette.TextDim, 0.65f);
            }
            var loaded = Resources.Load<Sprite>("Ui/seg_" + id + (on ? "_on" : "_off"));
            if (loaded != null) return loaded;
            return Art(art, k, m, x);
        }

        /// <summary>그림 (fig · scene · ico) 을 세 색으로. blur 0~1 = 흐림 (0이면 또렷). 없는 id면 null.</summary>
        public static Sprite Art(string artId, Color k, Color m, Color x, float blur = 0f)
        {
            var loaded = Resources.Load<Sprite>("Ui/" + artId);
            if (loaded != null) return loaded;
            int b = Mathf.RoundToInt(Mathf.Clamp01(blur) * 10f);
            string key = artId + ColorUtility.ToHtmlStringRGBA(k) + ColorUtility.ToHtmlStringRGBA(m) + ColorUtility.ToHtmlStringRGBA(x) + "b" + b;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var scene = SceneOf(artId);
            if (scene == null) return null;
            return Cache[key] = Render(scene, k, m, x, b);
        }

        /// <summary>아이콘 (흰색 · 중간은 반투명, Image 색으로 물듦). 없는 id면 null.</summary>
        public static Sprite Icon(string icoId) => Art(icoId, Color.white, new Color(1f, 1f, 1f, 0.55f), Color.white);

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

        // ---------------- 그림 (SVG 좌표) ----------------

        private static Scene SceneOf(string id)
        {
            var s = new Scene { H = 44 };
            switch (id)
            {
                case "fig.general":
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
                    return s;
                case "fig.troop5":
                    s.W = 84;
                    Soldier(s, 14, 16, 1);
                    Soldier(s, 28, 14, 1);
                    Soldier(s, 42, 16, 1);
                    Soldier(s, 56, 14, 1);
                    Soldier(s, 70, 16, 1);
                    return s;
                case "fig.command":
                    {
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
                        return s;
                    }
                case "scene.castle":
                    s.W = 200; s.H = 90;
                    Add(s, Ink.M, "M0 70 L25 40 L45 55 L70 25 L95 50 L120 30 L150 52 L175 35 L200 50 L200 90 L0 90Z");
                    AddRect(s, Ink.K, 0, 78, 200, 12);
                    AddRect(s, Ink.K, 50, 52, 100, 28);
                    for (int i = 0; i < 13; i++) AddRect(s, Ink.K, 50 + i * 7.9f, 48, 4.5f, 4.5f);
                    AddRect(s, Ink.K, 40, 40, 16, 40);
                    AddRect(s, Ink.K, 144, 40, 16, 40);
                    Add(s, Ink.K, "M36 41 L48 30 L60 41Z");
                    Add(s, Ink.K, "M140 41 L152 30 L164 41Z");
                    AddRect(s, Ink.K, 85, 36, 30, 18);
                    Add(s, Ink.K, "M79 37 Q100 21 121 37Z");
                    Add(s, Ink.M, "M92 80 L92 67 Q100 58 108 67 L108 80Z");
                    AddRect(s, Ink.X, 47.5f, 21, 1, 10);
                    Add(s, Ink.X, "M48.5 21 L57 23.5 L48.5 26Z");
                    AddRect(s, Ink.X, 151.5f, 21, 1, 10);
                    Add(s, Ink.X, "M152.5 21 L161 23.5 L152.5 26Z");
                    AddRect(s, Ink.X, 99.5f, 11, 1, 12);
                    Add(s, Ink.X, "M100.5 11 L111 14 L100.5 17Z");
                    return s;
                case "scene.army":
                    s.W = 200; s.H = 90;
                    Add(s, Ink.M, "M0 64 Q100 52 200 64 L200 90 L0 90Z");
                    foreach (float bx in new[] { 28f, 78f, 128f, 176f })
                    {
                        AddRect(s, Ink.X, bx, 28, 1, 26);
                        Add(s, Ink.X, "M" + F(bx + 1) + " 28 L" + F(bx + 12) + " 31.5 L" + F(bx + 1) + " 35Z");
                    }
                    for (int i = 0; i < 24; i++) Soldier(s, 6 + i * 8.2f, 52, 0.45f);
                    for (int i = 0; i < 18; i++) Soldier(s, 5 + i * 11f, 60, 0.65f);
                    for (int i = 0; i < 12; i++) Soldier(s, 9 + i * 16.5f, 66, 0.9f);
                    return s;
                case "scene.flags":
                    s.W = 200; s.H = 90;
                    Add(s, Ink.M, "M0 62 Q50 46 100 58 Q150 70 200 52 L200 90 L0 90Z");
                    AddRect(s, Ink.K, 0, 82, 200, 8);
                    for (int i = 0; i < 6; i++)
                    {
                        float ox = 18 + i * 32f, oy = 58 + (i % 2) * 3f;
                        AddRect(s, Ink.K, ox + 5.5f, oy - 34, 1.4f, 44);
                        Add(s, Ink.X, "M" + F(ox + 6.9f) + " " + F(oy - 34) + " L" + F(ox + 30) + " " + F(oy - 31) + " L" + F(ox + 23) + " " + F(oy - 25.5f)
                            + " L" + F(ox + 30) + " " + F(oy - 20) + " L" + F(ox + 6.9f) + " " + F(oy - 17) + "Z");
                        Soldier(s, ox, oy, 0.85f);
                    }
                    return s;
                // ---- 아이콘 (24 × 24) ----
                case "ico.territory":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M3 6 L9 4 L9 18 L3 20Z");
                    Add(s, Ink.M, "M9 4 L15 6 L15 20 L9 18Z");
                    Add(s, Ink.K, "M15 6 L21 4 L21 18 L15 20Z");
                    return s;
                case "ico.army":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    AddRect(s, Ink.K, 5, 3, 2, 18);
                    Add(s, Ink.K, "M7 4 L20 6 L15.5 9.5 L20 13 L7 13Z");
                    AddRect(s, Ink.K, 3, 20, 6, 2);
                    return s;
                case "ico.battle":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M3 4.5 L4.5 3 L18 16.5 L16.5 18Z");
                    Add(s, Ink.K, "M14 19.5 L19.5 14 L20.5 15 L15 20.5Z");
                    Add(s, Ink.K, "M17.5 18.5 L18.5 17.5 L21.5 20.5 L20.5 21.5Z");
                    Add(s, Ink.M, "M21 4.5 L19.5 3 L6 16.5 L7.5 18Z");
                    Add(s, Ink.M, "M10 19.5 L4.5 14 L3.5 15 L9 20.5Z");
                    Add(s, Ink.M, "M6.5 18.5 L5.5 17.5 L2.5 20.5 L3.5 21.5Z");
                    return s;
                case "ico.sword":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M11 2 L13 2 L13 16 L11 16Z M12 0.5 L13 2 L11 2Z");
                    AddRect(s, Ink.K, 7.5f, 16, 9, 2);
                    AddRect(s, Ink.M, 11, 18, 2, 5);
                    return s;
                case "ico.recruit":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    AddEllipse(s, Ink.K, 9, 8, 3.6f, 3.6f);
                    Add(s, Ink.K, "M2.5 21 Q2.5 13 9 13 Q15.5 13 15.5 21Z");
                    AddRect(s, Ink.X, 16.5f, 9, 6, 2);
                    AddRect(s, Ink.X, 18.5f, 7, 2, 6);
                    return s;
                case "ico.alliance":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    AddRect(s, Ink.K, 5, 3, 1.6f, 18);
                    Add(s, Ink.K, "M6.6 4 L13.5 6.5 L6.6 9Z");
                    AddRect(s, Ink.K, 17.4f, 3, 1.6f, 18);
                    Add(s, Ink.M, "M17.4 4 L10.5 6.5 L17.4 9Z");
                    AddRect(s, Ink.K, 3, 20, 18, 2);
                    return s;
                case "ico.codex":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M3.5 4 L11 5 L11 20 L3.5 19Z");
                    Add(s, Ink.K, "M13 5 L20.5 4 L20.5 19 L13 20Z");
                    AddRect(s, Ink.M, 11, 5, 2, 15);
                    return s;
                case "ico.bow":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M18 2.5 Q5 12 18 21.5 L16 21.5 Q2.5 12 16 2.5Z");
                    AddRect(s, Ink.M, 17.3f, 2.5f, 0.9f, 19);
                    AddRect(s, Ink.X, 4, 11.4f, 15, 1.2f);
                    Add(s, Ink.X, "M1 12 L5 9.2 L5 14.8Z");
                    Add(s, Ink.X, "M18.5 12 L21.5 9.5 L22.5 10.5 L20 12 L22.5 13.5 L21.5 14.5Z");
                    return s;
                case "ico.horse":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    AddEllipse(s, Ink.K, 11.5f, 11, 6.5f, 3.6f);
                    Add(s, Ink.K, "M15.5 9.5 L18.5 4 L20.6 5 L19.5 10.5Z");
                    Add(s, Ink.K, "M18.5 4 L23.5 6.6 L22.6 8.2 L18.8 6.8Z");
                    Add(s, Ink.K, "M16 13 L20 16.5 L19.2 17.6 L15 14.4Z");
                    Add(s, Ink.K, "M14.5 13.5 L15.6 19.5 L14.4 19.6 L13.4 14Z");
                    Add(s, Ink.K, "M8 13 L3.8 15.8 L4.5 17 L9 14.6Z");
                    Add(s, Ink.K, "M9 13.5 L7.8 19.6 L6.6 19.4 L7.6 13.4Z");
                    Add(s, Ink.M, "M5.5 9 Q1.5 8.6 0.8 13.4 Q2.6 11 5.6 10.6Z");
                    Add(s, Ink.M, "M18.6 4.4 L16 8.6 L17 9 L19.6 5Z");
                    return s;
                case "ico.shop":
                    s.W = 24; s.H = 24; s.Scale = 6;
                    Add(s, Ink.K, "M8 4.5 L16 4.5 L14 8 Q20.5 10 20.5 16 Q20.5 21.5 12 21.5 Q3.5 21.5 3.5 16 Q3.5 10 10 8Z");
                    AddRect(s, Ink.M, 9, 7.2f, 6, 1.6f);
                    return s;
            }
            return null;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

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

        // ---------------- 칠하기 (가로는 정확한 덮임, 세로는 4줄 표본, 도형 범위만) ----------------

        private static Sprite Render(Scene s, Color k, Color m, Color x, int blur)
        {
            float sc = s.Scale;
            int w = Mathf.CeilToInt(s.W * sc), h = Mathf.CeilToInt(s.H * sc);
            var rgba = new Color[w * h]; // 곱한 색 (premultiplied), a = 덮임
            var cov = new float[w * h];
            var xs = new List<float>();
            const int Sub = 4;
            foreach (var shape in s.Shapes)
            {
                float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
                foreach (var ring in shape.Rings)
                    foreach (var p in ring) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
                int sy0 = Mathf.Max(0, Mathf.FloorToInt(minY * sc * Sub)), sy1 = Mathf.Min(h * Sub, Mathf.CeilToInt(maxY * sc * Sub) + 1);
                int px0 = Mathf.Clamp(Mathf.FloorToInt(minX * sc), 0, w), px1 = Mathf.Clamp(Mathf.CeilToInt(maxX * sc) + 1, 0, w);
                int r0 = sy0 / Sub, r1 = Mathf.Min(h - 1, sy1 / Sub);
                for (int r = r0; r <= r1; r++) System.Array.Clear(cov, (h - 1 - r) * w + px0, px1 - px0);
                for (int sy = sy0; sy < sy1; sy++)
                {
                    float y = (sy + 0.5f) / Sub / sc;
                    xs.Clear();
                    foreach (var ring in shape.Rings)
                        for (int e = 0; e < ring.Count; e++)
                        {
                            var a = ring[e];
                            var b = ring[(e + 1) % ring.Count];
                            if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                                xs.Add((a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x)) * sc);
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
                for (int r = r0; r <= r1; r++)
                {
                    int row = (h - 1 - r) * w;
                    for (int px = px0; px < px1; px++)
                    {
                        int p = row + px;
                        float a = Mathf.Clamp01(cov[p]) * col.a;
                        if (a <= 0) continue;
                        var d = rgba[p];
                        rgba[p] = new Color(col.r * a + d.r * (1 - a), col.g * a + d.g * (1 - a), col.b * a + d.b * (1 - a), a + d.a * (1 - a));
                    }
                }
            }
            if (blur > 0) rgba = Blur(rgba, w, h, Mathf.Max(1, Mathf.RoundToInt(blur * h / 90f)));
            for (int p = 0; p < rgba.Length; p++)
            {
                var c = rgba[p];
                if (c.a > 0.0001f) rgba[p] = new Color(c.r / c.a, c.g / c.a, c.b / c.a, c.a);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(rgba);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>상자 흐림 두 번 (가로 · 세로), 곱한 색에서.</summary>
        private static Color[] Blur(Color[] src, int w, int h, int r)
        {
            var tmp = new Color[src.Length];
            var dst = new Color[src.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    var acc = new Color(0, 0, 0, 0);
                    int row = y * w;
                    for (int x = -r; x <= r; x++) acc += src[row + Mathf.Clamp(x, 0, w - 1)];
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x] = acc / (2 * r + 1);
                        acc += src[row + Mathf.Min(w - 1, x + r + 1)] - src[row + Mathf.Max(0, x - r)];
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    var acc = new Color(0, 0, 0, 0);
                    for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * w + x] = acc / (2 * r + 1);
                        acc += tmp[Mathf.Min(h - 1, y + r + 1) * w + x] - tmp[Mathf.Max(0, y - r) * w + x];
                    }
                }
                var t = src; src = dst; dst = t;
            }
            return src;
        }
    }
}

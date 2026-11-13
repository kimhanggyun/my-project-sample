using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 그림 파일 없이 코드로 만드는 화면 장식 (art-direction.md 3장 수묵·한지, 임시).
    /// 한지 바탕(타일), 먹선 테두리 판(9-slice), 붓 획 띠, 낙관(도장) 판. 모두 흰색 바탕이라 Image 색으로 물든다.
    /// 진짜 그림이 생기면 Resources/Ui/&lt;이름&gt; (paper, frame, brush, seal)에 넣으면 그것을 먼저 쓴다.
    /// 난수는 화면 꾸밈에만 쓰므로 규칙 난수(SeededRandom)와 무관하다.
    /// </summary>
    public static class UiArt
    {
        private static Sprite _paper, _frame, _brush, _seal;
        private static float _frameRadius = -1f;

        private static Sprite FromResources(string name) => Resources.Load<Sprite>("Ui/" + name);

        /// <summary>한지 바탕: 아주 옅은 얼룩과 섬유 결 (256×256, 이어 붙여도 이음매가 없게 둘레를 감싼 잡음).</summary>
        public static Sprite Paper
        {
            get
            {
                if (_paper != null) return _paper;
                var loaded = FromResources("paper");
                if (loaded != null) return _paper = loaded;
                const int n = 256;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                var rng = new System.Random(11);
                // 낮은 주파수 얼룩 (둘레 감싼 값 잡음 4겹)
                float[] Grid(int cells)
                {
                    var g = new float[cells * cells];
                    for (int i = 0; i < g.Length; i++) g[i] = (float)rng.NextDouble();
                    return g;
                }
                float Sample(float[] g, int cells, float x, float y)
                {
                    float gx = x * cells / n, gy = y * cells / n;
                    int x0 = (int)gx, y0 = (int)gy;
                    float fx = gx - x0, fy = gy - y0;
                    fx = fx * fx * (3 - 2 * fx);
                    fy = fy * fy * (3 - 2 * fy);
                    float V(int a, int b) => g[((b % cells + cells) % cells) * cells + ((a % cells + cells) % cells)];
                    return Mathf.Lerp(Mathf.Lerp(V(x0, y0), V(x0 + 1, y0), fx), Mathf.Lerp(V(x0, y0 + 1), V(x0 + 1, y0 + 1), fx), fy);
                }
                var g1 = Grid(4); var g2 = Grid(8); var g3 = Grid(16); var g4 = Grid(64);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float v = 0.45f * Sample(g1, 4, x, y) + 0.25f * Sample(g2, 8, x, y) + 0.18f * Sample(g3, 16, x, y) + 0.12f * Sample(g4, 64, x, y);
                        float shade = 0.93f + 0.07f * v;
                        px[y * n + x] = new Color32((byte)(255 * shade), (byte)(255 * shade), (byte)(255 * shade), 255);
                    }
                // 섬유: 짧은 옅은 실 (이어 붙임 고려해 둘레 감쌈)
                for (int f = 0; f < 140; f++)
                {
                    float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n;
                    float a = (float)rng.NextDouble() * Mathf.PI;
                    int len = 6 + rng.Next(18);
                    for (int s = 0; s < len; s++)
                    {
                        int px0 = (((int)(x + Mathf.Cos(a) * s)) % n + n) % n, py0 = (((int)(y + Mathf.Sin(a) * s)) % n + n) % n;
                        var c = px[py0 * n + px0];
                        byte d = (byte)Mathf.Max(0, c.r - 10);
                        px[py0 * n + px0] = new Color32(d, d, d, 255);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                return _paper = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        /// <summary>
        /// 먹선 테두리 판 (9-slice): 둥근 모서리, 가장자리에 붓으로 그은 듯 굵기가 조금씩 다른 어두운 선.
        /// 판 색(Image.color)을 곱하므로 선은 판 색의 짙은 쪽이 된다.
        /// </summary>
        public static Sprite Frame(float radius)
        {
            if (_frame != null && Mathf.Approximately(_frameRadius, radius)) return _frame;
            var loaded = FromResources("frame");
            if (loaded != null) { _frameRadius = radius; return _frame = loaded; }
            int r = Mathf.Max(4, Mathf.CeilToInt(radius));
            int border = r + 6;
            int size = border * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            var rng = new System.Random(5);
            float[] wobble = new float[size];
            for (int i = 0; i < size; i++) wobble[i] = (float)rng.NextDouble();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x < r ? r : (x >= size - r ? size - r - 1 : x);
                    float cy = y < r ? r : (y >= size - r ? size - r - 1 : y);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float inside = Mathf.Clamp01(r - d + 0.5f); // 판 모양 (모서리 둥글게)
                    // 바깥에서 안쪽으로 잰 깊이 → 선 굵기 2.5~4px, 위치마다 조금씩 흔들림
                    float depth = r - d;
                    float edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    if (d <= 0f) depth = edge; // 모서리 밖(가운데 줄)에서는 가장자리까지 거리
                    float w = 2.5f + 1.5f * wobble[(x + y) % size];
                    float ink = Mathf.Clamp01(w - depth + 0.5f);
                    float v = Mathf.Lerp(1f, 0.32f, ink); // 선은 판 색의 32%
                    byte c = (byte)(255 * v);
                    px[y * size + x] = new Color32(c, c, c, (byte)(255 * inside));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _frameRadius = radius;
            return _frame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>붓 획 띠: 가로로 길고 양끝이 갈라지며 가는 먹 자국 (제목 뒤에 깐다).</summary>
        public static Sprite Brush
        {
            get
            {
                if (_brush != null) return _brush;
                var loaded = FromResources("brush");
                if (loaded != null) return _brush = loaded;
                const int w = 512, h = 96;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                var rng = new System.Random(3);
                float[] top = new float[w], bottom = new float[w];
                float t = 0.15f, b = 0.85f;
                for (int x = 0; x < w; x++)
                {
                    t = Mathf.Clamp(t + ((float)rng.NextDouble() - 0.5f) * 0.03f, 0.05f, 0.35f);
                    b = Mathf.Clamp(b + ((float)rng.NextDouble() - 0.5f) * 0.03f, 0.65f, 0.95f);
                    float taper = Mathf.Clamp01(Mathf.Min(x / 60f, (w - x) / 110f)); // 왼쪽은 눌러 시작, 오른쪽은 길게 빠짐
                    float mid = 0.5f, half = (b - t) / 2f * taper;
                    top[x] = mid - half;
                    bottom[x] = mid + half;
                }
                var streaks = new float[h];
                for (int y = 0; y < h; y++) streaks[y] = (float)rng.NextDouble();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fy = (float)y / h;
                        float a = fy >= top[x] && fy <= bottom[x] ? 1f : 0f;
                        // 마른 붓 자국: 오른쪽으로 갈수록 가로 줄무늬로 비어 간다
                        float dry = Mathf.Clamp01((x - w * 0.78f) / (w * 0.22f)); // 끝 부분만 (제목 글자 뒤는 꽉 차게)
                        if (streaks[y] < dry * 0.7f) a *= 0.15f;
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * a * (0.85f + 0.15f * streaks[y])));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                return _brush = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        /// <summary>낙관(도장) 판: 가장자리가 거칠게 닳은 네모.</summary>
        public static Sprite Seal
        {
            get
            {
                if (_seal != null) return _seal;
                var loaded = FromResources("seal");
                if (loaded != null) return _seal = loaded;
                const int n = 96;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                var rng = new System.Random(9);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float edge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                        float a = Mathf.Clamp01((edge - 2f + (float)rng.NextDouble() * 3f) / 2f);
                        if (rng.NextDouble() < 0.04) a *= 0.5f; // 인주가 덜 묻은 점
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                return _seal = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        /// <summary>밝기 (0~1, 사람 눈 기준).</summary>
        public static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }
}

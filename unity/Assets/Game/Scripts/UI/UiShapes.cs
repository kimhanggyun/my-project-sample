using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>코드로 만든 기본 모양 스프라이트 (그림 파일 없이 인형·막대 등을 그릴 때). 한 번 만들어 같이 쓴다.</summary>
    public static class UiShapes
    {
        private static Sprite _circle;
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> RoundedCache = new System.Collections.Generic.Dictionary<int, Sprite>();

        /// <summary>테두리 없는 둥근 네모 (9-slice, 흰색). 색 테두리 카드·칩·꼬리표처럼 먹선 없이 둥글기만 필요할 때.</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Max(2, radius);
            if (RoundedCache.TryGetValue(radius, out var s)) return s;
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                    float cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return RoundedCache[radius] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>부드러운 가장자리의 흰 원 (색은 Image.color로).</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                float r = size / 2f - 1f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x - size / 2f + 0.5f) * (x - size / 2f + 0.5f) + (y - size / 2f + 0.5f) * (y - size / 2f + 0.5f));
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                return _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}

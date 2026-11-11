using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>코드로 만든 기본 모양 스프라이트 (그림 파일 없이 인형·막대 등을 그릴 때). 한 번 만들어 같이 쓴다.</summary>
    public static class UiShapes
    {
        private static Sprite _circle;

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

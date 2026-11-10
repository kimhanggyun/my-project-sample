using System.Collections.Generic;
using Game.Data.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Unity.UI
{
    /// <summary>
    /// 화면 스타일 (UI 1단계 임시, `Ui/styles.json`): 색(<see cref="Palette"/>), 모서리 둥글기, 글자 크기 배율.
    /// 시작할 때 한 번 적용한다 (바꾸면 다시 시작). 파일이 없거나 값이 없으면 코드 기본값 그대로.
    /// </summary>
    public static class UiStyle
    {
        public const string File = "Ui/styles.json";
        /// <summary>개발 빌드: 설정 화면에서 고른 스타일 (저장소 키).</summary>
        public const string OverrideKey = "dev.style";

        public static string Current { get; private set; } = "basic";
        public static float CornerRadius { get; private set; }
        public static float FontScale { get; private set; } = 1f;
        /// <summary>(id, 이름 키) 차례대로.</summary>
        public static List<KeyValuePair<string, string>> Available { get; } = new List<KeyValuePair<string, string>>();

        private static Sprite _rounded;
        private static float _roundedRadius = -1f;

        /// <param name="choice">고른 스타일 id (null이면 파일의 default).</param>
        public static void Apply(IDataSource data, string choice)
        {
            Available.Clear();
            if (data == null || !data.Exists(File)) return;
            var root = JObject.Parse(data.ReadText(File));
            if (!(root["styles"] is JObject styles)) return;
            foreach (var p in styles.Properties()) Available.Add(new KeyValuePair<string, string>(p.Name, (p.Value as JObject)?.Value<string>("nameKey") ?? p.Name));
            string id = choice != null && styles[choice] is JObject ? choice : root.Value<string>("default");
            if (id == null || !(styles[id] is JObject s)) return;
            Current = id;
            CornerRadius = Mathf.Max(0f, s.Value<float?>("cornerRadius") ?? 0f);
            FontScale = Mathf.Clamp(s.Value<float?>("fontScale") ?? 1f, 0.5f, 2f);
            if (s["colors"] is JObject c)
            {
                Palette.Background = Read(c, "background", Palette.Background);
                Palette.Bar = Read(c, "bar", Palette.Bar);
                Palette.Card = Read(c, "card", Palette.Card);
                Palette.Button = Read(c, "button", Palette.Button);
                Palette.ButtonActive = Read(c, "buttonActive", Palette.ButtonActive);
                Palette.ButtonDisabled = Read(c, "buttonDisabled", Palette.ButtonDisabled);
                Palette.Text = Read(c, "text", Palette.Text);
                Palette.TextDim = Read(c, "textDim", Palette.TextDim);
                Palette.Ally = Read(c, "ally", Palette.Ally);
                Palette.Enemy = Read(c, "enemy", Palette.Enemy);
                Palette.Good = Read(c, "good", Palette.Good);
                Palette.Bad = Read(c, "bad", Palette.Bad);
                Palette.Overlay = Read(c, "overlay", Palette.Overlay);
            }
        }

        private static Color Read(JObject c, string key, Color fallback) =>
            c.Value<string>(key) is string hex && ColorUtility.TryParseHtmlString(hex, out var col) ? col : fallback;

        /// <summary>둥근 모서리 판 (9-slice). 둥글기가 0이면 null (그냥 사각형).</summary>
        public static Sprite Rounded()
        {
            if (CornerRadius <= 0f) return null;
            if (_rounded != null && Mathf.Approximately(_roundedRadius, CornerRadius)) return _rounded;
            int r = Mathf.CeilToInt(CornerRadius);
            int size = r * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 가장 가까운 모서리 원 중심까지의 거리로 가장자리를 부드럽게
                    float cx = x < r ? r : (x >= size - r ? size - r - 1 : x);
                    float cy = y < r ? r : (y >= size - r ? size - r - 1 : y);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            _roundedRadius = CornerRadius;
            return _rounded;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Game.Data.Json;
using Game.Unity.Boot;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 부품 꾸미기 (docs/design/ui-kit.md): 부품 모양 = 테마 색 · 역할 · 변형(모양 종류) · 값.
    /// 목록(변형 · 조절값 · 역할 · 그림 id)은 data/Ui/uiKit.json, 값은 styles.json parts. 개발 빌드는 꾸미기 화면에서 바꾼 값(개발 값)을
    /// 이 기기에 저장해 데이터 위에 덮어 보고, [내보내기]로 styles.json에 쓴다 (에디터) · 클립보드로 (기기).
    /// 값이 없는 역할은 예전 모양 그대로 (예전 데이터와 화면이 바뀌지 않게).
    /// </summary>
    public static class UiKit
    {
        public const string CatalogFile = "Ui/uiKit.json";
        private const string DevKey = "dev.uikit.v1";

        public sealed class Field
        {
            public string Name, Type, Label, Group;
            public float Min, Max = 1, Step = 1;
            public readonly List<string> Options = new List<string>();
        }

        public sealed class Kind
        {
            public readonly List<string> Variants = new List<string>();
            public readonly List<Field> Fields = new List<Field>();
        }

        public sealed class Role { public string Id, Kind, Category, Label; }
        public sealed class ArtInfo { public string Id, Group, Label; }

        public static readonly List<(string id, string label)> Categories = new List<(string, string)>();
        public static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>();
        public static readonly List<Role> Roles = new List<Role>();
        public static readonly List<ArtInfo> Arts = new List<ArtInfo>();
        public static readonly List<string> ThemeColors = new List<string>();
        public static readonly Dictionary<string, string> TabIcons = new Dictionary<string, string>();

        /// <summary>참이면 개발 값을 빼고 데이터 값만 (꾸미기 화면 A/B 비교의 A).</summary>
        public static bool DataOnly;
        /// <summary>개발 값은 개발 빌드(에디터 포함)에서만.</summary>
        public static bool DevOn => Debug.isDebugBuild;

        private static JObject _devParts = new JObject(), _devColors = new JObject();
        private static readonly Dictionary<string, Color> DataColors = new Dictionary<string, Color>();

        // ---------------- 읽기 ----------------

        /// <summary>목록을 읽고 개발 값을 불러 테마 색에 덮는다. UiStyle.Apply 다음에 부른다.</summary>
        public static void Load(IDataSource data)
        {
            Categories.Clear(); Kinds.Clear(); Roles.Clear(); Arts.Clear(); ThemeColors.Clear(); TabIcons.Clear();
            if (data != null && data.Exists(CatalogFile))
            {
                try
                {
                    var root = JObject.Parse(data.ReadText(CatalogFile));
                    foreach (var c in root["themeColors"] as JArray ?? new JArray()) ThemeColors.Add((string)c);
                    foreach (var c in root["categories"] as JArray ?? new JArray()) Categories.Add(((string)c["id"], (string)c["label"]));
                    if (root["kinds"] is JObject kinds)
                        foreach (var kp in kinds.Properties())
                        {
                            var k = new Kind();
                            foreach (var v in kp.Value["variants"] as JArray ?? new JArray()) k.Variants.Add((string)v);
                            if (kp.Value["fields"] is JObject fields)
                                foreach (var fp in fields.Properties())
                                {
                                    var f = new Field { Name = fp.Name, Type = (string)fp.Value["type"], Label = (string)fp.Value["label"] ?? fp.Name, Group = (string)fp.Value["group"] };
                                    f.Min = fp.Value.Value<float?>("min") ?? 0; f.Max = fp.Value.Value<float?>("max") ?? 1; f.Step = fp.Value.Value<float?>("step") ?? 1;
                                    foreach (var o in fp.Value["options"] as JArray ?? new JArray()) f.Options.Add((string)o);
                                    k.Fields.Add(f);
                                }
                            Kinds[kp.Name] = k;
                        }
                    if (root["roles"] is JObject roles)
                        foreach (var rp in roles.Properties())
                            Roles.Add(new Role { Id = rp.Name, Kind = (string)rp.Value["kind"], Category = (string)rp.Value["category"], Label = (string)rp.Value["label"] ?? rp.Name });
                    if (root["arts"] is JObject arts)
                        foreach (var ap in arts.Properties()) Arts.Add(new ArtInfo { Id = ap.Name, Group = (string)ap.Value["group"], Label = (string)ap.Value["label"] ?? ap.Name });
                    if (root["tabIcons"] is JObject icons) foreach (var ip in icons.Properties()) TabIcons[ip.Name] = (string)ip.Value;
                }
                catch (Exception e) { Debug.LogWarning("uiKit.json: " + e.Message); }
            }
            DataColors.Clear();
            foreach (var n in ThemeColors) DataColors[n] = ThemeColor(n, Color.magenta);
            LoadDev();
            if (DevOn) foreach (var p in _devColors.Properties()) if (ColorUtility.TryParseHtmlString((string)p.Value, out var c)) SetThemeColor(p.Name, c);
            foreach (var a in Arts)
                if (a.Group != "frame" && !SegmentArt.Knows(a.Id) && Resources.Load<Sprite>("Ui/" + a.Id) == null) Debug.LogWarning("uiKit 그림 없음: " + a.Id);
        }

        public static Role RoleOf(string id) => Roles.Find(r => r.Id == id);
        public static Kind KindOf(string roleId) { var r = RoleOf(roleId); return r != null && Kinds.TryGetValue(r.Kind, out var k) ? k : null; }

        /// <summary>그 역할의 값 (페이지 덮어쓰기 → 데이터 → 개발 값). 없으면 빈 값.</summary>
        public static PartSpec Spec(string role)
        {
            string name = role;
            if (UiStyle.Page != null && UiStyle.Pages.TryGetValue(UiStyle.Page, out var map) && map.TryGetValue(role, out var over)) name = over;
            var o = new JObject();
            if (UiStyle.RawParts.TryGetValue(name, out var d)) foreach (var p in d.Properties()) o[p.Name] = p.Value.DeepClone();
            if (!DataOnly && DevOn && _devParts[name] is JObject dv) foreach (var p in dv.Properties()) o[p.Name] = p.Value.DeepClone();
            return new PartSpec(name, o);
        }

        /// <summary>색 값: 테마 색 이름 또는 #RRGGBB(AA). 못 읽으면 fallback.</summary>
        public static Color ColorOf(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (value[0] == '#') return ColorUtility.TryParseHtmlString(value, out var c) ? c : fallback;
            return ThemeColor(value, fallback);
        }

        public static Color ThemeColor(string name, Color fallback)
        {
            switch (name)
            {
                case "background": return Palette.Background;
                case "bar": return Palette.Bar;
                case "card": return Palette.Card;
                case "button": return Palette.Button;
                case "buttonActive": return Palette.ButtonActive;
                case "buttonDisabled": return Palette.ButtonDisabled;
                case "text": return Palette.Text;
                case "textDim": return Palette.TextDim;
                case "accentLight": return Palette.AccentLight;
                case "line": return Palette.Line;
                case "pictureBack": return Palette.PictureBack;
                case "track": return Palette.Track;
                case "board": return Palette.Board;
                case "cellOn": return Palette.CellOn;
                case "good": return Palette.Good;
                case "bad": return Palette.Bad;
                case "seal": return Palette.Seal;
                case "danger": return Palette.Danger;
                case "ally": return Palette.Ally;
                case "enemy": return Palette.Enemy;
                case "overlay": return Palette.Overlay;
                case "promptBack": return Palette.PromptBack;
                case "promptText": return Palette.PromptText;
                case "white": return Color.white;
                case "black": return Color.black;
                case "clear": return Color.clear;
            }
            return fallback;
        }

        private static void SetThemeColor(string name, Color c)
        {
            switch (name)
            {
                case "background": Palette.Background = c; break;
                case "bar": Palette.Bar = c; break;
                case "card": Palette.Card = c; break;
                case "button": Palette.Button = c; break;
                case "buttonActive": Palette.ButtonActive = c; break;
                case "buttonDisabled": Palette.ButtonDisabled = c; break;
                case "text": Palette.Text = c; break;
                case "textDim": Palette.TextDim = c; break;
                case "accentLight": Palette.AccentLight = c; break;
                case "line": Palette.Line = c; break;
                case "pictureBack": Palette.PictureBack = c; break;
                case "track": Palette.Track = c; break;
                case "board": Palette.Board = c; break;
                case "cellOn": Palette.CellOn = c; break;
                case "good": Palette.Good = c; break;
                case "bad": Palette.Bad = c; break;
                case "seal": Palette.Seal = c; break;
                case "danger": Palette.Danger = c; break;
                case "ally": Palette.Ally = c; break;
                case "enemy": Palette.Enemy = c; break;
                case "overlay": Palette.Overlay = c; break;
                case "promptBack": Palette.PromptBack = c; break;
                case "promptText": Palette.PromptText = c; break;
            }
        }

        /// <summary>바꿀 수 있는 테마 색인가 (white · black · clear 빼고).</summary>
        public static bool Editable(string themeColor) => themeColor != "white" && themeColor != "black" && themeColor != "clear";

        // ---------------- 개발 값 ----------------

        private static void LoadDev()
        {
            _devParts = new JObject();
            _devColors = new JObject();
            string s = null;
            try { s = PlayerPrefs.GetString(DevKey, ""); } catch { }
            if (string.IsNullOrEmpty(s)) return;
            try
            {
                var o = JObject.Parse(s);
                if (o["parts"] is JObject p) _devParts = p;
                if (o["colors"] is JObject c) _devColors = c;
            }
            catch { }
        }

        private static void SaveDev()
        {
            var o = new JObject { ["parts"] = _devParts, ["colors"] = _devColors };
            try { PlayerPrefs.SetString(DevKey, o.ToString(Newtonsoft.Json.Formatting.None)); PlayerPrefs.Save(); } catch { }
        }

        public static bool HasDev(string role) => _devParts[role] is JObject o && o.Count > 0;
        public static bool HasDev(string role, string key) => _devParts[role] is JObject o && o[key] != null;
        public static bool HasDevColor(string name) => _devColors[name] != null;
        public static int DevCount
        {
            get
            {
                int n = _devColors.Count;
                foreach (var p in _devParts.Properties()) if (p.Value is JObject o) n += o.Count;
                return n;
            }
        }

        /// <summary>개발 값 하나 (value null = 지움 → 데이터 값으로).</summary>
        public static void SetDev(string role, string key, JToken value)
        {
            if (!(_devParts[role] is JObject o)) { o = new JObject(); _devParts[role] = o; }
            if (value == null) o.Remove(key); else o[key] = value;
            if (o.Count == 0) _devParts.Remove(role);
            SaveDev();
        }

        public static void ResetDev(string role) { _devParts.Remove(role); SaveDev(); }

        public static void SetDevColor(string name, Color c)
        {
            _devColors[name] = "#" + ColorUtility.ToHtmlStringRGBA(c);
            SetThemeColor(name, c);
            SaveDev();
        }

        public static void ResetDevColor(string name)
        {
            _devColors.Remove(name);
            if (DataColors.TryGetValue(name, out var c)) SetThemeColor(name, c);
            SaveDev();
        }

        public static Color DataColor(string name) => DataColors.TryGetValue(name, out var c) ? c : ThemeColor(name, Color.magenta);

        public static void ResetAll()
        {
            foreach (var p in new List<JProperty>(_devColors.Properties())) ResetDevColor(p.Name);
            _devParts = new JObject();
            _devColors = new JObject();
            SaveDev();
        }

        /// <summary>
        /// 개발 값을 데이터로: 에디터는 저장소 data/Ui/styles.json에 써서 다시 읽고 개발 값을 비운다 (참 돌려줌),
        /// 기기는 바뀐 값 JSON을 클립보드로 (거짓). 어느 쪽이든 로그에 JSON.
        /// </summary>
        public static bool Export()
        {
            var change = new JObject { ["style"] = UiStyle.Current, ["parts"] = _devParts.DeepClone(), ["colors"] = _devColors.DeepClone() };
            Debug.Log("부품 꾸미기 내보내기: " + change.ToString(Newtonsoft.Json.Formatting.None));
#if UNITY_EDITOR
            string path = Path.Combine(GameDataPaths.RepoRoot, "data", UiStyle.File);
            string text = File.ReadAllText(path);
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var root = JObject.Parse(text);
            if (!(root["parts"] is JObject parts)) { parts = new JObject(); root["parts"] = parts; }
            foreach (var p in _devParts.Properties())
            {
                if (!(parts[p.Name] is JObject target)) { target = new JObject(); parts[p.Name] = target; }
                foreach (var kv in ((JObject)p.Value).Properties()) target[kv.Name] = kv.Value.DeepClone();
            }
            if (_devColors.Count > 0 && root["styles"]?[UiStyle.Current]?["colors"] is JObject cols)
                foreach (var p in _devColors.Properties())
                {
                    string hex = (string)p.Value;
                    if (hex != null && hex.Length == 9 && hex.EndsWith("FF")) hex = hex.Substring(0, 7);
                    cols[p.Name] = hex;
                }
            File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n").Replace("\n", nl) + nl);
            _devParts = new JObject();
            _devColors = new JObject();
            SaveDev();
            var data = GameDataPaths.Data();
            UiStyle.Apply(data, UiStyle.Current);
            Load(data);
            return true;
#else
            GUIUtility.systemCopyBuffer = change.ToString(Newtonsoft.Json.Formatting.Indented);
            return false;
#endif
        }

        // ---------------- 그림 (테두리 · 고리 · 번짐 · 점선) ----------------

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>둥근 사각 고리 (9-slice, 가운데 빔). 테두리.</summary>
        public static Sprite Ring(int radius, int width)
        {
            radius = Mathf.Max(radius, width + 2);
            string key = "ring" + radius + "_" + width;
            if (SpriteCache.TryGetValue(key, out var s)) return s;
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                    float cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float depth = radius - d;
                    float a = Mathf.Clamp01(depth + 0.5f) * Mathf.Clamp01(width - depth + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SpriteCache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>장식 테두리 (Tiled 9-slice, 가운데 빔): frame.arrowTwist (화살 두 가닥이 꼬임 + 모서리 화살촉) · frame.knot (두 줄 + 모서리 매듭 고리).</summary>
        public static Sprite FrameTile(string id)
        {
            if (SpriteCache.TryGetValue(id, out var s)) return s;
            var loaded = Resources.Load<Sprite>("Ui/" + id);
            if (loaded != null) return SpriteCache[id] = loaded;
            const int b = 16, p = 24, size = b * 2 + p;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            bool twist = id != "frame.knot";
            float Band(float along, float across)
            {
                if (twist)
                {
                    float c = b * 0.5f, amp = b * 0.24f, t = (along - b) / p * Mathf.PI * 2f;
                    float s1 = c + amp * Mathf.Sin(t), s2 = c - amp * Mathf.Sin(t);
                    const float w = 1.6f;
                    return Mathf.Max(Mathf.Clamp01(w - Mathf.Abs(across - s1) + 0.5f), Mathf.Clamp01(w - Mathf.Abs(across - s2) + 0.5f));
                }
                return Mathf.Max(Mathf.Clamp01(1.2f - Mathf.Abs(across - b * 0.3f) + 0.5f), Mathf.Clamp01(1.2f - Mathf.Abs(across - b * 0.62f) + 0.5f));
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    if (y < b) a = Mathf.Max(a, Band(x, y));
                    if (y >= size - b) a = Mathf.Max(a, Band(x, size - 1 - y));
                    if (x < b) a = Mathf.Max(a, Band(y, x));
                    if (x >= size - b) a = Mathf.Max(a, Band(y, size - 1 - x));
                    // 모서리: 화살촉 마름모 / 매듭 고리
                    float qx = x < b ? x : (x >= size - b ? size - 1 - x : -100), qy = y < b ? y : (y >= size - b ? size - 1 - y : -100);
                    if (qx > -50 && qy > -50)
                    {
                        float dx = qx - b * 0.5f, dy = qy - b * 0.5f;
                        if (twist) a = Mathf.Max(a, Mathf.Clamp01(5.5f - (Mathf.Abs(dx) + Mathf.Abs(dy)) + 0.5f));
                        else
                        {
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            a = Mathf.Max(a, Mathf.Max(Mathf.Clamp01(1.5f - Mathf.Abs(r - b * 0.36f) + 0.5f), Mathf.Clamp01(2f - r + 0.5f)));
                            if (qx < b * 0.62f + 1 && qy < b * 0.62f + 1 && r < b * 0.36f - 1.5f) a = Mathf.Max(a, Mathf.Clamp01(2f - r + 0.5f));
                        }
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SpriteCache[id] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>위 → 아래 색 번짐.</summary>
        public static Sprite Gradient(Color top, Color bottom)
        {
            string key = "grad" + ColorUtility.ToHtmlStringRGBA(top) + ColorUtility.ToHtmlStringRGBA(bottom);
            if (SpriteCache.TryGetValue(key, out var s)) return s;
            const int h = 64;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[2 * h];
            for (int y = 0; y < h; y++) { var c = Color.Lerp(bottom, top, y / (h - 1f)); px[y * 2] = c; px[y * 2 + 1] = c; }
            tex.SetPixels(px);
            tex.Apply();
            return SpriteCache[key] = Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>가로 점선 한 칸 (14 칠 · 14 빔, Tiled로 늘어놓음).</summary>
        public static Sprite Dash
        {
            get
            {
                if (SpriteCache.TryGetValue("dash", out var s)) return s;
                var tex = new Texture2D(28, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
                var px = new Color32[28 * 4];
                for (int y = 0; y < 4; y++) for (int x = 0; x < 28; x++) px[y * 28 + x] = new Color32(255, 255, 255, (byte)(x < 14 ? 255 : 0));
                tex.SetPixels32(px);
                tex.Apply();
                return SpriteCache["dash"] = Sprite.Create(tex, new Rect(0, 0, 28, 4), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // ---------------- 그리기 도우미 ----------------

        private static void Remove(Transform t, string name)
        {
            var old = t.Find(name);
            if (old == null) return;
            old.SetParent(null, false);
            UnityEngine.Object.Destroy(old.gameObject);
        }

        private static Image Layer(Transform parent, string name, Sprite sprite, Color color, Image.Type type = Image.Type.Simple, int index = 0)
        {
            Remove(parent, name);
            var rt = Ui.Fill(Ui.Rect(parent, name));
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>그림 배경의 세 색: 바탕보다 조금 밝은 실루엣 + 강조 깃.</summary>
        public static Sprite SceneOn(string art, Color back, float blur) =>
            SegmentArt.Art(art, Color.Lerp(back, Palette.TextDim, 0.45f), Color.Lerp(back, Palette.TextDim, 0.25f), Color.Lerp(back, Palette.AccentLight, 0.6f), blur);

        /// <summary>
        /// 바탕 (bg.*): none · solid · texture(한지 무늬) · gradient · scene(흐린 그림). 값이 없으면 그대로 둔다.
        /// target에 Image가 있으면 그 색을 투명으로 하고 맨 뒤에 바탕 층(KitBg)을 깐다.
        /// </summary>
        public static void Backdrop(RectTransform target, string role, Color fallback)
        {
            var spec = Spec(role);
            Remove(target, "KitBg");
            if (spec.IsEmpty) return;
            var own = target.GetComponent<Image>();
            if (own != null) { own.color = new Color(0, 0, 0, 0); own.sprite = null; }
            string v = spec.Variant ?? "solid";
            Color c = spec.Col("color", fallback);
            var layer = Ui.Fill(Ui.Rect(target, "KitBg"));
            layer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            layer.SetAsFirstSibling();
            if (v == "none") return;
            var img = layer.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = c;
            if (v == "texture") { img.sprite = UiArt.Paper; img.type = Image.Type.Tiled; }
            else if (v == "gradient") { img.color = Color.white; img.sprite = Gradient(c, spec.Col("color2", Color.Lerp(c, Color.black, 0.35f))); }
            else if (v == "scene")
            {
                layer.gameObject.AddComponent<RectMask2D>();
                var art = SceneOn(spec.Str("art", "scene.army"), c, spec.Num("blur", 0.4f));
                if (art == null) return;
                var pic = Ui.Rect(layer, "Art");
                string align = spec.Str("align", "bottom");
                float py = align == "top" ? 1f : align == "middle" ? 0.5f : 0f;
                pic.anchorMin = Vector2.zero;
                pic.anchorMax = Vector2.one;
                pic.pivot = new Vector2(0.5f, py);
                pic.offsetMin = pic.offsetMax = Vector2.zero;
                var pi = pic.gameObject.AddComponent<Image>();
                pi.sprite = art;
                pi.color = new Color(1, 1, 1, spec.Num("alpha", 0.35f));
                pi.raycastTarget = false;
                var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectRatio = art.rect.width / art.rect.height;
                fit.aspectMode = spec.Str("fit", "fit") == "fill" ? AspectRatioFitter.AspectMode.EnvelopeParent : AspectRatioFitter.AspectMode.FitInParent;
            }
        }

        /// <summary>
        /// 버튼 모양 입히기 (역할 button.* · 예전 Styled 역할): 색 · 글자색 · 높이 · 글자 크기, 변형 square · round · pill · icon · iconText · framed · scene, 테두리.
        /// 값이 없으면 그대로.
        /// </summary>
        public static Button DressButton(Button b, string role)
        {
            if (b == null) return b;
            var spec = Spec(role);
            if (spec.IsEmpty) return b;
            var img = b.targetGraphic as Image;
            var label = b.GetComponentInChildren<Text>(true);
            var le = b.GetComponent<LayoutElement>();
            if (spec.Has("height") && le != null) le.minHeight = le.preferredHeight = spec.Num("height", 0);
            if (img != null && spec.Has("color")) img.color = spec.Col("color", img.color);
            if (label != null)
            {
                if (spec.Has("textColor")) label.color = spec.Col("textColor", label.color);
                if (spec.Has("fontSize")) label.fontSize = Mathf.RoundToInt(spec.Num("fontSize", label.fontSize) * UiStyle.FontScale);
            }
            var kind = RoleOf(role)?.Kind ?? RoleOf(spec.Name)?.Kind;
            if (kind != "button" || img == null) return b;
            float h = le != null && le.preferredHeight > 0 ? le.preferredHeight : 100f;
            string v = spec.Variant;
            int radius = Mathf.RoundToInt(spec.Num("radius", Mathf.Max(UiStyle.CornerRadius, 12)));
            switch (v)
            {
                case "square": img.sprite = null; img.type = Image.Type.Simple; break;
                case "round": case "icon": case "iconText": case "scene": img.sprite = UiShapes.Rounded(Mathf.Max(2, radius)); img.type = Image.Type.Sliced; break;
                case "pill": radius = Mathf.RoundToInt(h / 2f); img.sprite = UiShapes.Rounded(radius); img.type = Image.Type.Sliced; break;
                case "framed":
                    {
                        string frame = spec.Str("frame", "frame.arrowTwist");
                        if (frame == "frame.ink") { img.sprite = UiArt.Frame(Mathf.Max(6, radius)); img.type = Image.Type.Sliced; }
                        else
                        {
                            img.sprite = UiShapes.Rounded(Mathf.Max(2, radius)); img.type = Image.Type.Sliced;
                            var f = Layer(b.transform, "KitFrame", FrameTile(frame), spec.Col("border", Palette.AccentLight), Image.Type.Tiled);
                            f.fillCenter = false;
                            float bw = spec.Num("borderWidth", 4);
                            f.pixelsPerUnitMultiplier = Mathf.Clamp(4f / Mathf.Max(1f, bw), 0.4f, 2f);
                        }
                        break;
                    }
            }
            float border = spec.Num("borderWidth", 0);
            if (border > 0 && v != "framed")
            {
                var ring = Layer(b.transform, "KitRing", Ring(v == "square" ? 2 : radius, Mathf.RoundToInt(border)), spec.Col("border", Palette.AccentLight), Image.Type.Sliced);
                ring.fillCenter = true;
            }
            else Remove(b.transform, "KitRing");
            Color fore = label != null ? label.color : Palette.Text;
            if (v == "icon" || v == "iconText")
            {
                var sprite = SegmentArt.Icon(spec.Str("icon", "ico.battle"));
                Remove(b.transform, "KitIcon");
                if (sprite != null)
                {
                    var icon = Ui.Rect(b.transform, "KitIcon");
                    icon.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    float sz = h * (v == "icon" ? 0.64f : 0.5f);
                    icon.anchorMin = icon.anchorMax = new Vector2(v == "icon" ? 0.5f : 0f, 0.5f);
                    icon.pivot = new Vector2(v == "icon" ? 0.5f : 0f, 0.5f);
                    icon.sizeDelta = new Vector2(sz, sz);
                    icon.anchoredPosition = new Vector2(v == "icon" ? 0 : h * 0.3f, 0);
                    var ii = icon.gameObject.AddComponent<Image>();
                    ii.sprite = sprite; ii.preserveAspect = true; ii.color = fore; ii.raycastTarget = false;
                    if (label != null)
                    {
                        if (v == "icon") label.enabled = false;
                        else label.rectTransform.offsetMin = new Vector2(h * 0.3f + sz + 8, label.rectTransform.offsetMin.y);
                    }
                }
            }
            if (v == "scene")
            {
                var art = SceneOn(spec.Str("art", "scene.flags"), img.color, 0.3f);
                if (art != null)
                {
                    var layer = Layer(b.transform, "KitArt", art, new Color(1, 1, 1, spec.Num("artAlpha", 0.6f)));
                    layer.preserveAspect = true;
                    b.gameObject.AddComponent<RectMask2D>();
                }
            }
            if (label != null) label.transform.SetAsLastSibling();
            return b;
        }

        /// <summary>뒤로가기 (button.back): chevron ‹ · bow (활을 뒤로가기 모양으로) · circle (둥근 ‹ + 테두리).</summary>
        public static Button BackButton(Transform parent, Action onBack, float size)
        {
            var spec = Spec("button.back");
            string v = spec.Variant ?? "chevron";
            float s = spec.Num("size", size);
            var b = Ui.Button(parent, v == "bow" ? "" : "‹", onBack, s, spec.Col("color", Palette.Button), 40);
            Ui.Size(b, s, s + 6);
            var label = b.GetComponentInChildren<Text>();
            Color fore = spec.Col("textColor", label.color);
            label.color = fore;
            var img = (Image)b.targetGraphic;
            if (v == "circle")
            {
                img.sprite = UiShapes.Rounded(Mathf.RoundToInt(s / 2f)); img.type = Image.Type.Sliced;
                Layer(b.transform, "KitRing", Ring(Mathf.RoundToInt(s / 2f), 3), Palette.AccentLight, Image.Type.Sliced);
                label.transform.SetAsLastSibling();
            }
            else if (v == "bow")
            {
                var icon = Ui.Rect(b.transform, "KitIcon");
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
                icon.sizeDelta = new Vector2(s * 0.72f, s * 0.72f);
                var ii = icon.gameObject.AddComponent<Image>();
                ii.sprite = SegmentArt.Icon("ico.bow"); ii.preserveAspect = true; ii.color = fore; ii.raycastTarget = false;
            }
            return b;
        }

        /// <summary>출진 버튼 (button.sortie): 주요 버튼 + plain · run (말이 달리고 길 무늬가 흐름) · clash (검과 검이 맞부딪쳐 불꽃).</summary>
        public static Button SortieButton(Transform parent, string text, Action onClick, float height)
        {
            var b = Ui.PrimaryButton(parent, text, onClick, height);
            var spec = Spec("button.sortie");
            if (spec.IsEmpty) return b;
            var img = (Image)b.targetGraphic;
            img.color = spec.Col("color", img.color);
            int radius = Mathf.RoundToInt(spec.Num("radius", Mathf.Max(UiStyle.CornerRadius, 12)));
            img.sprite = UiShapes.Rounded(Mathf.Max(2, radius)); img.type = Image.Type.Sliced;
            Color fx = spec.Col("border", Palette.AccentLight);
            float bw = spec.Num("borderWidth", 0);
            if (bw > 0) Layer(b.transform, "KitRing", Ring(radius, Mathf.RoundToInt(bw)), fx, Image.Type.Sliced);
            string v = spec.Variant ?? "plain";
            var label = b.GetComponentInChildren<Text>();
            if (v == "plain") { label.transform.SetAsLastSibling(); return b; }
            b.gameObject.AddComponent<RectMask2D>();
            var anim = b.gameObject.AddComponent<KitAnim>();
            anim.Mode = v;
            anim.Speed = spec.Num("speed", 1f);
            anim.OnPress = spec.Str("when", "always") == "press";
            if (v == "run")
            {
                var road = Ui.Rect(b.transform, "KitRoad");
                road.anchorMin = new Vector2(0f, 0f); road.anchorMax = new Vector2(1f, 0f);
                road.pivot = new Vector2(0f, 0f);
                road.offsetMin = new Vector2(0f, 10f); road.offsetMax = new Vector2(56f, 14f);
                var ri = road.gameObject.AddComponent<Image>();
                ri.sprite = Dash; ri.type = Image.Type.Tiled; ri.color = new Color(fx.r, fx.g, fx.b, 0.45f); ri.raycastTarget = false;
                anim.Road = road;
                var horse = Ui.Rect(b.transform, "KitHorse");
                horse.anchorMin = horse.anchorMax = new Vector2(0f, 0.5f);
                horse.pivot = new Vector2(0.5f, 0.5f);
                horse.sizeDelta = new Vector2(height * 0.62f, height * 0.62f);
                var hi = horse.gameObject.AddComponent<Image>();
                hi.sprite = SegmentArt.Icon("ico.horse"); hi.preserveAspect = true; hi.color = fx; hi.raycastTarget = false;
                anim.Runner = horse;
            }
            else if (v == "clash")
            {
                RectTransform Sword(string name, float x)
                {
                    var s = Ui.Rect(b.transform, name);
                    s.anchorMin = s.anchorMax = new Vector2(0.5f, 0f);
                    s.pivot = new Vector2(0.5f, 0.05f);
                    s.sizeDelta = new Vector2(height * 0.8f, height * 0.8f);
                    s.anchoredPosition = new Vector2(x, height * 0.08f);
                    var si = s.gameObject.AddComponent<Image>();
                    si.sprite = SegmentArt.Icon("ico.sword"); si.preserveAspect = true; si.color = fx; si.raycastTarget = false;
                    return s;
                }
                anim.SwordL = Sword("KitSwordL", -height * 0.55f);
                anim.SwordR = Sword("KitSwordR", height * 0.55f);
                var spark = Ui.Rect(b.transform, "KitSpark");
                spark.anchorMin = spark.anchorMax = new Vector2(0.5f, 0.5f);
                spark.sizeDelta = new Vector2(height * 0.9f, height * 0.9f);
                spark.anchoredPosition = new Vector2(0f, height * 0.12f);
                anim.Spark = spark.gameObject.AddComponent<Image>();
                anim.Spark.sprite = SegmentArt.Glow; anim.Spark.color = new Color(fx.r, fx.g, fx.b, 0f); anim.Spark.raycastTarget = false;
            }
            label.transform.SetAsLastSibling();
            return b;
        }

        /// <summary>
        /// 탭 줄 (tabs.*): button (판 버튼) · underline (밑줄) · art (흐린 그림 + 가운데 글자, 메인 세그먼트) · iconText (아이콘 위 · 글자 아래)
        /// · group (묶음: 둥근 테두리 하나 안에서 고른 칸만 알약으로 채움, 2026-11-27 사용자 참고 그림 — 그림이 있는 칸은 그림도).
        /// 칸마다 Button과 고름 표시 함수를 돌려준다 (메인 아래 탭이 고름을 바꿀 때).
        /// </summary>
        public static Dictionary<string, (Button button, Action<bool> mark)> TabStrip(Transform parent, IList<(string id, string label)> items, string selected,
            Action<string> onSelect, string role, string defVariant, float height, float width, int spacing = 18, Func<string, string> iconOf = null)
        {
            var spec = Spec(role);
            string v = spec.Variant ?? defVariant;
            float h = spec.Num("height", height);
            int font = Mathf.RoundToInt(spec.Num("fontSize", v == "art" ? 34 : v == "iconText" ? 22 : 28) * UiStyle.FontScale);
            Color back = spec.Col("color", v == "underline" || v == "iconText" ? new Color(0, 0, 0, 0) : v == "art" || v == "group" ? Palette.Card : Palette.Button);
            Color onBack = spec.Col("onColor", v == "underline" || v == "iconText" ? new Color(0, 0, 0, 0) : Palette.ButtonActive);
            Color onText = spec.Col("onText", v == "iconText" || v == "underline" ? Palette.AccentLight : Color.white);
            Color line = spec.Col("border", v == "group" ? Palette.ButtonActive : Palette.AccentLight);
            Color offText = Color.Lerp(Palette.Text, Palette.TextDim, v == "button" ? 0f : 0.35f);
            var result = new Dictionary<string, (Button, Action<bool>)>();
            var row = parent as RectTransform;
            if (parent.GetComponent<HorizontalLayoutGroup>() == null)
            {
                row = Ui.Row(parent, "TabStrip", h, spacing);
                var hl = row.GetComponent<HorizontalLayoutGroup>();
                hl.childForceExpandWidth = false;
                hl.childAlignment = TextAnchor.MiddleCenter;
            }
            float ch = h;
            if (v == "group")
            {
                // 묶음: 칸들을 감싸는 둥근 띠 (바탕 + 테두리), 칸 사이 틈 없음
                const int pad = 5;
                var group = Ui.Rect(row, "Group");
                Ui.Size(group, h, items.Count * width + pad * 2).flexibleWidth = 0;
                var gi = group.gameObject.AddComponent<Image>();
                gi.sprite = UiShapes.Rounded(Mathf.RoundToInt(h / 2f)); gi.type = Image.Type.Sliced; gi.color = back; gi.raycastTarget = false;
                var ring = Layer(group, "KitRing", Ring(Mathf.RoundToInt(h / 2f), Mathf.RoundToInt(spec.Num("borderWidth", 3))), line, Image.Type.Sliced);
                ring.fillCenter = true;
                var gl = group.gameObject.AddComponent<HorizontalLayoutGroup>();
                gl.padding = new RectOffset(pad, pad, pad, pad);
                gl.spacing = 0;
                gl.childControlWidth = gl.childControlHeight = true;
                gl.childForceExpandWidth = gl.childForceExpandHeight = false;
                gl.childAlignment = TextAnchor.MiddleLeft;
                row = group;
                ch = h - pad * 2;
            }
            foreach (var it in items)
            {
                var item = it;
                var cell = Ui.Rect(row, "Tab_" + item.id);
                Ui.Size(cell, ch, width).flexibleWidth = 0;
                int r = Mathf.Max(2, Mathf.RoundToInt(UiStyle.CornerRadius > 0 ? UiStyle.CornerRadius : 12));
                var frame = Ui.Background(cell, back);
                if (v == "art" || v == "button") { frame.sprite = UiShapes.Rounded(r); frame.type = Image.Type.Sliced; }
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = frame;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onSelect?.Invoke(item.id); });
                var fill = Ui.Fill(Ui.Rect(cell, "Fill"));
                if (v == "art") { fill.offsetMin = new Vector2(4, 4); fill.offsetMax = new Vector2(-4, -4); }
                var fillImg = fill.gameObject.AddComponent<Image>();
                fillImg.raycastTarget = false;
                if (v == "art" || v == "button") { fillImg.sprite = UiShapes.Rounded(r); fillImg.type = Image.Type.Sliced; }
                if (v == "group") { fillImg.sprite = UiShapes.Rounded(Mathf.Max(2, Mathf.RoundToInt(ch / 2f))); fillImg.type = Image.Type.Sliced; }
                Image art = null, glow = null, icon = null;
                if ((v == "art" || v == "group") && SegmentArt.Has(item.id))
                {
                    var pic = Ui.Fill(Ui.Rect(fill, "Art"));
                    pic.offsetMin = new Vector2(6, 2); pic.offsetMax = new Vector2(-6, -2);
                    art = pic.gameObject.AddComponent<Image>();
                    art.preserveAspect = true; art.raycastTarget = false;
                    var g = Ui.Rect(fill, "Glow");
                    g.anchorMin = new Vector2(0.3f, 0.22f); g.anchorMax = new Vector2(0.7f, 0.78f);
                    g.offsetMin = g.offsetMax = Vector2.zero;
                    glow = g.gameObject.AddComponent<Image>();
                    glow.sprite = SegmentArt.Glow; glow.raycastTarget = false;
                }
                string iconId = v == "iconText" ? iconOf?.Invoke(item.id) : null;
                var iconSprite = iconId != null ? SegmentArt.Icon(iconId) : null;
                if (iconSprite != null)
                {
                    var ic = Ui.Rect(fill, "Icon");
                    ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 1f);
                    ic.pivot = new Vector2(0.5f, 1f);
                    ic.sizeDelta = new Vector2(h * 0.44f, h * 0.44f);
                    ic.anchoredPosition = new Vector2(0, -h * 0.1f);
                    icon = ic.gameObject.AddComponent<Image>();
                    icon.sprite = iconSprite; icon.preserveAspect = true; icon.raycastTarget = false;
                }
                var t = Ui.Label(fill, item.label, font, TextAnchor.MiddleCenter, offText);
                Ui.Fill(t.rectTransform);
                if (iconSprite != null) { t.rectTransform.anchorMax = new Vector2(1f, 0.45f); t.alignment = TextAnchor.MiddleCenter; }
                Outline o = null;
                if (v == "group") t.fontStyle = FontStyle.Bold;
                if (art != null || v == "art") { t.fontStyle = FontStyle.Bold; o = t.gameObject.AddComponent<Outline>(); o.effectDistance = new Vector2(2f, -2f); }
                var under = Ui.Rect(fill, "Under");
                under.anchorMin = under.anchorMax = new Vector2(0.5f, 0f);
                under.pivot = new Vector2(0.5f, 0f);
                under.sizeDelta = new Vector2(v == "underline" ? width * 0.6f : 40f, 5f);
                under.anchoredPosition = new Vector2(0, v == "art" ? 8 : 4);
                var underImg = under.gameObject.AddComponent<Image>();
                underImg.color = line; underImg.raycastTarget = false;
                string id = item.id;
                void Mark(bool on)
                {
                    frame.color = v == "art" ? (on ? line : Palette.Line) : new Color(0, 0, 0, 0);
                    fillImg.color = on ? onBack : v == "group" ? new Color(0, 0, 0, 0) : back;
                    t.color = on ? onText : offText;
                    under.gameObject.SetActive(on && v != "button" && v != "group");
                    if (art != null)
                    {
                        art.sprite = SegmentArt.Get(id, on, onBack);
                        art.color = new Color(1, 1, 1, spec.Num("artAlpha", 1f));
                    }
                    var shade = on ? Color.Lerp(Color.black, onBack, 0.45f) : Color.Lerp(Color.black, back, 0.6f);
                    if (glow != null) glow.color = new Color(shade.r, shade.g, shade.b, spec.Num("glow", 0.45f));
                    if (o != null) o.effectColor = new Color(shade.r, shade.g, shade.b, 0.9f);
                    if (icon != null) icon.color = on ? onText : offText;
                }
                Mark(item.id == selected);
                result[item.id] = (b, Mark);
            }
            return result;
        }
    }

    /// <summary>부품 하나의 값 (styles.json parts + 개발 값).</summary>
    public sealed class PartSpec
    {
        public readonly string Name;
        private readonly JObject _o;
        public PartSpec(string name, JObject o) { Name = name; _o = o ?? new JObject(); }
        public bool IsEmpty => _o.Count == 0;
        public bool Has(string k) => _o[k] != null && _o[k].Type != JTokenType.Null;
        public string Variant => Str("variant", null);
        public string Str(string k, string d) => Has(k) ? (string)_o[k] : d;
        public float Num(string k, float d) => Has(k) && (_o[k].Type == JTokenType.Integer || _o[k].Type == JTokenType.Float) ? (float)_o[k] : d;
        public Color Col(string k, Color d) => Has(k) ? UiKit.ColorOf((string)_o[k], d) : d;
        public JToken Raw(string k) => _o[k];
    }

    /// <summary>부품 움직임 (출진 버튼 말 달림 · 검 부딪침). 화면 시간(unscaled)으로.</summary>
    public sealed class KitAnim : MonoBehaviour, IPointerDownHandler
    {
        public string Mode;
        public float Speed = 1f;
        public bool OnPress;
        public RectTransform Runner, Road, SwordL, SwordR;
        public Image Spark;
        private float _pressed = -99f;

        public void OnPointerDown(PointerEventData e) { _pressed = Time.unscaledTime; }

        private void Update()
        {
            bool active = !OnPress || Time.unscaledTime - _pressed < 1.6f / Mathf.Max(0.1f, Speed);
            float t = Time.unscaledTime * Speed;
            float w = ((RectTransform)transform).rect.width;
            if (Road != null && active) Road.anchoredPosition = new Vector2(-((t * 60f) % 28f), Road.anchoredPosition.y);
            if (Runner != null)
            {
                Runner.gameObject.SetActive(active);
                float u = (t * 0.4f) % 1f;
                Runner.anchoredPosition = new Vector2(-60f + u * (w + 120f), Mathf.Abs(Mathf.Sin(t * 9f)) * 6f);
            }
            if (SwordL != null && SwordR != null)
            {
                float c = (t * 0.7f) % 1f;
                float e = active ? (c < 0.5f ? Mathf.SmoothStep(0f, 1f, c * 2f) : Mathf.SmoothStep(1f, 0f, (c - 0.5f) * 2f)) : 0f;
                float ang = Mathf.Lerp(50f, -32f, e);
                SwordL.localRotation = Quaternion.Euler(0, 0, ang);
                SwordR.localRotation = Quaternion.Euler(0, 0, -ang);
                if (Spark != null)
                {
                    float hit = active ? Mathf.Clamp01(1f - Mathf.Abs(c - 0.5f) * 8f) : 0f;
                    var col = Spark.color; col.a = hit; Spark.color = col;
                    Spark.rectTransform.localScale = Vector3.one * (0.6f + hit * 0.6f);
                }
            }
        }
    }
}

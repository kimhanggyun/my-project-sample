using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 임시 3D 아트 목록 (Resources/Art/catalog.json, docs/impl/unity/art.md).
    /// 키(예: soldier.cavalry, prop.forest) → 모델(Resources 경로)·목표 높이·방향. 모델이 없으면 TryGet이 false → 화면은 단순 도형을 쓴다.
    /// 모델은 크기를 목표 높이로 맞추고, 바닥을 땅에, 가운데를 원점에 둔다 (에셋마다 단위·원점이 달라도 그대로 쓸 수 있게).
    /// </summary>
    public sealed class ArtCatalog
    {
        public const string CatalogPath = "Art/catalog";

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly Dictionary<string, ArtModel> _loaded = new Dictionary<string, ArtModel>();
        private readonly System.Func<Color, Material> _fallbackMaterial;
        private readonly List<Object> _owned = new List<Object>();

        private sealed class Entry
        {
            public List<string> Models = new List<string>();
            public float Height = 1f;
            public float Yaw;
            public float Tint = 0.6f;
            public float PerCell = 1f;
            public float Jitter = 0.15f;
        }

        /// <param name="fallbackMaterial">모델 재질의 셰이더를 쓸 수 없을 때 대신할 기본 재질 (색 지정).</param>
        public ArtCatalog(System.Func<Color, Material> fallbackMaterial)
        {
            _fallbackMaterial = fallbackMaterial;
            var text = Resources.Load<TextAsset>(CatalogPath);
            if (text == null) return;
            try
            {
                foreach (var p in ((JObject)JObject.Parse(text.text)["models"] ?? new JObject()).Properties())
                {
                    if (p.Name.StartsWith("_") || !(p.Value is JObject o)) continue;
                    var e = new Entry
                    {
                        Height = o.Value<float?>("height") ?? 1f,
                        Yaw = o.Value<float?>("yaw") ?? 0f,
                        Tint = o.Value<float?>("tint") ?? 0.6f,
                        PerCell = o.Value<float?>("perCell") ?? 1f,
                        Jitter = o.Value<float?>("jitter") ?? 0.15f,
                    };
                    if (o["model"] is JValue one) e.Models.Add(one.Value<string>());
                    if (o["models"] is JArray many) foreach (var m in many) e.Models.Add(m.Value<string>());
                    _entries[p.Name] = e;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("아트 목록을 읽지 못해 단순 도형을 씁니다: " + ex.Message); _entries.Clear(); }
        }

        /// <summary>키의 모델들 (여러 개면 변형). 하나도 없으면 false.</summary>
        public bool TryGet(string key, out List<ArtModel> models, out float perCell, out float jitter)
        {
            models = null;
            perCell = 1f;
            jitter = 0f;
            if (key == null || !_entries.TryGetValue(key, out var e)) return false;
            perCell = e.PerCell;
            jitter = e.Jitter;
            foreach (var path in e.Models)
            {
                var m = Load(path, e);
                if (m == null) continue;
                (models ?? (models = new List<ArtModel>())).Add(m);
            }
            return models != null;
        }

        public bool TryGet(string key, out ArtModel model)
        {
            model = TryGet(key, out var list, out _, out _) ? list[0] : null;
            return model != null;
        }

        /// <summary>모델의 팀 색 섞는 비율 (0 = 원래 색, 1 = 팀 색).</summary>
        public float TintOf(string key) => key != null && _entries.TryGetValue(key, out var e) ? e.Tint : 0.6f;

        private ArtModel Load(string path, Entry e)
        {
            string cacheKey = path + "|" + e.Height + "|" + e.Yaw;
            if (_loaded.TryGetValue(cacheKey, out var cached)) return cached;
            ArtModel model = null;
            var prefab = string.IsNullOrEmpty(path) ? null : Resources.Load<GameObject>(path);
            var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf != null && mf.sharedMesh != null)
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = new Material[mesh.subMeshCount];
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mr != null && mr.sharedMaterials.Length > 0 ? mr.sharedMaterials[Mathf.Min(i, mr.sharedMaterials.Length - 1)] : null;
                    mats[i] = Own(Usable(src));
                }
                // 프리팹 안 메시의 위치·회전·크기 → 방향 → 크기를 목표 높이로, 바닥 = 0, 가운데 = 원점
                var inPrefab = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var turned = Matrix4x4.Rotate(Quaternion.Euler(0f, e.Yaw, 0f)) * inPrefab;
                var b = Transform(mesh.bounds, turned);
                float s = b.size.y > 1e-5f ? e.Height / b.size.y : 1f;
                var local = Matrix4x4.Translate(new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s)) * Matrix4x4.Scale(Vector3.one * s) * turned;
                model = new ArtModel(mesh, mats, local);
            }
            else if (!string.IsNullOrEmpty(path)) Debug.Log("임시 아트 모델이 없어 단순 도형을 씁니다: Resources/" + path);
            _loaded[cacheKey] = model;
            return model;
        }

        /// <summary>인스턴싱을 켠 사본. 셰이더가 없거나 이 기기에서 못 쓰면(분홍) 기본 재질에 텍스처·색만 옮긴다.</summary>
        private Material Usable(Material src)
        {
            Material m;
            if (src != null && src.shader != null && src.shader.isSupported && src.shader.name != "Hidden/InternalErrorShader") m = new Material(src);
            else
            {
                m = _fallbackMaterial(src != null && (src.HasProperty("_BaseColor") || src.HasProperty("_Color")) ? src.color : Color.white);
                if (src != null && src.mainTexture != null) m.mainTexture = src.mainTexture;
            }
            m.enableInstancing = true;
            return m;
        }

        /// <summary>팀 색을 섞은 재질 묶음 (모델 원래 색 × 팀 색 비율).</summary>
        public Material[] Tinted(ArtModel model, Color color, float tint)
        {
            var mats = new Material[model.Materials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = Own(new Material(model.Materials[i]));
                if (m.HasProperty("_BaseColor") || m.HasProperty("_Color")) m.color = m.color * Color.Lerp(Color.white, color, tint);
                m.enableInstancing = true;
                mats[i] = m;
            }
            return mats;
        }

        private static Bounds Transform(Bounds b, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                result.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return result;
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        public void Destroy()
        {
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
        }
    }

    /// <summary>불러온 모델 하나: 메시, 서브메시별 재질, 크기·바닥을 맞춘 변환.</summary>
    public sealed class ArtModel
    {
        public Mesh Mesh { get; }
        public Material[] Materials { get; }
        /// <summary>모델 → 바닥 기준 (높이 맞춤·바닥 = 0).</summary>
        public Matrix4x4 Local { get; }

        public ArtModel(Mesh mesh, Material[] materials, Matrix4x4 local)
        {
            Mesh = mesh;
            Materials = materials;
            Local = local;
        }
    }

    /// <summary>같은 메시·재질로 여러 개를 한 번에 그리는 묶음 (GPU 인스턴싱, 1023개씩 나눠 그린다).</summary>
    public sealed class InstanceBatch
    {
        private const int MaxPerCall = 1023;
        public readonly Mesh Mesh;
        public readonly Material[] Materials;
        public readonly Matrix4x4 Local;
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
        private Matrix4x4[] _buffer = new Matrix4x4[64];

        public InstanceBatch(Mesh mesh, Material[] materials, Matrix4x4 local)
        {
            Mesh = mesh;
            Materials = materials;
            Local = local;
        }

        /// <summary>바닥 위치·방향·크기로 하나 더한다 (모델 변환은 여기서 곱한다).</summary>
        public void Add(Vector3 position, Quaternion rotation, float scale = 1f) =>
            Matrices.Add(Matrix4x4.TRS(position, rotation, Vector3.one * scale) * Local);

        public void Draw(Camera camera)
        {
            int n = Matrices.Count;
            if (n == 0) return;
            if (_buffer.Length < n) _buffer = new Matrix4x4[Mathf.NextPowerOfTwo(n)];
            Matrices.CopyTo(_buffer);
            for (int sub = 0; sub < Materials.Length && sub < Mesh.subMeshCount; sub++)
            {
                var rp = new RenderParams(Materials[sub]) { camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                for (int start = 0; start < n; start += MaxPerCall)
                    Graphics.RenderMeshInstanced(rp, Mesh, sub, _buffer, Mathf.Min(MaxPerCall, n - start), start);
            }
        }
    }
}

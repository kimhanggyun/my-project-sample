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
    /// 여러 조각·뼈대(SkinnedMesh) 모델은 처음 쓸 때 메시 하나로 굳힌다 (<see cref="Bake"/>).
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
            /// <summary>자세를 가져올 동작 파일 (Resources 경로, 예: KayKit 리그 FBX). 없으면 모델 파일 안의 동작.</summary>
            public string PoseFrom;
            /// <summary>그 안에서 고를 동작 이름 (일부만 맞아도 됨, 기본 "idle").</summary>
            public string PoseClip = "idle";
            /// <summary>탈것(mount.*)에 탄 사람을 올리는 높이 (칸 단위, R-90). 없으면 코드 기본값.</summary>
            public float? RiderY;
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
                        PoseFrom = o.Value<string>("poseFrom"),
                        PoseClip = o.Value<string>("poseClip") ?? "idle",
                        RiderY = o.Value<float?>("riderY"),
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
        /// <summary>탈것에 탄 사람 높이 (아트 목록 riderY, 없으면 fallback).</summary>
        public float RiderYOf(string key, float fallback) => key != null && _entries.TryGetValue(key, out var e) && e.RiderY.HasValue ? e.RiderY.Value : fallback;

        public float TintOf(string key) => key != null && _entries.TryGetValue(key, out var e) ? e.Tint : 0.6f;

        private ArtModel Load(string path, Entry e)
        {
            string cacheKey = path + "|" + e.Height + "|" + e.Yaw + "|" + e.PoseFrom + "|" + e.PoseClip;
            if (_loaded.TryGetValue(cacheKey, out var cached)) return cached;
            ArtModel model = null;
            var prefab = string.IsNullOrEmpty(path) ? null : Resources.Load<GameObject>(path);
            if (prefab != null)
            {
                var baked = Bake(prefab, FindPose(path, e), path);
                if (baked.Mesh != null)
                {
                    // 굳힌 메시는 이 목록 것 (나중에 지움). 원래 메시를 그대로 쓰는 경우는 에셋이라 지우면 안 된다
                    var mesh = baked.Owned ? Own(baked.Mesh) : baked.Mesh;
                    var mats = new Material[baked.Materials.Count];
                    for (int i = 0; i < mats.Length; i++) mats[i] = Own(Usable(baked.Materials[i]));
                    // 방향 → 크기를 목표 높이로, 바닥 = 0, 가운데 = 원점 (메시 → 모델 뿌리 → 방향)
                    var turned = Matrix4x4.Rotate(Quaternion.Euler(0f, e.Yaw, 0f)) * baked.MeshToRoot;
                    var b = Transform(mesh.bounds, turned);
                    float s = b.size.y > 1e-5f ? e.Height / b.size.y : 1f;
                    var local = Matrix4x4.Translate(new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s)) * Matrix4x4.Scale(Vector3.one * s) * turned;
                    model = new ArtModel(mesh, mats, local);
                }
            }
            if (model == null && !string.IsNullOrEmpty(path)) Debug.Log("임시 아트 모델이 없어 단순 도형을 씁니다: Resources/" + path);
            _loaded[cacheKey] = model;
            return model;
        }

        /// <summary>굳힌 결과: 메시, 하위 메시별 재질, 메시 → 모델 뿌리 행렬, 새로 만든 메시인지(지워도 되는지).</summary>
        private struct Baked
        {
            public Mesh Mesh;
            public List<Material> Materials;
            public Matrix4x4 MeshToRoot;
            public bool Owned;
        }

        /// <summary>
        /// 모델 하나를 메시 하나로 굳힌다: 보통 메시(MeshFilter)와 뼈대 메시(SkinnedMeshRenderer, 2026-11-13)를 모두 모아,
        /// 뼈대 메시는 Idle 동작 첫 프레임 자세(없으면 기본 자세)로 BakeMesh 한 뒤, 뼈에 붙은 무기·방패 같은 자식 메시까지
        /// 모델 뿌리 기준으로 합친다. 재질이 같은 조각은 한 하위 메시로 묶는다 (그리는 횟수를 줄인다).
        /// 읽을 수 없는 메시(가져오기 설정 Read/Write 꺼짐)는 합칠 수 없다 (2026-11-14):
        /// 조각이 그것 하나뿐이면 굳히지 않고 원래 메시를 그대로 쓰고, 여러 조각이면 그 조각만 빼고 경고를 남긴다.
        /// 모델 전체를 버리지 않는다.
        /// </summary>
        private static Baked Bake(GameObject prefab, AnimationClip pose, string path)
        {
            var inst = Object.Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var temp = new List<Mesh>();
            try
            {
                PoseIdle(inst, pose);
                var toRoot = inst.transform.worldToLocalMatrix;
                var pieces = new List<(Mesh mesh, Material[] mats, Matrix4x4 m, bool baked)>();
                foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    var t = smr.transform;
                    if (!smr.sharedMesh.isReadable)
                    {
                        // 읽을 수 없으면 BakeMesh도 못 한다 → 기본 자세 메시를 그대로 (아래에서 하나뿐이면 원래대로)
                        pieces.Add((smr.sharedMesh, smr.sharedMaterials, toRoot * Matrix4x4.TRS(t.position, t.rotation, t.lossyScale), false));
                        continue;
                    }
                    var bakedMesh = new Mesh();
                    smr.BakeMesh(bakedMesh, true); // 크기까지 반영해 굳힌 메시 (렌더러 위치·회전 기준)
                    temp.Add(bakedMesh);
                    pieces.Add((bakedMesh, smr.sharedMaterials, toRoot * Matrix4x4.TRS(t.position, t.rotation, Vector3.one), true));
                }
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    pieces.Add((mf.sharedMesh, mr != null ? mr.sharedMaterials : null, toRoot * mf.transform.localToWorldMatrix, false));
                }
                if (pieces.Count == 0) return default;

                List<Material> MatsOf(Mesh mesh, Material[] mats)
                {
                    var list = new List<Material>();
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        list.Add(mats != null && mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null);
                    return list;
                }

                // 조각 하나: 합칠 필요가 없다. 읽을 수 없는 원래 메시면 그대로 쓴다
                if (pieces.Count == 1 && !pieces[0].baked && !pieces[0].mesh.isReadable)
                {
                    Debug.LogWarning("아트 모델의 Read/Write가 꺼져 있어 굳히지 않고 원래 메시를 씁니다 (켜 두면 더 좋음): Resources/" + path);
                    return new Baked { Mesh = pieces[0].mesh, Materials = MatsOf(pieces[0].mesh, pieces[0].mats), MeshToRoot = pieces[0].m, Owned = false };
                }

                var groups = new List<(Material mat, List<CombineInstance> parts)>();
                int skipped = 0;
                (Mesh mesh, Material[] mats, Matrix4x4 m) biggestUnreadable = (null, null, Matrix4x4.identity);
                foreach (var pc in pieces)
                {
                    if (!pc.mesh.isReadable)
                    {
                        skipped++;
                        if (biggestUnreadable.mesh == null || pc.mesh.vertexCount > biggestUnreadable.mesh.vertexCount) biggestUnreadable = (pc.mesh, pc.mats, pc.m);
                        continue;
                    }
                    for (int sub = 0; sub < pc.mesh.subMeshCount; sub++)
                    {
                        var mat = pc.mats != null && pc.mats.Length > 0 ? pc.mats[Mathf.Min(sub, pc.mats.Length - 1)] : null;
                        int gi = groups.FindIndex(g => g.mat == mat);
                        if (gi < 0) { groups.Add((mat, new List<CombineInstance>())); gi = groups.Count - 1; }
                        groups[gi].parts.Add(new CombineInstance { mesh = pc.mesh, subMeshIndex = sub, transform = pc.m });
                    }
                }
                if (skipped > 0)
                    Debug.LogWarning($"아트 모델 조각 {skipped}개의 Read/Write가 꺼져 있어 합치지 못했습니다 (가져오기 설정에서 켜 주세요): Resources/{path}");
                if (groups.Count == 0)
                {
                    // 읽을 수 있는 조각이 없으면 가장 큰 조각 하나라도 원래대로
                    return new Baked { Mesh = biggestUnreadable.mesh, Materials = MatsOf(biggestUnreadable.mesh, biggestUnreadable.mats), MeshToRoot = biggestUnreadable.m, Owned = false };
                }
                // 재질마다 한 메시 → 하위 메시로 합침
                var subs = new CombineInstance[groups.Count];
                var matsOut = new List<Material>();
                long vertices = 0;
                for (int i = 0; i < groups.Count; i++)
                {
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(groups[i].parts.ToArray(), true, true);
                    vertices += part.vertexCount;
                    temp.Add(part);
                    subs[i] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
                    matsOut.Add(groups[i].mat);
                }
                var combined = new Mesh { name = prefab.name + " (굳힘)", indexFormat = vertices > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                combined.CombineMeshes(subs, false, false);
                combined.RecalculateBounds();
                return new Baked { Mesh = combined, Materials = matsOut, MeshToRoot = Matrix4x4.identity, Owned = true };
            }
            catch (System.Exception ex)
            {
                // 그 밖의 실패도 모델 하나만 단순 도형으로 (전투 전체가 멈추지 않게)
                Debug.LogWarning("아트 모델을 굳히지 못했습니다: Resources/" + path + " — " + ex.Message);
                return default;
            }
            finally
            {
                foreach (var m in temp) Object.Destroy(m);
                Object.Destroy(inst);
            }
        }

        /// <summary>
        /// 굳힐 자세의 동작: poseFrom 파일(없으면 모델 파일) 안의 동작 중 이름에 poseClip("idle")이 들어간 것, 없으면 첫 동작.
        /// FBX의 동작은 하위 에셋이라 Resources.LoadAll로 꺼낸다.
        /// </summary>
        private static AnimationClip FindPose(string modelPath, Entry e)
        {
            AnimationClip match = null, first = null;
            foreach (var src in new[] { e.PoseFrom, modelPath })
            {
                if (string.IsNullOrEmpty(src)) continue;
                foreach (var clip in Resources.LoadAll<AnimationClip>(src))
                {
                    if (clip == null || clip.name.StartsWith("__preview__")) continue;
                    if (first == null) first = clip;
                    if (match == null && clip.name.ToLowerInvariant().Contains((e.PoseClip ?? "idle").ToLowerInvariant())) match = clip;
                }
                if (match != null) break;
            }
            return match ?? first;
        }

        /// <summary>
        /// 자세 잡기: 주어진 동작(FindPose) → 없으면 모델의 Animator·Animation에 붙은 Idle(또는 첫) 동작의 첫 프레임.
        /// 아무 동작도 없으면 기본 자세 그대로. (일반(Generic) 리그 기준. 휴머노이드 동작은 SampleAnimation이 안 먹을 수 있다)
        /// </summary>
        private static void PoseIdle(GameObject inst, AnimationClip pose)
        {
            var animator = inst.GetComponentInChildren<Animator>(true);
            if (pose == null && animator != null && animator.runtimeAnimatorController != null)
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                {
                    if (clip == null) continue;
                    if (pose == null) pose = clip;
                    if (clip.name.ToLowerInvariant().Contains("idle")) { pose = clip; break; }
                }
            var legacy = inst.GetComponentInChildren<Animation>(true);
            if (pose == null && legacy != null)
                foreach (AnimationState st in legacy)
                    if (st.clip != null) { pose = st.clip; if (st.clip.name.ToLowerInvariant().Contains("idle")) break; }
            if (pose == null) return;
            if (animator != null) animator.enabled = false; // 샘플한 자세를 Animator가 덮지 않게
            pose.SampleAnimation(animator != null ? animator.gameObject : inst, 0f);
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

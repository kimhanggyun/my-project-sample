using System.Collections.Generic;
using Game.Flow;
using Game.Unity.Battle3D;
using Game.Unity.Boot;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity.UI
{
    /// <summary>
    /// 병종 그림 런타임 렌더 (cosmetics.md 9장 4): 전투 병사와 같은 인형(SoldierMeshes, 입은 갑옷 · 말 색 그대로)을 작은 카메라로 찍어 그림으로 쓴다.
    /// 계열 · 갑옷 · 말 조합마다 한 번 찍어 보관하고, 치장을 바꾸면 새 조합으로 다시 찍는다. 찍을 수 없으면 null (화면은 미리 그린 PNG로).
    /// 미리보기라 치장 색은 원래 색 (팀 색 조정 없음), 몸통은 아군 색.
    /// </summary>
    public static class TroopPictureRenderer
    {
        private const int Size = 256;
        private static readonly Color AllyColor = new Color(0.20f, 0.36f, 0.62f);
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static Material _template;

        /// <summary>그 병종의 지금 모습 (입은 치장). 사람 병사 계열이 아니면 null.</summary>
        public static Sprite For(GameContext ctx, string troopType)
        {
            if (ctx?.Defs == null || ctx.State == null) return null;
            string line = CosmeticService.LineOf(ctx.Defs, troopType);
            if (line == null) return null;
            return For(ctx, line, CosmeticService.Worn(ctx.State, line, CosmeticKinds.Armor), CosmeticService.Worn(ctx.State, line, CosmeticKinds.Horse));
        }

        /// <summary>계열 · 갑옷 · 말 조합의 그림 (꾸미기 미리보기는 안 가진 것도 입혀 본다).</summary>
        public static Sprite For(GameContext ctx, string line, string armor, string horse)
        {
            if (ctx?.Defs == null || line == null) return null;
            string key = line + "|" + armor + "|" + horse;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            try { s = Render(ctx, line, armor, horse); }
            catch (System.Exception e) { Debug.LogWarning("병종 그림을 찍지 못했습니다: " + e.Message); s = null; }
            if (s != null) Cache[key] = s;
            return s;
        }

        private static Material NewMaterial(Color c)
        {
            if (_template == null) _template = Resources.Load<Material>("Materials/GameLit");
            if (_template == null && GraphicsSettings.defaultRenderPipeline != null) _template = GraphicsSettings.defaultRenderPipeline.defaultMaterial;
            var m = _template != null ? new Material(_template) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = c;
            m.mainTexture = null;
            return m;
        }

        private static Sprite Render(GameContext ctx, string line, string armor, string horse)
        {
            var armorLook = CosmeticLooks.Of(ctx.Defs, armor);
            var horseLook = CosmeticLooks.Of(ctx.Defs, horse);
            var mesh = SoldierMeshes.For(line, armorLook != null);
            var parts = SoldierMeshes.PartsOf(mesh);
            var made = new List<Object>();
            var mats = new Material[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Color c = parts[i] == SoldierMeshes.Team ? AllyColor
                    : parts[i] == SoldierMeshes.Skin ? SoldierMeshes.SkinColor
                    : parts[i] == SoldierMeshes.Metal ? SoldierMeshes.MetalColor
                    : parts[i] == SoldierMeshes.Armor ? CosmeticLooks.ColorOf(armorLook, "trim", SoldierMeshes.DarkColor)
                    : parts[i] == SoldierMeshes.Horse ? (horseLook != null ? CosmeticLooks.ColorOf(horseLook, "horse", SoldierMeshes.DarkColor) : SoldierMeshes.DarkColor)
                    : SoldierMeshes.DarkColor;
                mats[i] = NewMaterial(c);
                made.Add(mats[i]);
            }
            // 다른 화면과 겹치지 않게 아주 먼 곳에 인형 · 빛 · 카메라를 잠깐 둔다
            var origin = new Vector3(0f, -5000f, 0f);
            var root = new GameObject("TroopPictureStage");
            made.Add(root);
            root.transform.position = origin;
            var doll = new GameObject("Doll", typeof(MeshFilter), typeof(MeshRenderer));
            doll.transform.SetParent(root.transform, false);
            doll.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = doll.GetComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = new GameObject("Light", typeof(Light));
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-0.4f, -0.6f, -0.8f)); // 앞 오른쪽 위에서 비춤 (카메라 쪽 얼굴이 밝게)
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            var camGo = new GameObject("Camera", typeof(Camera));
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.GetComponent<Camera>();
            var b = mesh.bounds;
            Vector3 center = origin + b.center;
            float half = Mathf.Max(b.extents.y, b.extents.x * 1.1f, b.extents.z * 0.9f) * 1.18f;
            cam.orthographic = true;
            cam.orthographicSize = half;
            cam.transform.position = center + (line == "cavalry" ? new Vector3(-1f, 0.25f, 0.35f) : new Vector3(-0.22f, 0.12f, 1f)).normalized * 3f; // 사람 = 거의 앞모습 (병종 그림 PNG처럼), 말 = 옆모습
            cam.transform.LookAt(center);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.enabled = false;
            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "troop_" + line, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            // 바로 지운다: 한 프레임에 여러 장을 찍으면 Destroy(프레임 끝)로는 앞 인형이 남아 겹쳐 찍힌다
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}

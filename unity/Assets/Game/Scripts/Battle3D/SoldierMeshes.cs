using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 임시 병사 모양 (외부 모델이 없을 때, docs/impl/unity/art.md 1장): 기본 도형을 합쳐 병종별 작은 인형을 만든다.
    /// 머리·몸통 + 보병 방패 / 창병 긴 창 / 궁병 활 / 기병 말 / 정찰병 가벼운 차림. 높이 약 0.42 (전장 1칸 = 1), 원점은 발밑.
    /// 부위별 색은 하위 메시(submesh)로 나눈다: 0 = 갑옷·투구(팀 색), 1 = 얼굴(살색), 2 = 무기(쇠·나무), 3 = 다리·말(갈색).
    /// 최종 아트(수묵 방향)는 나중에 모델로 바꾼다.
    /// </summary>
    public static class SoldierMeshes
    {
        /// <summary>하위 메시 번호 (재질 순서).</summary>
        public const int Team = 0, Skin = 1, Metal = 2, Dark = 3;
        /// <summary>팀 색 외 부위 색.</summary>
        public static readonly Color SkinColor = new Color(0.93f, 0.78f, 0.62f);
        public static readonly Color MetalColor = new Color(0.62f, 0.64f, 0.68f);
        public static readonly Color DarkColor = new Color(0.36f, 0.26f, 0.17f);

        private static readonly Dictionary<string, Mesh> Built = new Dictionary<string, Mesh>();

        private static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name + ".fbx");

        /// <param name="baseType">1단계 병종 id (infantry, spearman, archer, cavalry, scout). 모르는 값이면 보병.</param>
        public static Mesh For(string baseType)
        {
            if (Built.TryGetValue(baseType ?? "", out var m) && m != null) return m;
            // 부위 → 하위 메시 번호 (Materials 순서와 같다)
            var parts = new List<CombineInstance>[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            int part = Team;
            var cube = Builtin("Cube");
            var sphere = Builtin("Sphere");
            var capsule = Builtin("Capsule");
            var cylinder = Builtin("Cylinder");
            void Add(Mesh mesh, Vector3 pos, Vector3 scale, Vector3 euler = default) =>
                parts[part].Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale) });

            bool mounted = baseType == "cavalry";
            float lift = mounted ? 0.16f : 0f;
            if (mounted)
            {
                part = Dark;
                Add(cube, new Vector3(0f, 0.13f, 0f), new Vector3(0.12f, 0.11f, 0.30f));              // 말 몸
                Add(cube, new Vector3(0f, 0.22f, 0.17f), new Vector3(0.07f, 0.12f, 0.08f), new Vector3(-30f, 0f, 0f)); // 말 목·머리
                for (int i = 0; i < 4; i++)
                    Add(cube, new Vector3(i % 2 == 0 ? -0.04f : 0.04f, 0.04f, i < 2 ? 0.11f : -0.11f), new Vector3(0.03f, 0.08f, 0.03f)); // 다리
            }
            float bodyH = baseType == "scout" ? 0.11f : 0.13f;
            part = Dark;
            if (!mounted)
            {
                Add(cube, new Vector3(-0.025f, 0.06f, 0f), new Vector3(0.035f, 0.12f, 0.04f));      // 다리
                Add(cube, new Vector3(0.025f, 0.06f, 0f), new Vector3(0.035f, 0.12f, 0.04f));
            }
            part = Team;
            Add(capsule, new Vector3(0f, lift + 0.17f, 0f), new Vector3(0.11f, bodyH * 0.85f, 0.09f)); // 몸통(갑옷)
            Add(cube, new Vector3(0f, lift + 0.375f, 0f), new Vector3(0.1f, 0.035f, 0.1f));           // 투구
            part = Skin;
            Add(sphere, new Vector3(0f, lift + 0.33f, 0f), new Vector3(0.085f, 0.085f, 0.085f));       // 얼굴
            part = Metal;
            switch (baseType)
            {
                case "spearman":
                    Add(cylinder, new Vector3(0.07f, lift + 0.30f, 0.03f), new Vector3(0.015f, 0.27f, 0.015f)); // 긴 창 (세움)
                    break;
                case "archer":
                    Add(cube, new Vector3(0.075f, lift + 0.22f, 0.04f), new Vector3(0.012f, 0.24f, 0.03f), new Vector3(0f, 0f, 8f)); // 활
                    Add(cube, new Vector3(-0.04f, lift + 0.22f, -0.06f), new Vector3(0.04f, 0.12f, 0.03f));                     // 화살통
                    break;
                case "cavalry":
                    Add(cylinder, new Vector3(0.07f, lift + 0.22f, 0.12f), new Vector3(0.012f, 0.2f, 0.012f), new Vector3(60f, 0f, 0f)); // 앞으로 든 창
                    break;
                case "scout":
                    Add(cube, new Vector3(0.06f, lift + 0.16f, 0.03f), new Vector3(0.015f, 0.1f, 0.015f)); // 짧은 칼
                    break;
                default: // 보병: 방패(팀 색) + 칼
                    part = Team;
                    Add(cube, new Vector3(-0.07f, lift + 0.18f, 0.05f), new Vector3(0.02f, 0.14f, 0.11f));
                    part = Metal;
                    Add(cube, new Vector3(0.07f, lift + 0.2f, 0.04f), new Vector3(0.015f, 0.13f, 0.015f));
                    break;
            }
            // 부위마다 하나로 합친 뒤, 부위들을 하위 메시로 합친다
            var subs = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var pm = new Mesh();
                if (parts[i].Count > 0) pm.CombineMeshes(parts[i].ToArray(), true, true);
                subs[i] = new CombineInstance { mesh = pm, transform = Matrix4x4.identity };
            }
            m = new Mesh { name = "soldier_" + (baseType ?? "default") };
            m.CombineMeshes(subs, false, false);
            m.RecalculateBounds();
            return Built[baseType ?? ""] = m;
        }

        /// <summary>쓰러진 병사 표시 (낮고 납작한 상자).</summary>
        public static readonly Vector3 FallenScale = new Vector3(0.12f, 0.035f, 0.26f);
    }
}

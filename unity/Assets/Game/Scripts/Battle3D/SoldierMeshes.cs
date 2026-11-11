using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 임시 병사 모양 (외부 모델이 없을 때, docs/impl/unity/art.md 1장): 기본 도형을 합쳐 병종별 작은 인형을 만든다.
    /// 머리·몸통 + 보병 방패 / 창병 긴 창 / 궁병 활 / 기병 말 / 정찰병 가벼운 차림. 높이 약 0.42 (전장 1칸 = 1), 원점은 발밑.
    /// 색은 재질(팀 색) 하나로 칠한다. 최종 아트(수묵 방향)는 나중에 모델로 바꾼다.
    /// </summary>
    public static class SoldierMeshes
    {
        private static readonly Dictionary<string, Mesh> Built = new Dictionary<string, Mesh>();

        private static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name + ".fbx");

        /// <param name="baseType">1단계 병종 id (infantry, spearman, archer, cavalry, scout). 모르는 값이면 보병.</param>
        public static Mesh For(string baseType)
        {
            if (Built.TryGetValue(baseType ?? "", out var m) && m != null) return m;
            var parts = new List<CombineInstance>();
            var cube = Builtin("Cube");
            var sphere = Builtin("Sphere");
            var capsule = Builtin("Capsule");
            var cylinder = Builtin("Cylinder");
            void Add(Mesh mesh, Vector3 pos, Vector3 scale, Vector3 euler = default) =>
                parts.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale) });

            bool mounted = baseType == "cavalry";
            float lift = mounted ? 0.16f : 0f;
            if (mounted)
            {
                Add(cube, new Vector3(0f, 0.13f, 0f), new Vector3(0.12f, 0.11f, 0.30f));              // 말 몸
                Add(cube, new Vector3(0f, 0.22f, 0.17f), new Vector3(0.07f, 0.12f, 0.08f), new Vector3(-30f, 0f, 0f)); // 말 목·머리
                for (int i = 0; i < 4; i++)
                    Add(cube, new Vector3(i % 2 == 0 ? -0.04f : 0.04f, 0.04f, i < 2 ? 0.11f : -0.11f), new Vector3(0.03f, 0.08f, 0.03f)); // 다리
            }
            float bodyH = baseType == "scout" ? 0.11f : 0.13f;
            Add(capsule, new Vector3(0f, lift + 0.15f, 0f), new Vector3(0.11f, bodyH, 0.09f));       // 몸통
            Add(sphere, new Vector3(0f, lift + 0.33f, 0f), new Vector3(0.09f, 0.09f, 0.09f));         // 머리
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
                default: // 보병: 방패 + 칼
                    Add(cube, new Vector3(-0.07f, lift + 0.18f, 0.05f), new Vector3(0.02f, 0.14f, 0.11f));
                    Add(cube, new Vector3(0.07f, lift + 0.2f, 0.04f), new Vector3(0.015f, 0.13f, 0.015f));
                    break;
            }
            m = new Mesh { name = "soldier_" + (baseType ?? "default") };
            m.CombineMeshes(parts.ToArray(), true, true);
            m.RecalculateBounds();
            return Built[baseType ?? ""] = m;
        }

        /// <summary>쓰러진 병사 표시 (낮고 납작한 상자).</summary>
        public static readonly Vector3 FallenScale = new Vector3(0.12f, 0.035f, 0.26f);
    }
}

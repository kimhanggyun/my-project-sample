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

            if (baseType == "catapult")
            {
                // 투석기 (R-83): 나무 틀 + 바퀴 4 + 비스듬한 팔 + 돌 바구니. 팀 색은 틀 위 천
                part = Dark;
                Add(cube, new Vector3(0f, 0.08f, 0f), new Vector3(0.22f, 0.05f, 0.34f));                              // 바닥 틀
                Add(cube, new Vector3(-0.09f, 0.17f, 0f), new Vector3(0.03f, 0.16f, 0.03f));                          // 기둥
                Add(cube, new Vector3(0.09f, 0.17f, 0f), new Vector3(0.03f, 0.16f, 0.03f));
                Add(cube, new Vector3(0f, 0.27f, -0.04f), new Vector3(0.025f, 0.03f, 0.38f), new Vector3(-35f, 0f, 0f)); // 던지는 팔
                for (int i = 0; i < 4; i++)
                    Add(cylinder, new Vector3(i % 2 == 0 ? -0.13f : 0.13f, 0.05f, i < 2 ? 0.12f : -0.12f), new Vector3(0.09f, 0.015f, 0.09f), new Vector3(0f, 0f, 90f)); // 바퀴
                part = Metal;
                Add(sphere, new Vector3(0f, 0.39f, -0.19f), new Vector3(0.07f, 0.07f, 0.07f));                       // 돌
                part = Team;
                Add(cube, new Vector3(0f, 0.115f, 0.1f), new Vector3(0.2f, 0.02f, 0.12f));                            // 천 (팀 색)
                part = Skin;
                Add(sphere, new Vector3(0.14f, 0.2f, 0.14f), new Vector3(0.05f, 0.05f, 0.05f));                       // 곁의 병사 (머리만)
            }
            else if (baseType == "warship")
            {
                // 배 (R-85): 갈색 선체 + 돛대 + 팀 색 돛, 갑판 위 병사 둘 (머리). 수군 2단계도 모두 이 모양
                part = Dark;
                Add(cube, new Vector3(0f, 0.05f, 0f), new Vector3(0.18f, 0.08f, 0.42f));                       // 선체
                Add(cube, new Vector3(0f, 0.07f, 0.22f), new Vector3(0.1f, 0.06f, 0.08f), new Vector3(0f, 45f, 0f)); // 뱃머리
                Add(cylinder, new Vector3(0f, 0.3f, 0f), new Vector3(0.02f, 0.22f, 0.02f));                   // 돛대
                part = Team;
                Add(cube, new Vector3(0f, 0.32f, 0.02f), new Vector3(0.2f, 0.2f, 0.01f));                     // 돛 (팀 색)
                part = Skin;
                Add(sphere, new Vector3(-0.05f, 0.13f, -0.1f), new Vector3(0.05f, 0.05f, 0.05f));
                Add(sphere, new Vector3(0.05f, 0.13f, 0.12f), new Vector3(0.05f, 0.05f, 0.05f));
            }
            else BuildPerson(baseType, parts, cube, sphere, capsule, cylinder);
            var subs0 = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var pm = new Mesh();
                if (parts[i].Count > 0) pm.CombineMeshes(parts[i].ToArray(), true, true);
                subs0[i] = new CombineInstance { mesh = pm, transform = Matrix4x4.identity };
            }
            m = new Mesh { name = "soldier_" + (baseType ?? "default") };
            m.CombineMeshes(subs0, false, false);
            m.RecalculateBounds();
            return Built[baseType ?? ""] = m;
        }

        private static void BuildPerson(string baseType, List<CombineInstance>[] parts, Mesh cube, Mesh sphere, Mesh capsule, Mesh cylinder)
        {
            int part = Team;
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
                case "engineer":
                    // 공병 (R-83): 등에 진 나무 기둥 + 손에 든 도끼
                    part = Dark;
                    Add(cylinder, new Vector3(-0.02f, lift + 0.24f, -0.07f), new Vector3(0.02f, 0.16f, 0.02f), new Vector3(0f, 0f, 70f));
                    part = Metal;
                    Add(cube, new Vector3(0.07f, lift + 0.2f, 0.04f), new Vector3(0.012f, 0.12f, 0.012f));
                    Add(cube, new Vector3(0.07f, lift + 0.26f, 0.06f), new Vector3(0.012f, 0.04f, 0.05f));
                    break;
                default: // 보병: 방패(팀 색) + 칼
                    part = Team;
                    Add(cube, new Vector3(-0.07f, lift + 0.18f, 0.05f), new Vector3(0.02f, 0.14f, 0.11f));
                    part = Metal;
                    Add(cube, new Vector3(0.07f, lift + 0.2f, 0.04f), new Vector3(0.015f, 0.13f, 0.015f));
                    break;
            }
        }

        /// <summary>장수 재질 순서: 0 = 갑옷·망토(팀 색), 1 = 얼굴, 2 = 금빛 장식·무기, 3 = 탈것 몸 (말 = 흰 말, 가마 = 나무).</summary>
        public static readonly Color GoldColor = new Color(0.95f, 0.76f, 0.26f);
        public static readonly Color GeneralHorseColor = new Color(0.9f, 0.88f, 0.84f);
        public static readonly Color PalanquinWoodColor = new Color(0.45f, 0.28f, 0.16f);

        /// <summary>탈것 위에 탄 사람의 엉덩이 높이 (장수 크기 배율 적용 전, 칸 단위). 아트 장수 모델을 올릴 때도 쓴다.</summary>
        public static float RiderLift(string mount) => (mount == Game.Data.Defs.MountRules.Palanquin ? 0.13f : 0.17f) * GeneralScale;

        /// <summary>장수 인형 크기 (부대원 대비).</summary>
        public const float GeneralScale = 1.8f;

        /// <summary>
        /// 장수 인형 (부대원과 구별, 2026-11-11): 부대원보다 크게(약 1.8배), 금빛 투구·깃털·어깨 장식, 팀 색 망토, 금빛 칼.
        /// 탈것 (R-90): 말(흰 말) 또는 가마(나무 가마 + 팀 색 지붕, 메는 사람 넷). 가마에 탄 책사는 칼 대신 깃털 부채.
        /// 총대장(commander)은 등에 군기(깃대 + 팀 색 깃발)를 하나 더 단다. 원점은 발밑.
        /// rider = false면 탈것만 (아트 장수 모델을 그 위에 올릴 때).
        /// </summary>
        public static Mesh General(bool commander, string mount = null, bool rider = true)
        {
            bool palanquin = mount == Game.Data.Defs.MountRules.Palanquin;
            string key = (commander ? "#commander" : "#general") + (palanquin ? "_palanquin" : "_horse") + (rider ? "" : "_mountOnly");
            if (Built.TryGetValue(key, out var m) && m != null) return m;
            var parts = new List<CombineInstance>[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            var cube = Builtin("Cube");
            var sphere = Builtin("Sphere");
            var capsule = Builtin("Capsule");
            var cylinder = Builtin("Cylinder");
            int part = Dark;
            const float k = GeneralScale;
            void Add(Mesh mesh, Vector3 pos, Vector3 scale, Vector3 euler = default) =>
                parts[part].Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos * k, Quaternion.Euler(euler), scale * k) });

            float lift;
            if (palanquin)
            {
                // 가마: 나무 상자 + 앞뒤로 긴 멜대 두 개, 메는 사람 넷 (팀 색 옷), 팀 색 지붕
                Add(cube, new Vector3(0f, 0.17f, 0f), new Vector3(0.16f, 0.06f, 0.2f));                 // 바닥
                Add(cube, new Vector3(-0.075f, 0.24f, 0f), new Vector3(0.012f, 0.1f, 0.2f));            // 옆 난간
                Add(cube, new Vector3(0.075f, 0.24f, 0f), new Vector3(0.012f, 0.1f, 0.2f));
                Add(cube, new Vector3(0f, 0.24f, -0.095f), new Vector3(0.16f, 0.1f, 0.012f));           // 등받이
                for (int i = 0; i < 4; i++)
                    Add(cylinder, new Vector3(i % 2 == 0 ? -0.075f : 0.075f, 0.36f, i < 2 ? 0.09f : -0.09f), new Vector3(0.012f, 0.1f, 0.012f)); // 지붕 기둥
                Add(cube, new Vector3(-0.1f, 0.16f, 0f), new Vector3(0.016f, 0.016f, 0.52f));           // 멜대
                Add(cube, new Vector3(0.1f, 0.16f, 0f), new Vector3(0.016f, 0.016f, 0.52f));
                part = Team;
                Add(cube, new Vector3(0f, 0.465f, 0f), new Vector3(0.2f, 0.025f, 0.24f));               // 지붕
                Add(cube, new Vector3(0f, 0.495f, 0f), new Vector3(0.12f, 0.035f, 0.15f));
                for (int i = 0; i < 4; i++) // 메는 사람
                {
                    var at = new Vector3(i % 2 == 0 ? -0.1f : 0.1f, 0f, i < 2 ? 0.22f : -0.22f);
                    part = Team;
                    Add(capsule, at + new Vector3(0f, 0.09f, 0f), new Vector3(0.05f, 0.06f, 0.04f));
                    part = Skin;
                    Add(sphere, at + new Vector3(0f, 0.16f, 0f), new Vector3(0.035f, 0.035f, 0.035f));
                }
                part = Metal;
                Add(sphere, new Vector3(0f, 0.53f, 0f), new Vector3(0.03f, 0.03f, 0.03f));             // 지붕 꼭지
                lift = 0.13f;
            }
            else
            {
                // 흰 말
                Add(cube, new Vector3(0f, 0.14f, 0f), new Vector3(0.13f, 0.12f, 0.32f));
                Add(cube, new Vector3(0f, 0.24f, 0.18f), new Vector3(0.075f, 0.13f, 0.085f), new Vector3(-30f, 0f, 0f));
                for (int i = 0; i < 4; i++)
                    Add(cube, new Vector3(i % 2 == 0 ? -0.045f : 0.045f, 0.045f, i < 2 ? 0.12f : -0.12f), new Vector3(0.032f, 0.09f, 0.032f));
                part = Team;
                Add(cube, new Vector3(0f, 0.2f, 0f), new Vector3(0.14f, 0.03f, 0.2f)); // 말 안장 덮개
                lift = 0.17f;
            }
            if (rider)
            {
                // 갑옷·망토 (팀 색)
                part = Team;
                Add(capsule, new Vector3(0f, lift + 0.17f, 0f), new Vector3(0.12f, 0.12f, 0.1f));
                Add(cube, new Vector3(0f, lift + 0.15f, -0.07f), new Vector3(0.15f, 0.22f, 0.015f), new Vector3(12f, 0f, 0f)); // 망토
                // 얼굴
                part = Skin;
                Add(sphere, new Vector3(0f, lift + 0.33f, 0f), new Vector3(0.085f, 0.085f, 0.085f));
                // 금빛: 투구·깃털·어깨·칼 (가마의 책사는 관과 깃털 부채)
                part = Metal;
                Add(cube, new Vector3(0f, lift + 0.38f, 0f), new Vector3(0.105f, 0.04f, 0.105f));
                Add(cube, new Vector3(0f, lift + 0.45f, -0.01f), new Vector3(0.018f, 0.1f, 0.04f));        // 깃털·관
                Add(sphere, new Vector3(-0.07f, lift + 0.24f, 0f), new Vector3(0.05f, 0.04f, 0.06f));       // 어깨
                Add(sphere, new Vector3(0.07f, lift + 0.24f, 0f), new Vector3(0.05f, 0.04f, 0.06f));
                if (palanquin) Add(cube, new Vector3(0.08f, lift + 0.3f, 0.05f), new Vector3(0.07f, 0.09f, 0.01f), new Vector3(0f, 0f, 15f)); // 부채
                else Add(cube, new Vector3(0.08f, lift + 0.26f, 0.05f), new Vector3(0.016f, 0.2f, 0.02f), new Vector3(25f, 0f, 0f)); // 칼
            }
            if (commander)
            {
                // 총대장 군기: 등 뒤 깃대(금) + 깃발(팀 색)
                part = Metal;
                Add(cylinder, new Vector3(-0.05f, lift + 0.42f, -0.08f), new Vector3(0.01f, 0.2f, 0.01f));
                part = Team;
                Add(cube, new Vector3(-0.05f - 0.06f, lift + 0.56f, -0.08f), new Vector3(0.12f, 0.08f, 0.01f));
            }
            var subs = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var pm = new Mesh();
                if (parts[i].Count > 0) pm.CombineMeshes(parts[i].ToArray(), true, true);
                subs[i] = new CombineInstance { mesh = pm, transform = Matrix4x4.identity };
            }
            m = new Mesh { name = key.TrimStart('#') };
            m.CombineMeshes(subs, false, false);
            m.RecalculateBounds();
            return Built[key] = m;
        }

        /// <summary>쓰러진 병사 표시 (낮고 납작한 상자).</summary>
        public static readonly Vector3 FallenScale = new Vector3(0.12f, 0.035f, 0.26f);
    }
}

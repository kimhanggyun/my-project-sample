using System.Collections.Generic;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 병사 모양 (art.md 1장): 기본 도형을 합쳐 병종별 인형을 만든다. 사람 병사는 아트 목록에 모델을 두지 않아 늘 이것 (가는 실루엣, 2026-11-15).
    /// 가는 몸 + 보병 방패 / 창병 긴 창 / 궁병 활 / 기병 말 / 정찰병 가벼운 차림. 높이 약 0.36 (창 끝 0.54, 전장 1칸 = 1), 원점은 발밑.
    /// 부위별 색은 하위 메시(submesh)로 나눈다: 0 = 갑옷·투구(팀 색), 1 = 얼굴(살색), 2 = 무기(쇠·나무), 3 = 다리·말(갈색).
    /// 최종 아트(수묵 방향)는 나중에 모델로 바꾼다.
    /// </summary>
    public static class SoldierMeshes
    {
        /// <summary>부위 번호. 병사 메시는 비지 않은 부위만 하위 메시가 되고, 차례는 <see cref="PartsOf"/> (장수 메시는 0~3 그대로).</summary>
        public const int Team = 0, Skin = 1, Metal = 2, Dark = 3, Armor = 4, Horse = 5;
        private const int PartCount = 6;
        private static readonly Dictionary<Mesh, int[]> Parts = new Dictionary<Mesh, int[]>();

        /// <summary>병사 메시의 하위 메시마다 부위 번호 (재질을 이 차례로). 모르는 메시면 0~3.</summary>
        public static int[] PartsOf(Mesh m) => m != null && Parts.TryGetValue(m, out var p) ? p : new[] { Team, Skin, Metal, Dark };
        /// <summary>팀 색 외 부위 색.</summary>
        public static readonly Color SkinColor = new Color(0.93f, 0.78f, 0.62f);
        public static readonly Color MetalColor = new Color(0.62f, 0.64f, 0.68f);
        public static readonly Color DarkColor = new Color(0.36f, 0.26f, 0.17f);

        private static readonly Dictionary<string, Mesh> Built = new Dictionary<string, Mesh>();

        private static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name + ".fbx");

        /// <param name="baseType">1단계 병종 id (infantry, spearman, archer, cavalry, scout). 모르는 값이면 보병.</param>
        /// <param name="armored">치장 갑옷을 입음 (cosmetics.md 2장): 어깨 덧댐 · 가슴 띠 · 둥근 투구 · 방패가 갑옷 부위(Armor)로. 말 몸은 늘 Horse 부위 (말 색).</param>
        public static Mesh For(string baseType, bool armored = false)
        {
            string cacheKey = (baseType ?? "") + (armored ? "#armor" : "");
            if (Built.TryGetValue(cacheKey, out var m) && m != null) return m;
            var parts = new List<CombineInstance>[PartCount];
            for (int i = 0; i < PartCount; i++) parts[i] = new List<CombineInstance>();
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
            else BuildPerson(baseType, parts, cube, sphere, capsule, cylinder, armored);
            // 비지 않은 부위만 하위 메시로 (빈 하위 메시를 그리지 않게)
            var subs0 = new List<CombineInstance>();
            var map = new List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Count == 0) continue;
                var pm = new Mesh();
                pm.CombineMeshes(parts[i].ToArray(), true, true);
                subs0.Add(new CombineInstance { mesh = pm, transform = Matrix4x4.identity });
                map.Add(i);
            }
            m = new Mesh { name = "soldier_" + cacheKey };
            m.CombineMeshes(subs0.ToArray(), false, false);
            m.RecalculateBounds();
            Parts[m] = map.ToArray();
            return Built[cacheKey] = m;
        }

        /// <summary>
        /// 병사 = 가는 실루엣 (2026-11-15 사용자 시안 C): 사람 비율의 가는 몸이라 병사 사이에 틈이 보이고 움직임이 또렷하다.
        /// 몸통 · 팔만 팀 색, 얼굴 살색(위에서도 보이게 크게), 가는 다리 · 투구 챙 · 방패 · 활 · 말은 갈색, 칼 · 창은 쇠. 병종은 들고 있는 것으로 구분:
        /// 보병 = 둥근 방패 + 칼, 창병 = 몸보다 긴 창, 궁병 = 활 + 등 화살통, 기병 = 가는 말 + 앞으로 든 창, 정찰병 = 투구 없이 짧은 칼, 공병 = 도끼 + 등 나무.
        /// </summary>
        private static void BuildPerson(string baseType, List<CombineInstance>[] parts, Mesh cube, Mesh sphere, Mesh capsule, Mesh cylinder, bool armored)
        {
            int part = Team;
            void Add(Mesh mesh, Vector3 pos, Vector3 scale, Vector3 euler = default) =>
                parts[part].Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale) });
            bool mounted = baseType == "cavalry";
            float lift = mounted ? 0.15f : 0f;
            if (mounted)
            {
                // 가는 말: 몸 · 목 · 머리 · 다리 넷 · 꼬리 (말 색 부위)
                part = Horse;
                Add(capsule, new Vector3(0f, 0.13f, 0f), new Vector3(0.07f, 0.12f, 0.07f), new Vector3(90f, 0f, 0f));
                Add(cube, new Vector3(0f, 0.2f, 0.12f), new Vector3(0.035f, 0.11f, 0.045f), new Vector3(-35f, 0f, 0f));
                Add(cube, new Vector3(0f, 0.245f, 0.17f), new Vector3(0.035f, 0.035f, 0.075f), new Vector3(10f, 0f, 0f));
                for (int i = 0; i < 4; i++)
                    Add(cylinder, new Vector3(i % 2 == 0 ? -0.022f : 0.022f, 0.045f, i < 2 ? 0.08f : -0.08f), new Vector3(0.016f, 0.045f, 0.016f));
                Add(cube, new Vector3(0f, 0.13f, -0.135f), new Vector3(0.015f, 0.07f, 0.015f), new Vector3(25f, 0f, 0f));
            }
            bool light = baseType == "scout";
            part = Dark;
            if (!mounted)
            {
                // 가는 다리 (병종 그림 PNG 비율: 다리 = 키의 1/3, 살짝 벌림)
                Add(cylinder, new Vector3(-0.02f, 0.068f, 0f), new Vector3(0.024f, 0.07f, 0.024f), new Vector3(0f, 0f, -3f));
                Add(cylinder, new Vector3(0.02f, 0.068f, 0f), new Vector3(0.024f, 0.07f, 0.024f), new Vector3(0f, 0f, 3f));
            }
            else
            {
                Add(cylinder, new Vector3(-0.035f, lift + 0.03f, 0.01f), new Vector3(0.018f, 0.04f, 0.018f), new Vector3(0f, 0f, 30f)); // 말 탄 다리
                Add(cylinder, new Vector3(0.035f, lift + 0.03f, 0.01f), new Vector3(0.018f, 0.04f, 0.018f), new Vector3(0f, 0f, -30f));
            }
            // 몸통: 어깨가 넓고 허리가 좁은 웃옷 (팀 색, 위 · 아래 두 덩어리로 가늘어지게) + 허리띠
            part = Team;
            Add(capsule, new Vector3(0f, lift + 0.235f, 0f), new Vector3(0.06f, 0.05f, 0.036f));
            Add(capsule, new Vector3(0f, lift + 0.175f, 0f), new Vector3(0.046f, 0.045f, 0.032f));
            // 팔: 어깨에서 비스듬히 벌려 내림 (그림처럼 몸 밖으로), 끝에 손
            Add(cylinder, new Vector3(-0.047f, lift + 0.225f, 0.008f), new Vector3(0.016f, 0.05f, 0.016f), new Vector3(8f, 0f, -22f));
            Add(cylinder, new Vector3(0.047f, lift + 0.225f, 0.008f), new Vector3(0.016f, 0.05f, 0.016f), new Vector3(8f, 0f, 22f));
            part = Dark;
            Add(cylinder, new Vector3(0f, lift + 0.15f, 0f), new Vector3(0.05f, 0.006f, 0.036f));                     // 허리띠
            part = Skin;
            Add(sphere, new Vector3(-0.066f, lift + 0.183f, 0.012f), new Vector3(0.018f, 0.018f, 0.018f));            // 손
            Add(sphere, new Vector3(0.066f, lift + 0.183f, 0.012f), new Vector3(0.018f, 0.018f, 0.018f));
            Add(cylinder, new Vector3(0f, lift + 0.293f, 0f), new Vector3(0.018f, 0.012f, 0.018f));                   // 목
            Add(sphere, new Vector3(0f, lift + 0.33f, 0f), new Vector3(0.062f, 0.064f, 0.062f));                      // 얼굴
            // 뾰족 투구 (그림의 삿갓 모양): 넓은 챙 + 위로 좁아지는 둥근 판들. 정찰병은 투구 대신 팀 색 머리띠
            part = armored ? Armor : Dark;
            if (!light)
            {
                Add(cylinder, new Vector3(0f, lift + 0.357f, 0f), new Vector3(0.088f, 0.005f, 0.088f));
                Add(cylinder, new Vector3(0f, lift + 0.369f, 0f), new Vector3(0.05f, 0.008f, 0.05f));
                Add(cylinder, new Vector3(0f, lift + 0.383f, 0f), new Vector3(0.034f, 0.008f, 0.034f));
                Add(cylinder, new Vector3(0f, lift + 0.396f, 0f), new Vector3(0.018f, 0.008f, 0.018f));
            }
            else
            {
                part = Team;
                Add(cylinder, new Vector3(0f, lift + 0.343f, 0f), new Vector3(0.064f, 0.006f, 0.064f));
            }
            if (armored)
            {
                // 치장 갑옷: 어깨 덧댐 · 가슴 띠 (팀 색 몸통은 그대로 보이게 작게), 투구 · 방패는 위에서 갑옷 색
                part = Armor;
                Add(sphere, new Vector3(-0.036f, lift + 0.272f, 0f), new Vector3(0.036f, 0.022f, 0.04f));
                Add(sphere, new Vector3(0.036f, lift + 0.272f, 0f), new Vector3(0.036f, 0.022f, 0.04f));
                Add(cube, new Vector3(0f, lift + 0.235f, 0.017f), new Vector3(0.05f, 0.018f, 0.008f), new Vector3(0f, 0f, 30f));
            }
            part = Metal;
            switch (baseType)
            {
                case "spearman":
                    part = Dark;
                    Add(cylinder, new Vector3(0.068f, lift + 0.25f, 0.012f), new Vector3(0.009f, 0.25f, 0.009f));         // 몸보다 긴 창 (나무 자루, 손에 듦)
                    part = Metal;
                    Add(cube, new Vector3(0.068f, lift + 0.515f, 0.012f), new Vector3(0.016f, 0.04f, 0.006f));            // 창날
                    break;
                case "archer":
                    part = Dark;
                    Add(cube, new Vector3(-0.075f, lift + 0.235f, 0.02f), new Vector3(0.008f, 0.1f, 0.012f), new Vector3(0f, 0f, -20f)); // 활 위 (왼손)
                    Add(cube, new Vector3(-0.075f, lift + 0.135f, 0.02f), new Vector3(0.008f, 0.1f, 0.012f), new Vector3(0f, 0f, 20f));  // 활 아래
                    Add(cube, new Vector3(-0.012f, lift + 0.24f, -0.04f), new Vector3(0.025f, 0.09f, 0.02f), new Vector3(0f, 0f, 20f)); // 등 화살통
                    break;
                case "cavalry":
                    Add(cylinder, new Vector3(0.045f, lift + 0.2f, 0.1f), new Vector3(0.008f, 0.17f, 0.008f), new Vector3(70f, 0f, 0f)); // 앞으로 든 창
                    break;
                case "scout":
                    Add(cube, new Vector3(0.072f, lift + 0.21f, 0.014f), new Vector3(0.008f, 0.06f, 0.008f), new Vector3(0f, 0f, -15f)); // 짧은 칼
                    break;
                case "engineer":
                    part = Dark;
                    Add(cylinder, new Vector3(-0.01f, lift + 0.24f, -0.05f), new Vector3(0.016f, 0.12f, 0.016f), new Vector3(0f, 0f, 70f)); // 등 나무
                    part = Metal;
                    Add(cube, new Vector3(0.05f, lift + 0.18f, 0.03f), new Vector3(0.008f, 0.1f, 0.008f));               // 도끼 자루
                    Add(cube, new Vector3(0.05f, lift + 0.23f, 0.05f), new Vector3(0.008f, 0.035f, 0.035f));             // 도끼 날
                    break;
                default: // 보병: 둥근 방패(나무, 갑옷을 입으면 갑옷 색) + 칼
                    part = armored ? Armor : Dark;
                    Add(cylinder, new Vector3(-0.07f, lift + 0.2f, 0.03f), new Vector3(0.1f, 0.006f, 0.1f), new Vector3(90f, 0f, 0f)); // 앞(+Z)을 보는 둥근 방패 (왼손)
                    part = Metal;
                    Add(sphere, new Vector3(-0.07f, lift + 0.2f, 0.035f), new Vector3(0.022f, 0.022f, 0.01f));               // 방패 가운데 쇠
                    Add(cube, new Vector3(0.075f, lift + 0.235f, 0.014f), new Vector3(0.009f, 0.1f, 0.009f), new Vector3(0f, 0f, -18f)); // 칼 (오른손, 위로)
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

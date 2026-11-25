using Game.Battle.Model.Crowd;
using UnityEngine;

namespace Game.Unity.Battle3D
{
    /// <summary>
    /// 병사 머리 위 상태 표시 (R-152, 2026-11-25 사용자 "기본 없음, 고무 = 위 화살표, 사기 저하 = 아래 화살표, 혼란 = 어지러움 동그라미, 이탈 = 패닉, 공격 · 방어 · 강행군도").
    /// 모양은 기본 도형으로 만든 작은 3D 아이콘 (한 색). 크기 단위는 병사 모형과 같음 (그리는 쪽이 ModelScale을 곱함).
    /// 사기 충만 ↑ 금색 · 공격 강화 칼 붉은색 · 방어 강화 방패 파랑 · 강행군 » 초록 (땅과 나란히, 걷는 쪽) · 사기 저하 ↓ 회청 · 혼란 동그라미 노랑 (빙글) · 패닉 ‼ 빨강 (흰색과 번갈아 깜박).
    /// </summary>
    public static class MarkIcons
    {
        public const int Count = 8;
        private static readonly Mesh[] Built = new Mesh[Count];

        /// <summary>표시마다 색 (두 번째 = 깜박일 때 색, 없으면 같은 색).</summary>
        public static (Color main, Color alt) ColorsOf(int mark)
        {
            switch (mark)
            {
                case CrowdBattle.Marks.Inspired: return (new Color(1f, 0.82f, 0.2f), new Color(1f, 0.82f, 0.2f));
                case CrowdBattle.Marks.AttackUp: return (new Color(0.95f, 0.32f, 0.18f), new Color(1f, 0.7f, 0.5f));
                case CrowdBattle.Marks.DefenseUp: return (new Color(0.3f, 0.58f, 1f), new Color(0.7f, 0.85f, 1f));
                case CrowdBattle.Marks.March: return (new Color(0.4f, 0.9f, 0.4f), new Color(0.4f, 0.9f, 0.4f));
                case CrowdBattle.Marks.MoraleDown: return (new Color(0.5f, 0.56f, 0.68f), new Color(0.5f, 0.56f, 0.68f));
                case CrowdBattle.Marks.Confused: return (new Color(1f, 0.93f, 0.45f), new Color(1f, 0.93f, 0.45f));
                case CrowdBattle.Marks.Panic: return (new Color(1f, 0.12f, 0.08f), Color.white);
                default: return (Color.white, Color.white);
            }
        }

        public static Mesh Of(int mark)
        {
            if (mark <= 0 || mark >= Count) return null;
            if (Built[mark] != null) return Built[mark];
            var b = new LookMeshes.Builder();
            switch (mark)
            {
                case CrowdBattle.Marks.Inspired: Arrow(b, 1f); break;
                case CrowdBattle.Marks.MoraleDown: Arrow(b, -1f); break;
                case CrowdBattle.Marks.AttackUp:
                    b.Add("box", "main", new Vector3(0f, 0.035f, 0f), new Vector3(0.032f, 0.15f, 0.014f), new Vector3(0f, 0f, -30f));
                    b.Add("cone", "main", new Vector3(0.048f, 0.118f, 0f), new Vector3(0.032f, 0.04f, 0.014f), new Vector3(0f, 0f, -30f));
                    b.Add("box", "main", new Vector3(-0.03f, -0.05f, 0f), new Vector3(0.09f, 0.022f, 0.022f), new Vector3(0f, 0f, -30f));
                    b.Add("box", "main", new Vector3(-0.05f, -0.08f, 0f), new Vector3(0.026f, 0.05f, 0.026f), new Vector3(0f, 0f, -30f));
                    break;
                case CrowdBattle.Marks.DefenseUp:
                    // 방패 모양 (위 네모 + 아래 뾰족, 2026-11-25 캡처: 둥근 판은 위에서 보면 동그라미 덩어리) + 가운데 세로 줄
                    b.Add("box", "main", new Vector3(0f, 0.03f, 0f), new Vector3(0.14f, 0.09f, 0.024f));
                    b.Add("cone", "main", new Vector3(0f, -0.05f, 0f), new Vector3(0.14f, 0.08f, 0.024f), new Vector3(0f, 0f, 180f));
                    b.Add("box", "main", new Vector3(0f, 0.0f, -0.016f), new Vector3(0.02f, 0.14f, 0.012f));
                    break;
                case CrowdBattle.Marks.March:
                    // 땅과 나란한 » 두 개 (앞 = +Z)
                    for (int k = 0; k < 2; k++)
                    {
                        float z = k * 0.07f;
                        b.Add("box", "main", new Vector3(-0.03f, 0f, z), new Vector3(0.026f, 0.02f, 0.09f), new Vector3(0f, 45f, 0f));
                        b.Add("box", "main", new Vector3(0.03f, 0f, z), new Vector3(0.026f, 0.02f, 0.09f), new Vector3(0f, -45f, 0f));
                    }
                    break;
                case CrowdBattle.Marks.Confused:
                    // 머리 위 빙글 도는 동그라미 (땅과 나란한 고리 + 작은 별 셋)
                    for (int k = 0; k < 10; k++)
                    {
                        float a = k / 10f * Mathf.PI * 2f;
                        b.Add("sphere", "main", new Vector3(Mathf.Cos(a) * 0.1f, 0f, Mathf.Sin(a) * 0.1f), new Vector3(0.026f, 0.026f, 0.026f));
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k / 3f * Mathf.PI * 2f + 0.3f;
                        b.Add("cone", "main", new Vector3(Mathf.Cos(a) * 0.1f, 0.03f, Mathf.Sin(a) * 0.1f), new Vector3(0.04f, 0.05f, 0.04f));
                    }
                    break;
                case CrowdBattle.Marks.Panic:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        b.Add("box", "main", new Vector3(0.04f * s, 0.035f, 0f), new Vector3(0.034f, 0.11f, 0.026f));
                        b.Add("sphere", "main", new Vector3(0.04f * s, -0.055f, 0f), new Vector3(0.038f, 0.038f, 0.03f));
                    }
                    break;
            }
            return Built[mark] = b.Build("mark_" + mark);
        }

        /// <summary>화살표 (dir 1 = 위, −1 = 아래): 자루 + 납작한 뾰족 머리.</summary>
        private static void Arrow(LookMeshes.Builder b, float dir)
        {
            b.Add("box", "main", new Vector3(0f, -0.03f * dir, 0f), new Vector3(0.05f, 0.12f, 0.03f));
            b.Add("cone", "main", new Vector3(0f, 0.07f * dir, 0f), new Vector3(0.14f, 0.09f, 0.04f), dir < 0 ? new Vector3(0f, 0f, 180f) : Vector3.zero);
        }

        /// <summary>
        /// 한 병사의 표시 행렬 (애니메이션 포함): 머리 위 head 높이 · 시간 now · 병사마다 다른 phase · 걷는 쪽 yaw. alt = 깜박일 때 두 번째 색.
        /// ↑ = 위로 떠오름, ↓ = 아래로 가라앉음, 혼란 = 빙글 돎, 패닉 = 크게 콩닥 + 빨강 · 흰색 번갈아, 공격 · 방어 = 숨쉬듯 커졌다 작아짐 + 밝게 깜박, 강행군 = 걷는 쪽으로 흐름.
        /// </summary>
        public static Matrix4x4 Pose(int mark, Vector3 head, Quaternion billboard, float yaw, float now, float phase, float scale, out bool alt)
        {
            alt = false;
            var pos = head;
            var rot = billboard;
            float s = scale;
            float f = Frac(now * 0.9f + phase);
            switch (mark)
            {
                case CrowdBattle.Marks.Inspired: pos.y += f * 0.22f * scale; break;
                case CrowdBattle.Marks.MoraleDown: pos.y += (0.18f - f * 0.18f) * scale; break;
                case CrowdBattle.Marks.Confused: rot = Quaternion.Euler(0f, now * 260f + phase * 360f, 0f); break;
                case CrowdBattle.Marks.Panic:
                    s *= 1f + 0.28f * Mathf.Abs(Mathf.Sin((now + phase) * 10f));
                    alt = ((int)((now + phase) * 6f)) % 2 == 0;
                    break;
                case CrowdBattle.Marks.AttackUp:
                case CrowdBattle.Marks.DefenseUp:
                    s *= 1f + 0.14f * Mathf.Sin((now + phase) * 6f);
                    alt = ((int)((now + phase) * 2.5f)) % 2 == 0;
                    break;
                case CrowdBattle.Marks.March:
                    rot = Quaternion.Euler(0f, yaw, 0f);
                    pos += rot * Vector3.forward * ((Frac(now * 1.5f + phase) - 0.5f) * 0.2f * scale);
                    break;
            }
            return Matrix4x4.TRS(pos, rot, Vector3.one * s);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}

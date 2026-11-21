using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 대열 모양 (R-125): 병사 수만큼 자리(앞 = +y, 오른쪽 = +x, 칸)를 만든다. 무게중심이 (0, 0).
    /// line = 얇은 일자(2줄), thick = 두툼한 일자(5줄), wedge = 돌격 삼각형(꼭짓점이 앞), vee = V자(벌린 쪽이 앞, 두 팔이 감싼다).
    /// </summary>
    public static class CrowdFormations
    {
        public const string Line = "line", Thick = "thick", Wedge = "wedge", Vee = "vee";
        public static readonly string[] All = { Line, Thick, Wedge, Vee };

        public static bool IsKnown(string id) => id == Line || id == Thick || id == Wedge || id == Vee;

        public static List<Vec> Slots(string id, int count, Fix spacing)
        {
            var raw = new List<Vec>(count);
            switch (id)
            {
                case Thick: Rows(raw, count, 5, spacing); break;
                case Wedge: WedgeShape(raw, count, spacing); break;
                case Vee: VeeShape(raw, count, spacing); break;
                default: Rows(raw, count, 2, spacing); break;
            }
            return Centered(raw);
        }

        /// <summary>대열 맨 뒤 (가장 작은 y) — 장수 자리를 정할 때.</summary>
        public static Fix BackOf(List<Vec> slots)
        {
            Fix min = Fix.Zero;
            foreach (var s in slots) if (s.Y < min) min = s.Y;
            return min;
        }

        private static void Rows(List<Vec> into, int count, int rows, Fix sp)
        {
            int cols = (count + rows - 1) / rows;
            for (int i = 0; i < count; i++)
            {
                int r = i / cols, c = i % cols;
                into.Add(new Vec(sp * (2 * c - (cols - 1)) / 2, -sp * r));
            }
        }

        private static void WedgeShape(List<Vec> into, int count, Fix sp)
        {
            // 줄 k (0 = 꼭짓점)에 2k+1명, 앞에서 뒤로
            int k = 0;
            while (into.Count < count)
            {
                int width = 2 * k + 1;
                for (int c = 0; c < width && into.Count < count; c++)
                    into.Add(new Vec(sp * (2 * c - (width - 1)) / 2, -sp * k));
                k++;
            }
        }

        private static void VeeShape(List<Vec> into, int count, Fix sp)
        {
            // 가운데 뒤(0,0)에서 앞 왼쪽 · 앞 오른쪽으로 뻗은 두 팔, 팔 두께 3줄. 앞 = +y
            const int thick = 3;
            Fix ax = Fix.FromRatio(7, 10), ay = Fix.FromRatio(7, 10); // 팔 방향 (45도)
            int step = 0;
            while (into.Count < count)
            {
                for (int r = 0; r < thick && into.Count < count; r++)
                {
                    // 팔 위 위치 + 팔 바깥쪽으로 두께
                    Fix along = sp * step, off = sp * r;
                    into.Add(new Vec(-(ax * along) - ay * off, ay * along - ax * off));
                    if (into.Count < count && (step > 0 || r > 0)) into.Add(new Vec(ax * along + ay * off, ay * along - ax * off));
                }
                step++;
            }
        }

        private static List<Vec> Centered(List<Vec> raw)
        {
            if (raw.Count == 0) return raw;
            Fix sx = Fix.Zero, sy = Fix.Zero;
            foreach (var p in raw) { sx += p.X; sy += p.Y; }
            var c = new Vec(sx / raw.Count, sy / raw.Count);
            var res = new List<Vec>(raw.Count);
            foreach (var p in raw) res.Add(p - c);
            return res;
        }
    }
}

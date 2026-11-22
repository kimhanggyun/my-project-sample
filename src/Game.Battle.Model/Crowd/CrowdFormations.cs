using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 대열 모양 (R-125): 병사 수만큼 자리(앞 = +y, 오른쪽 = +x, 칸)를 만든다. 무게중심이 (0, 0).
    /// line = 얇은 일자(2줄), thick = 두툼한 일자(6줄), wedge = 돌격 삼각형(꼭짓점이 앞), vee = V자(벌린 쪽이 앞, 두 팔이 감싼다).
    /// R-141 (2026-11-22 사용자, 아이콘 진형 11개): single = 1줄 (가장 넓게), echelonL / echelonR = 사선 (왼쪽 / 오른쪽을 물려 뺌),
    /// split = 분진 (가운데를 비운 두 덩이), circle = 원진 (바깥 둘레부터 채운 원), loose = 산개 (간격 넓은 3줄), column = 종대 (폭 4명).
    /// </summary>
    public static class CrowdFormations
    {
        public const string Line = "line", Thick = "thick", Wedge = "wedge", Vee = "vee";
        public const string Single = "single", EchelonL = "echelonL", EchelonR = "echelonR", Split = "split", Circle = "circle", Loose = "loose", Column = "column";
        /// <summary>고르기 순서 (아이콘 줄): 쐐기 · 1줄 · 2줄 · 두툼 · V자 · 좌 사선 · 우 사선 · 분진 · 원진 · 산개 · 종대.</summary>
        public static readonly string[] All = { Wedge, Single, Line, Thick, Vee, EchelonL, EchelonR, Split, Circle, Loose, Column };

        public static bool IsKnown(string id) => System.Array.IndexOf(All, id) >= 0;

        public static List<Vec> Slots(string id, int count, Fix spacing)
        {
            var raw = new List<Vec>(count);
            switch (id)
            {
                case Thick: Rows(raw, count, 6, spacing); break;
                case Wedge: WedgeShape(raw, count, spacing); break;
                case Vee: VeeShape(raw, count, spacing); break;
                case Single: Rows(raw, count, 1, spacing); break;
                case EchelonL: Echelon(raw, count, spacing, Fix.FromRatio(1, 2)); break;
                case EchelonR: Echelon(raw, count, spacing, -Fix.FromRatio(1, 2)); break;
                case Split: SplitShape(raw, count, spacing); break;
                case Circle: CircleShape(raw, count, spacing); break;
                case Loose: Rows(raw, count, 3, spacing * Fix.FromRatio(9, 5)); break;
                case Column: Rows(raw, count, System.Math.Max(1, (count + 3) / 4), spacing); break;
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

        /// <summary>사선: 2줄을 비스듬히 — slope > 0이면 오른쪽이 앞 (왼쪽을 물려 뺌), < 0이면 반대.</summary>
        private static void Echelon(List<Vec> into, int count, Fix sp, Fix slope)
        {
            var tmp = new List<Vec>();
            Rows(tmp, count, 2, sp);
            foreach (var p in tmp) into.Add(new Vec(p.X, p.Y + p.X * slope));
        }

        /// <summary>분진: 4줄 덩이 둘, 사이를 한 덩이 폭의 0.6만큼 비움 (들어오는 적을 양쪽에서 낌).</summary>
        private static void SplitShape(List<Vec> into, int count, Fix sp)
        {
            int left = (count + 1) / 2, right = count - left;
            int cols = (left + 3) / 4;
            Fix blockW = sp * cols;
            Fix shift = blockW / 2 + blockW * Fix.FromRatio(3, 10);
            var a = new List<Vec>(); Rows(a, left, 4, sp);
            var b = new List<Vec>(); Rows(b, right, 4, sp);
            foreach (var p in a) into.Add(new Vec(p.X - shift, p.Y));
            foreach (var p in b) into.Add(new Vec(p.X + shift, p.Y));
        }

        /// <summary>원진: 격자 점 가운데 원 안의 것을 바깥 둘레부터 채움 (사람이 적으면 가운데가 빔). 같은 거리면 (x, y) 순.</summary>
        private static void CircleShape(List<Vec> into, int count, Fix sp)
        {
            int r = 1;
            while (true)
            {
                int n = 0;
                for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) if (i * i + j * j <= r * r) n++;
                if (n >= count) break;
                r++;
            }
            var pts = new List<(int d, int i, int j)>();
            for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++) { int d = i * i + j * j; if (d <= r * r) pts.Add((d, i, j)); }
            pts.Sort((p, q) => p.d != q.d ? q.d.CompareTo(p.d) : p.i != q.i ? p.i.CompareTo(q.i) : p.j.CompareTo(q.j));
            for (int k = 0; k < count; k++) into.Add(new Vec(sp * pts[k].i, sp * pts[k].j));
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
            Fix ax = Fix.FromRatio(1, 2), ay = Fix.FromRatio(87, 100); // 팔 방향 (앞에서 30도 벌어짐 — 넓게 퍼지지 않고 감싸게)
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

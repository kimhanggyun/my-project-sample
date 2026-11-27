using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 대열 모양 (R-125 · R-141 · R-157): 병사 수만큼 자리(앞 = +y, 오른쪽 = +x, 칸)를 만든다. 무게중심이 (0, 0).
    /// R-157 (2026-11-27 사용자): 진형은 줄 수가 아니라 **정해진 길이(모양)**. 기준 앞줄 길이 = front명 (formationFront 50, 병종 간격만큼의 길이),
    /// 사람이 늘면 뒤로 두꺼워진다 — 일자 rows(5)줄까지, 두툼 = 길이 ½ · 2 × rows줄까지, 더 두툼 = 길이 ⅓ · 3 × rows줄까지, 그보다 많으면 옆으로 넓어짐.
    /// 줄마다 고르게 (한 줄 인원 = 전체 ÷ 줄 수 올림). V자 = 두 팔 길이 각 ½, 팔이 rows줄까지 두꺼워진 뒤 길어짐. 쐐기 = 꼭짓점에서 줄마다 2명씩 넓어지다 길이 ½에서 멈추고 뒤로.
    /// 분진 = 덩이 둘 각 길이 ¼. 원진 = 둘레가 기준 길이인 원을 바깥부터 채우고 꽉 차면 커짐. 산개 = 일자를 넓은 간격으로. 사선 = 일자를 비스듬히.
    /// single = 1줄 (옆으로만), column = 종대 (폭 4명).
    /// </summary>
    public static class CrowdFormations
    {
        public const string Line = "line", Thick = "thick", Wedge = "wedge", Vee = "vee", Thicker = "thicker";
        public const string Single = "single", EchelonL = "echelonL", EchelonR = "echelonR", Split = "split", Circle = "circle", Loose = "loose", Column = "column";
        /// <summary>고르기 순서 (아이콘 줄): 쐐기 · 1줄 · 일자 · 두툼 · 더 두툼 · V자 · 좌 사선 · 우 사선 · 분진 · 원진 · 산개 · 종대.</summary>
        public static readonly string[] All = { Wedge, Single, Line, Thick, Thicker, Vee, EchelonL, EchelonR, Split, Circle, Loose, Column };

        /// <summary>데이터가 없을 때 기준 앞줄 사람 수, 일자 최대 줄 수.</summary>
        public const int DefaultFront = 50;
        public const int DefaultRows = 5;

        public static bool IsKnown(string id) => System.Array.IndexOf(All, id) >= 0;

        public static List<Vec> Slots(string id, int count, Fix spacing) => Slots(id, count, spacing, DefaultFront, DefaultRows);

        /// <param name="front">기준 앞줄 사람 수 (길이 = 사람 × 간격).</param>
        /// <param name="rows">일자가 두꺼워지는 최대 줄 수 (두툼 2배 · 더 두툼 3배).</param>
        public static List<Vec> Slots(string id, int count, Fix spacing, int front, int rows)
        {
            var raw = new List<Vec>(count);
            int cols = System.Math.Max(1, front); // 기준 길이에 서는 사람 수
            rows = System.Math.Max(1, rows);
            switch (id)
            {
                case Thick: Block(raw, count, System.Math.Max(1, cols / 2), rows * 2, spacing); break;
                case Thicker: Block(raw, count, System.Math.Max(1, cols / 3), rows * 3, spacing); break;
                case Wedge: WedgeShape(raw, count, spacing, System.Math.Max(1, cols / 2)); break;
                case Vee: VeeShape(raw, count, spacing, System.Math.Max(1, cols / 2), rows); break;
                case Single: Rows(raw, count, 1, spacing); break;
                case EchelonL: Echelon(raw, count, spacing, Fix.FromRatio(1, 2), cols, rows); break;
                case EchelonR: Echelon(raw, count, spacing, -Fix.FromRatio(1, 2), cols, rows); break;
                case Split: SplitShape(raw, count, spacing, System.Math.Max(1, cols / 4), rows); break;
                case Circle: CircleShape(raw, count, spacing, cols); break;
                case Loose:
                    {
                        Fix wide = spacing * Fix.FromRatio(9, 5);
                        Block(raw, count, System.Math.Max(1, cols * 5 / 9), rows, wide); // 같은 길이를 넓은 간격으로
                        break;
                    }
                case Column: Rows(raw, count, System.Math.Max(1, (count + 3) / 4), spacing); break;
                default: Block(raw, count, cols, rows, spacing); break;
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

        /// <summary>정해진 길이의 덩이: 줄 수 = 사람 ÷ 길이 올림 (최대 maxRows), 줄마다 고르게. maxRows를 넘으면 옆으로 넓어짐.</summary>
        private static void Block(List<Vec> into, int count, int cols, int maxRows, Fix sp)
        {
            int rows = System.Math.Max(1, System.Math.Min(maxRows, (count + cols - 1) / cols));
            Rows(into, count, rows, sp);
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

        /// <summary>사선: 일자를 비스듬히 — slope > 0이면 오른쪽이 앞 (왼쪽을 물려 뺌), < 0이면 반대.</summary>
        private static void Echelon(List<Vec> into, int count, Fix sp, Fix slope, int cols, int rows)
        {
            var tmp = new List<Vec>();
            Block(tmp, count, cols, rows, sp);
            foreach (var p in tmp) into.Add(new Vec(p.X, p.Y + p.X * slope));
        }

        /// <summary>분진: 길이 ¼ 덩이 둘 (rows줄까지 두꺼워짐), 사이를 한 덩이 폭의 0.6만큼 비움 (들어오는 적을 양쪽에서 낌).</summary>
        private static void SplitShape(List<Vec> into, int count, Fix sp, int cols, int rows)
        {
            int left = (count + 1) / 2, right = count - left;
            var a = new List<Vec>(); Block(a, left, cols, rows, sp);
            var b = new List<Vec>(); Block(b, right, cols, rows, sp);
            Fix minX = Fix.Zero, maxX = Fix.Zero;
            foreach (var p in a) { if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X; }
            Fix blockW = maxX - minX + sp;
            Fix shift = blockW / 2 + blockW * Fix.FromRatio(3, 10);
            foreach (var p in a) into.Add(new Vec(p.X - shift, p.Y));
            foreach (var p in b) into.Add(new Vec(p.X + shift, p.Y));
        }

        /// <summary>원진: 둘레가 기준 길이(cols명)인 원 — 격자 점 가운데 원 안의 것을 바깥 둘레부터 채움. 꽉 차면 원이 커짐. 같은 거리면 (x, y) 순.</summary>
        private static void CircleShape(List<Vec> into, int count, Fix sp, int cols)
        {
            int r = System.Math.Max(1, (cols * 100 + 314) / 628); // 둘레 2πr ≈ cols
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

        /// <summary>쐐기: 줄 k (0 = 꼭짓점)에 2k+1명씩 넓어지다가 폭 maxWidth(길이 ½)에서 멈추고 그 폭으로 뒤로.</summary>
        private static void WedgeShape(List<Vec> into, int count, Fix sp, int maxWidth)
        {
            int cap = System.Math.Max(1, maxWidth | 1);
            int k = 0;
            while (into.Count < count)
            {
                int width = System.Math.Min(2 * k + 1, cap);
                for (int c = 0; c < width && into.Count < count; c++)
                    into.Add(new Vec(sp * (2 * c - (width - 1)) / 2, -sp * k));
                k++;
            }
        }

        /// <summary>V자: 가운데 뒤에서 앞 왼쪽 · 앞 오른쪽으로 뻗은 두 팔 (길이 armLen명), 팔이 maxThick줄까지 두꺼워진 뒤 길어짐. 앞 = +y.</summary>
        private static void VeeShape(List<Vec> into, int count, Fix sp, int armLen, int maxThick)
        {
            int perArm = (count + 1) / 2;
            int thick = System.Math.Max(1, System.Math.Min(maxThick, (perArm + armLen - 1) / armLen));
            int len = (perArm + thick - 1) / thick;
            Fix ax = Fix.FromRatio(1, 2), ay = Fix.FromRatio(87, 100); // 팔 방향 (앞에서 30도 벌어짐)
            Fix tip = sp / 2;
            for (int step = 0; step < len && into.Count < count; step++)
                for (int r = 0; r < thick && into.Count < count; r++)
                {
                    Fix along = sp * step, off = sp * r;
                    into.Add(new Vec(-(ax * along) - ay * off - tip, ay * along - ax * off));
                    if (into.Count < count) into.Add(new Vec(ax * along + ay * off + tip, ay * along - ax * off));
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

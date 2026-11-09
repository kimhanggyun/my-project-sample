using System.Collections.Generic;
using Game.Foundation.Geo;

namespace Game.Territory
{
    /// <summary>
    /// 좌표 → 지역 (territory.md 3장, verification R-45). 실제 서비스는 서버가 판정한다 (같은 코드). 정수 계산만.
    /// </summary>
    public sealed class RegionLocator
    {
        private readonly TerritoryDefs _defs;

        public RegionLocator(TerritoryDefs defs) { _defs = defs; }

        /// <summary>점이 들어 있는 지역을 큰 것부터 작은 것까지 (시·도 → … → 가장 작은 지역). 없으면 빈 목록.</summary>
        public List<RegionDef> Locate(GeoPoint p)
        {
            var path = new List<RegionDef>();
            string parent = null;
            foreach (var level in RegionLevels.Order)
            {
                RegionDef found = null;
                foreach (var r in _defs.Ordered)
                    if (r.Level == level && r.Parent == parent && Contains(r.Boundary, p)) { found = r; break; }
                if (found == null) break;
                path.Add(found);
                parent = found.Id;
            }
            return path;
        }

        /// <summary>가장 작은 지역 (없으면 null).</summary>
        public RegionDef Current(GeoPoint p)
        {
            var path = Locate(p);
            return path.Count == 0 ? null : path[path.Count - 1];
        }

        /// <summary>
        /// 공격할 수 있는 지역 (territory.md 3장, 제안): 지금 있는 지역과 이웃 지역, 그리고 그 위 단계들의 지금 지역·이웃.
        /// 단계마다 한 줄 (큰 단계 먼저). 먼 지역은 아직 없음 (연합원이 있는 곳을 통해서 — 연합 단계).
        /// </summary>
        public List<RegionDef> Attackable(GeoPoint p)
        {
            var list = new List<RegionDef>();
            foreach (var r in Locate(p))
            {
                list.Add(r);
                foreach (var n in r.Neighbors) list.Add(_defs.Regions[n]);
            }
            return list;
        }

        /// <summary>
        /// 점이 다각형 안에 있는가 (경계선 위는 안). 반직선 교차 수: 경도 방향으로 가로 반직선을 그어 변과 만나는 수가 홀수면 안.
        /// 곱셈은 백만분의 1도 정수 (변 길이 × 차이 ≤ 360e6 × 360e6 &lt; long 범위).
        /// </summary>
        public static bool Contains(IReadOnlyList<GeoPoint> poly, GeoPoint p)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                long yi = poly[i].LatE6, xi = poly[i].LonE6, yj = poly[j].LatE6, xj = poly[j].LonE6;
                if (OnSegment(xi, yi, xj, yj, p.LonE6, p.LatE6)) return true;
                if ((yi > p.LatE6) == (yj > p.LatE6)) continue;
                // 교점의 경도 x* = xi + (p.y - yi)(xj - xi)/(yj - yi). p.x < x* 를 나눗셈 없이 비교 (분모 부호에 맞춰)
                long dy = yj - yi;
                long lhs = (p.LonE6 - xi) * dy;
                long rhs = (p.LatE6 - yi) * (xj - xi);
                if (dy > 0 ? lhs < rhs : lhs > rhs) inside = !inside;
            }
            return inside;
        }

        private static bool OnSegment(long x1, long y1, long x2, long y2, long px, long py)
        {
            long cross = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
            if (cross != 0) return false;
            return px >= System.Math.Min(x1, x2) && px <= System.Math.Max(x1, x2) && py >= System.Math.Min(y1, y2) && py <= System.Math.Max(y1, y2);
        }
    }
}

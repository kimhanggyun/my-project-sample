using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Math;

namespace Game.Battle.Model.Field
{
    /// <summary>
    /// 전장: 맵 + 지형 이동 규칙. 병종별 진입 가능 여부, 이동 비용, 높이, 길 찾기.
    /// 규칙 스위치 terrainRules가 꺼지면 모든 지형의 이동 비용은 1이고, 누구도 못 지나는 칸('*')만 막힌다.
    /// </summary>
    public sealed class Battlefield
    {
        private static readonly Fix Diagonal = Fix.Parse("1.4142");

        public MapDef Map { get; }
        private readonly DefinitionSet _defs;
        private readonly bool _terrainRules;
        private readonly Fix _minCost;

        public Battlefield(MapDef map, DefinitionSet defs, bool terrainRulesEnabled)
        {
            Map = map;
            _defs = defs;
            _terrainRules = terrainRulesEnabled;
            foreach (var t in map.TerrainIds)
                if (!defs.TerrainRules.ContainsKey(t)) throw new DataException($"맵 '{map.Id}': 이동 규칙이 없는 지형 '{t}'.");
            _minCost = Fix.OneValue;
            if (terrainRulesEnabled)
                foreach (var t in map.TerrainIds)
                {
                    var r = defs.TerrainRules[t];
                    _minCost = Fix.Min(_minCost, r.Cost);
                    foreach (var c in r.CostByTag.Values) _minCost = Fix.Min(_minCost, c);
                }
        }

        public int Width => Map.Width;
        public int Height => Map.Height;

        /// <summary>전투 중 바뀐 지형 (화계로 불탄 숲 등). 칸 순서 y × 폭 + x, 없으면 맵 그대로.</summary>
        private string[] _changed;

        public string TerrainAt(Cell c)
        {
            int x = Clamp(c.X, Width), y = Clamp(c.Y, Height);
            return _changed?[y * Width + x] ?? Map.TerrainAt(x, y);
        }

        /// <summary>지형을 바꾼다 (지형 변화). 길은 다음에 찾을 때 반영된다.</summary>
        public void SetTerrain(Cell c, string terrainId)
        {
            if (!Map.InBounds(c.X, c.Y)) return;
            if (!_defs.TerrainRules.ContainsKey(terrainId)) throw new DataException($"이동 규칙이 없는 지형 '{terrainId}'.");
            if (_changed == null) _changed = new string[Width * Height];
            _changed[c.Y * Width + c.X] = terrainId;
        }

        /// <summary>바뀐 칸 (칸 번호 순서, 지문·화면용).</summary>
        public IEnumerable<(int index, string terrain)> ChangedCells()
        {
            if (_changed == null) yield break;
            for (int i = 0; i < _changed.Length; i++) if (_changed[i] != null) yield return (i, _changed[i]);
        }
        public string TerrainAt(Vec p) => TerrainAt(Cell.Of(p));
        public TerrainRuleDef RuleAt(Cell c) => _defs.TerrainRules[TerrainAt(c)];
        public int HeightAt(Vec p) => RuleAt(Cell.Of(p)).Height;

        public bool CanEnter(Cell c, TroopTypeDef troop)
        {
            if (!Map.InBounds(c.X, c.Y)) return false;
            var rule = RuleAt(c);
            return _terrainRules ? !rule.Blocks(troop) : !rule.BlocksEveryone;
        }

        public bool CanEnter(Cell c, IReadOnlyList<TroopTypeDef> members)
        {
            if (!Map.InBounds(c.X, c.Y)) return false;
            foreach (var m in members) if (!CanEnter(c, m)) return false;
            return members.Count > 0 || !RuleAt(c).BlocksEveryone;
        }

        /// <summary>군단 이동 비용 = 구성 병종 중 가장 느린 값.</summary>
        public Fix CostAt(Cell c, IReadOnlyList<TroopTypeDef> members)
        {
            if (!_terrainRules) return Fix.OneValue;
            var rule = RuleAt(c);
            Fix cost = rule.Cost;
            bool any = false;
            foreach (var m in members)
            {
                Fix v = rule.CostFor(m);
                cost = any ? Fix.Max(cost, v) : v;
                any = true;
            }
            return cost;
        }

        /// <summary>
        /// A* 길 찾기 (8방향, 막힌 모서리는 대각선으로 못 지남). 시작 칸은 빼고 돌려준다.
        /// 목표에 못 가면 목표에 가장 가까운 도달 가능한 칸까지의 길. 이웃 순서·동점 처리가 고정이라 결정적이다.
        /// </summary>
        public List<Cell> FindPath(Cell from, Cell to, IReadOnlyList<TroopTypeDef> members)
        {
            int w = Width, h = Height, n = w * h;
            from = new Cell(Clamp(from.X, w), Clamp(from.Y, h));
            to = new Cell(Clamp(to.X, w), Clamp(to.Y, h));
            var g = new long[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = long.MaxValue; parent[i] = -1; }

            int start = from.Y * w + from.X, goal = to.Y * w + to.X;
            var open = new SortedSet<(long f, long h, int idx)>();
            g[start] = 0;
            long h0 = Heuristic(from, to);
            open.Add((h0, h0, start));
            int best = start;
            long bestH = h0;

            while (open.Count > 0)
            {
                var cur = open.Min;
                open.Remove(cur);
                int ci = cur.idx;
                if (closed[ci]) continue;
                closed[ci] = true;
                if (cur.h < bestH) { bestH = cur.h; best = ci; }
                if (ci == goal) { best = goal; break; }
                int cx = ci % w, cy = ci / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    var nc = new Cell(nx, ny);
                    if (!CanEnter(nc, members)) continue;
                    bool diag = Dx[k] != 0 && Dy[k] != 0;
                    if (diag && (!CanEnter(new Cell(cx + Dx[k], cy), members) || !CanEnter(new Cell(cx, cy + Dy[k]), members))) continue;
                    int ni = ny * w + nx;
                    if (closed[ni]) continue;
                    Fix step = CostAt(nc, members);
                    if (diag) step = step * Diagonal;
                    long ng = g[ci] + step.Raw;
                    if (ng >= g[ni]) continue;
                    g[ni] = ng;
                    parent[ni] = ci;
                    long nh = Heuristic(nc, to);
                    open.Add((ng + nh, nh, ni));
                }
            }

            var path = new List<Cell>();
            for (int i = best; i != start && i != -1; i = parent[i]) path.Add(new Cell(i % w, i / w));
            path.Reverse();
            return path;
        }

        private long Heuristic(Cell a, Cell b)
        {
            long dx = System.Math.Abs(a.X - b.X), dy = System.Math.Abs(a.Y - b.Y);
            long diag = System.Math.Min(dx, dy), straight = System.Math.Max(dx, dy) - diag;
            Fix cells = Fix.FromInt(straight) + Fix.FromInt(diag) * Diagonal;
            return (cells * _minCost).Raw;
        }

        private static int Clamp(int v, int size) => v < 0 ? 0 : (v >= size ? size - 1 : v);

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };
    }
}

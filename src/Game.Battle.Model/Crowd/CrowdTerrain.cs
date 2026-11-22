using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 샘플전투2 지형 (R-133, battle-crowd.md 3.12): 기존 전장 맵(Maps/*.json, 위 줄 = 적 쪽)을 맵 1칸 = mapCellSize 전장 칸으로 깐다.
    /// 지형 규칙은 기존 데이터 그대로 — 이동 비용 · 못 가는 지형 · 높이 (terrainRules.json), 병종 전투 보정 %p (terrainCombat.json, 치는 병사가 선 지형).
    /// 전장 좌표 (x 오른쪽, y 적 쪽) ↔ 맵 칸 (열 = x / 칸, 행 = 맵 높이 − 1 − y / 칸).
    /// </summary>
    /// <summary>
    /// 막는 소품 하나 (R-134): 나무 기둥 · 바위. 병사는 들어가지 못하고 비켜 간다. 자리는 맵 칸 번호로 정해 늘 같다 (화면도 이 자리에 그림).
    /// </summary>
    public sealed class CrowdObstacle
    {
        public string Terrain;
        public Vec Pos;
        public Fix Radius;
        /// <summary>크기 배수 (반지름 · 그림 크기에 같이 곱함).</summary>
        public Fix Size;
        /// <summary>그림 고르기 · 방향 (보이기만).</summary>
        public int Variant;
        public int YawDeg;
    }

    /// <summary>지형마다 막는 소품 설정 (crowd.json obstacles): 맵 칸 하나에 몇 개 · 기본 반지름.</summary>
    public sealed class CrowdObstacleSpec
    {
        public Fix PerCell;
        public Fix Radius;
    }

    public sealed class CrowdTerrain
    {
        public MapDef Map { get; }
        public Fix CellSize { get; }
        public Fix Width => CellSize * Map.Width;
        public Fix Depth => CellSize * Map.Height;
        private readonly IReadOnlyDictionary<string, TerrainRuleDef> _rules;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Fix>> _combat;
        private readonly Dictionary<string, Vec> _snapCache = new Dictionary<string, Vec>();

        public CrowdTerrain(MapDef map, IReadOnlyDictionary<string, TerrainRuleDef> rules, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Fix>> combat, Fix cellSize)
        {
            Map = map;
            _rules = rules ?? new Dictionary<string, TerrainRuleDef>();
            _combat = combat ?? new Dictionary<string, IReadOnlyDictionary<string, Fix>>();
            CellSize = cellSize;
        }

        // ---- 칸 ----

        /// <summary>전장 자리 → 맵 칸 (밖이면 가장자리 칸).</summary>
        public void CellOf(Vec p, out int col, out int row)
        {
            col = (int)(p.X / CellSize).FloorToLong();
            int up = (int)(p.Y / CellSize).FloorToLong();
            if (col < 0) col = 0; else if (col >= Map.Width) col = Map.Width - 1;
            if (up < 0) up = 0; else if (up >= Map.Height) up = Map.Height - 1;
            row = Map.Height - 1 - up;
        }

        public int CellIndex(Vec p) { CellOf(p, out int c, out int r); return r * Map.Width + c; }

        /// <summary>맵 칸 가운데의 전장 자리.</summary>
        public Vec CellCenter(int col, int row) => new Vec(CellSize * col + CellSize / 2, CellSize * (Map.Height - 1 - row) + CellSize / 2);

        public string TerrainAt(Vec p) { CellOf(p, out int c, out int r); return Map.TerrainAt(c, r); }

        public TerrainRuleDef RuleAt(Vec p) => _rules.TryGetValue(TerrainAt(p), out var r) ? r : null;

        // ---- 병종별 ----

        /// <summary>그 병종이 그 칸에 못 들어감 (강 · 절벽 = 모두, 산 = 기병 …, 물 = 수군 아니면 모두).</summary>
        public bool Blocks(CrowdTroop troop, int col, int row)
        {
            if (!_rules.TryGetValue(Map.TerrainAt(col, row), out var rule)) return false;
            if (rule.Water) return !troop.Has(TerrainRuleDef.NavalTag);
            foreach (var t in rule.Impassable)
                if (t == TerrainRuleDef.AllTags || troop.Has(t)) return true;
            return false;
        }

        public bool Blocks(CrowdTroop troop, Vec p) { CellOf(p, out int c, out int r); return Blocks(troop, c, r); }

        /// <summary>이동 비용 배율 (태그별 값이 있으면 그중 가장 큰 것, 없으면 기본). 모르는 지형은 1.</summary>
        public Fix Cost(CrowdTroop troop, int col, int row)
        {
            if (!_rules.TryGetValue(Map.TerrainAt(col, row), out var rule)) return Fix.OneValue;
            bool found = false;
            Fix best = Fix.Zero;
            foreach (var kv in rule.CostByTag)
                if (troop.Has(kv.Key) && (!found || kv.Value > best)) { best = kv.Value; found = true; }
            Fix c = found ? best : rule.Cost;
            return c <= Fix.Zero ? Fix.OneValue : c;
        }

        /// <summary>걸음 배수 = 1 ÷ 이동 비용 (숲에 든 기병은 0.4배).</summary>
        public Fix SpeedFactor(CrowdTroop troop, Vec p) { CellOf(p, out int c, out int r); return Fix.OneValue / Cost(troop, c, r); }

        /// <summary>그 자리 지형의 병종 전투 보정 %p (병종 태그마다 표의 값을 더함, battle.md 2.16.1).</summary>
        public Fix CombatPp(CrowdTroop troop, Vec p)
        {
            if (!_combat.TryGetValue(TerrainAt(p), out var row)) return Fix.Zero;
            Fix sum = Fix.Zero;
            foreach (var tag in troop.Tags)
                if (row.TryGetValue(tag, out var pp)) sum += pp;
            return sum;
        }

        public int HeightAt(Vec p) { var r = RuleAt(p); return r != null ? r.Height : 0; }

        /// <summary>
        /// 높이 (언덕 한 단 = 1): 맵에 높이 층이 있으면 그 값 ÷ 3 (0 ~ 3, 1/3 단씩 — 계속 오르는 비탈), 없으면 지형 규칙 height.
        /// </summary>
        public Fix LevelAt(Vec p)
        {
            if (!Map.HasHeights) return Fix.FromInt(HeightAt(p));
            CellOf(p, out int c, out int r);
            return Fix.FromRatio(Map.HeightAt(c, r), 3);
        }

        /// <summary>맵 1칸 크기 = 전장 폭 ÷ 맵 폭 (24칸 맵 → 2.5, 48칸 맵 → 1.25, R-135).</summary>
        public static Fix CellSizeFor(MapDef map, Fix fieldWidth) => fieldWidth / map.Width;

        // ---- 막는 소품 (R-134) ----

        /// <summary>정수 해시 → 0 ~ 1 (고정소수점, 결정적).</summary>
        private static Fix Hash01(int cell, int salt)
        {
            unchecked
            {
                uint v = (uint)cell * 2654435761u ^ (uint)(salt + 1) * 40503u;
                v ^= v >> 13; v *= 0x5bd1e995u; v ^= v >> 15;
                return Fix.FromRaw((long)((v & 0xFFFFFF) % 10000));
            }
        }

        /// <summary>
        /// 지형마다 막는 소품을 깐다: 칸마다 perCell개 (소수 부분은 그 확률로 하나 더), 칸 안 흩뿌림 (칸 경계를 조금 넘음),
        /// 크기 × 0.75 ~ 1.25. 같은 맵이면 늘 같다.
        /// </summary>
        public List<CrowdObstacle> MakeObstacles(IReadOnlyDictionary<string, CrowdObstacleSpec> specs)
        {
            var list = new List<CrowdObstacle>();
            if (specs == null) return list;
            int mw = Map.Width, mh = Map.Height;
            for (int r = 0; r < mh; r++)
                for (int c = 0; c < mw; c++)
                {
                    string id = Map.TerrainAt(c, r);
                    if (!specs.TryGetValue(id, out var sp)) continue;
                    int i = r * mw + c;
                    Fix per = sp.PerCell * CellSize * CellSize / Fix.FromRatio(625, 100); // perCell = 2.5 × 2.5 넓이마다 (촘촘한 맵도 같은 빽빽함)
                    int whole = (int)per.FloorToLong();
                    int count = whole + (Hash01(i, 99) < per - Fix.FromInt(whole) ? 1 : 0);
                    for (int k = 0; k < count; k++)
                    {
                        Fix fx = Fix.FromRatio(-15, 100) + Fix.FromRatio(130, 100) * Hash01(i, k * 4);
                        Fix fz = Fix.FromRatio(-15, 100) + Fix.FromRatio(130, 100) * Hash01(i, k * 4 + 1);
                        Fix size = Fix.FromRatio(75, 100) + Fix.FromRatio(50, 100) * Hash01(i, k * 4 + 3);
                        list.Add(new CrowdObstacle
                        {
                            Terrain = id,
                            Pos = new Vec(CellSize * (Fix.FromInt(c) + fx), CellSize * (Fix.FromInt(mh - 1 - r) + fz)),
                            Radius = sp.Radius * size,
                            Size = size,
                            Variant = (int)(Hash01(i, k * 4 + 2) * 1000).FloorToLong(),
                            YawDeg = (int)(Hash01(i, k * 4 + 5) * 360).FloorToLong(),
                        });
                    }
                }
            return list;
        }

        // ---- 길 ----

        /// <summary>두 자리 사이 곧은 길에 못 가는 칸이 없음 (반 칸마다 살핌).</summary>
        public bool Clear(CrowdTroop troop, Vec from, Vec to)
        {
            var d = to - from;
            Fix len = d.Length;
            int steps = (int)(len / (CellSize / 2)).FloorToLong() + 1;
            for (int k = 0; k <= steps; k++)
                if (Blocks(troop, from + d * Fix.FromRatio(k, steps))) return false;
            return true;
        }

        /// <summary>못 가는 칸이면 가장 가까운 갈 수 있는 칸 가운데 (같은 거리면 칸 번호 작은 것).</summary>
        public Vec Snap(CrowdTroop troop, Vec p)
        {
            if (!Blocks(troop, p)) return p;
            CellOf(p, out int c0, out int r0);
            string key = troop.Id + "|" + c0 + "|" + r0;
            if (_snapCache.TryGetValue(key, out var hit)) return hit;
            int best = -1;
            long bestD = long.MaxValue;
            for (int r = 0; r < Map.Height; r++)
                for (int c = 0; c < Map.Width; c++)
                {
                    if (Blocks(troop, c, r)) continue;
                    long d = (long)(c - c0) * (c - c0) + (long)(r - r0) * (r - r0);
                    if (d < bestD) { bestD = d; best = r * Map.Width + c; }
                }
            var v = best < 0 ? p : CellCenter(best % Map.Width, best / Map.Width);
            _snapCache[key] = v;
            return v;
        }

        /// <summary>
        /// 맵 칸 위 가장 싼 길 (8방향, 대각선 × 1.4, 칸 비용 = 이동 비용, 못 가는 칸 · 모서리 끼기 금지).
        /// 돌려주는 것은 지나갈 칸 가운데들 (시작 칸 빼고), 마지막은 to 그대로. 길이 없으면 빈 목록.
        /// 같은 값이면 칸 번호 작은 것 먼저 (결정성).
        /// </summary>
        public List<Vec> FindPath(CrowdTroop troop, Vec from, Vec to)
        {
            int w = Map.Width;
            CellOf(from, out int sc, out int sr);
            CellOf(to, out int gc, out int gr);
            int start = sr * w + sc, goal = gr * w + gc;
            var path = new List<Vec>();
            if (start == goal) { path.Add(to); return path; }
            var dist = Dijkstra(troop, start, goal, out var prev);
            if (dist[goal] == long.MaxValue) return path;
            var cells = new List<int>();
            for (int c = goal; c != start && c >= 0; c = prev[c]) cells.Add(c);
            cells.Reverse();
            for (int k = 0; k < cells.Count - 1; k++) path.Add(CellCenter(cells[k] % w, cells[k] / w));
            path.Add(to);
            return path;
        }

        /// <summary>
        /// 지름길 지도 (R-137): 맵 칸마다 goal 칸까지 남은 거리 (못 가면 long.MaxValue). 막힌 병사가 이 값이 줄어드는 이웃 칸으로 가면 가장 짧은 길로 돌아온다.
        /// </summary>
        public long[] DistanceField(CrowdTroop troop, Vec goal)
        {
            CellOf(goal, out int gc, out int gr);
            return Dijkstra(troop, gr * Map.Width + gc, -1, out _);
        }

        /// <summary>그 칸에서 지름길 지도를 따라 한 칸 (모서리 끼기 금지, 같으면 칸 번호 작은 것). 더 줄어드는 이웃이 없으면 -1.</summary>
        public int NextCell(CrowdTroop troop, long[] field, int cell)
        {
            int w = Map.Width, h = Map.Height;
            int cc = cell % w, cr = cell / w;
            int best = -1;
            long bestD = field[cell];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nc = cc + dx, nr = cr + dy;
                    if (nc < 0 || nr < 0 || nc >= w || nr >= h || Blocks(troop, nc, nr)) continue;
                    if (dx != 0 && dy != 0 && (Blocks(troop, cc + dx, cr) || Blocks(troop, cc, cr + dy))) continue;
                    int ni = nr * w + nc;
                    if (field[ni] < bestD) { bestD = field[ni]; best = ni; }
                }
            return best;
        }

        /// <summary>다익스트라 + 이진 힙 (거리, 같으면 칸 번호 작은 것 — 결정적). stop ≥ 0이면 그 칸에서 멈춤.</summary>
        private long[] Dijkstra(CrowdTroop troop, int start, int stop, out int[] prev)
        {
            int w = Map.Width, h = Map.Height, n = w * h;
            var dist = new long[n];
            prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = long.MaxValue; prev[i] = -1; }
            dist[start] = 0;
            var heap = new List<(long d, int i)> { (0, start) };
            bool Less((long d, int i) a, (long d, int i) b) => a.d < b.d || (a.d == b.d && a.i < b.i);
            void Push((long d, int i) x)
            {
                heap.Add(x);
                int k = heap.Count - 1;
                while (k > 0) { int p = (k - 1) / 2; if (!Less(heap[k], heap[p])) break; var t = heap[k]; heap[k] = heap[p]; heap[p] = t; k = p; }
            }
            (long d, int i) Pop()
            {
                var top = heap[0];
                var last = heap[heap.Count - 1];
                heap.RemoveAt(heap.Count - 1);
                if (heap.Count > 0)
                {
                    heap[0] = last;
                    int k = 0;
                    while (true)
                    {
                        int l = k * 2 + 1, r2 = l + 1, m = k;
                        if (l < heap.Count && Less(heap[l], heap[m])) m = l;
                        if (r2 < heap.Count && Less(heap[r2], heap[m])) m = r2;
                        if (m == k) break;
                        var t = heap[k]; heap[k] = heap[m]; heap[m] = t; k = m;
                    }
                }
                return top;
            }
            while (heap.Count > 0)
            {
                var (cd, cur) = Pop();
                if (done[cur] || cd != dist[cur]) continue;
                done[cur] = true;
                if (cur == stop) break;
                int cc = cur % w, cr = cur / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nc = cc + dx, nr = cr + dy;
                        if (nc < 0 || nr < 0 || nc >= w || nr >= h || Blocks(troop, nc, nr)) continue;
                        if (dx != 0 && dy != 0 && (Blocks(troop, cc + dx, cr) || Blocks(troop, cc, cr + dy))) continue; // 모서리 끼기 금지
                        int ni = nr * w + nc;
                        if (done[ni]) continue;
                        long step = Cost(troop, nc, nr).Raw * (dx != 0 && dy != 0 ? 14 : 10) / 10;
                        long nd = dist[cur] + step;
                        if (nd < dist[ni]) { dist[ni] = nd; prev[ni] = cur; Push((nd, ni)); }
                    }
            }
            return dist;
        }
    }
}

using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;

namespace Game.Battle.Model.Crowd
{
    /// <summary>
    /// 공성 명령 (R-145, 2026-11-23 사용자 "문이 열리기 전까지는 명령이 달랐으면"). 성문이 모두 닫혀 있는 동안만 쓰고, 하나라도 부서지면 야전 명령으로.
    /// 지키는 쪽: wall = 성벽에 오름 (궁병은 쏘고, 보병 · 창병은 돌을 떨어뜨리고 사다리 꼭대기를 막음) · brace = 성문 뒤에서 문을 막음.
    /// 치는 쪽: bash = 성문 부수기 · ram = 충차로 부수기 · ladder = 사다리로 성벽 오르기 · shootWall = 성벽 위 적에게 쏘기 · waitBreach = 성문 앞에서 기다렸다 부서지면 돌입.
    /// </summary>
    public static class SiegeTasks
    {
        public const string Wall = "wall", Brace = "brace", Bash = "bash", Ram = "ram", Ladder = "ladder", ShootWall = "shootWall", WaitBreach = "waitBreach";
    }

    /// <summary>사다리 (R-145): 성벽(wall) 칸 하나에 걸침. 발 = 성 밖 칸, 꼭대기 = 성벽 안쪽 성벽 위 칸.</summary>
    public sealed class CrowdLadder
    {
        public int Army;
        public Vec Foot, Top;
        /// <summary>다음 병사가 오르기 시작할 수 있는 틱.</summary>
        public int FreeTick;
        public int Climbed, Thrown;
    }

    public sealed partial class CrowdBattle
    {
        public readonly List<CrowdLadder> Ladders = new List<CrowdLadder>();
        /// <summary>성문이 처음 부서진 틱 (공성 끝 → 야전 명령), 아직이면 -1.</summary>
        public int BreachTick { get; private set; } = -1;
        public int StoneHits { get; private set; }

        /// <summary>공성 중: 성문이 있고 하나도 부서지지 않음.</summary>
        public bool SiegeActive => Gates.Count > 0 && BreachTick < 0;

        /// <summary>그 부대가 지금 고를 수 있는 공성 명령 (공성 중이 아니면 없음).</summary>
        public IReadOnlyList<string> SiegeTasksFor(int army)
        {
            var list = new List<string>();
            if (!SiegeActive || army < 0 || army >= Armies.Count) return list;
            var a = Armies[army];
            var t = a.Troop;
            if (a.Team == SiegeDefender)
            {
                if (t.Ranged) list.Add(SiegeTasks.Wall);
                else if (t.Mounted) list.Add(SiegeTasks.Brace);
                else { list.Add(SiegeTasks.Brace); list.Add(SiegeTasks.Wall); }
            }
            else
            {
                if (t.Ranged) list.Add(SiegeTasks.ShootWall);
                else if (t.Mounted) list.Add(SiegeTasks.WaitBreach);
                else { list.Add(SiegeTasks.Bash); list.Add(SiegeTasks.Ladder); list.Add(SiegeTasks.Ram); }
            }
            return list;
        }

        /// <summary>AI · 위임이 고르는 공성 명령: 지키는 쪽 궁병 · 창병 = 성벽, 보병 · 기병 = 문 막기 / 치는 쪽 궁병 = 성벽 쏘기, 기병 = 돌입 대기, 창병 = 사다리, 보병 = 성문 부수기.</summary>
        public string DefaultSiegeTask(CrowdArmy a)
        {
            var t = a.Troop;
            if (a.Team == SiegeDefender) return t.Ranged || t.Polearm ? SiegeTasks.Wall : SiegeTasks.Brace;
            return t.Ranged ? SiegeTasks.ShootWall : t.Mounted ? SiegeTasks.WaitBreach : t.Polearm ? SiegeTasks.Ladder : SiegeTasks.Bash;
        }

        /// <summary>공성 명령 (R-145). 고를 수 없는 명령이면 무시.</summary>
        public void OrderSiege(int army, string task)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            bool ok = false;
            foreach (var t in SiegeTasksFor(army)) if (t == task) ok = true;
            if (!ok) return;
            var a = Armies[army];
            var g = NearestGate(a.Center);
            if (g == null) return;
            var r = Rules;
            Fix cell = Terrain.CellSize;
            switch (task)
            {
                case SiegeTasks.Wall:
                    OrderHold(army);
                    AssignWallSpots(a);
                    break;
                case SiegeTasks.Brace:
                    OrderMove(army, Open(a, g.Center + g.Inward * (cell * 2)));
                    break;
                case SiegeTasks.Bash:
                case SiegeTasks.Ram:
                    OrderMove(army, Open(a, g.Center - g.Inward * (cell * Fix.FromRatio(3, 2))));
                    break;
                case SiegeTasks.ShootWall:
                    OrderMove(army, Open(a, g.Center - g.Inward * (RangeOf(a.Troop) * r.ShootWallRangePct / 100)));
                    break;
                case SiegeTasks.WaitBreach:
                    OrderMove(army, Open(a, g.Center - g.Inward * (cell * r.WaitBreachCells)));
                    break;
                case SiegeTasks.Ladder:
                    var l = LadderFor(a, g);
                    if (l == null) { OrderSiege(army, SiegeTasks.Bash); return; } // 걸 자리가 없으면 성문 부수기
                    OrderMove(army, Open(a, l.Foot - g.Inward * cell));
                    break;
            }
            a.SiegeTask = task;
        }

        /// <summary>공성 명령을 지움 (다른 명령을 내리면). 성벽 자리 · 오르던 길도.</summary>
        private void ClearSiege(CrowdArmy a)
        {
            a.SiegeTask = null;
            foreach (var s in a.Soldiers) { s.Spot = null; s.SpotPath = null; }
        }

        /// <summary>그 자리에서 가장 가까운 닫힌 성문 (없으면 가장 가까운 성문, 성문이 없으면 null).</summary>
        private CrowdGate NearestGate(Vec p)
        {
            CrowdGate best = null;
            Fix bestD = Fix.FromInt(999999);
            foreach (var g in Gates)
            {
                Fix d = Vec.DistanceSquared(g.Center, p) + (g.Open ? Fix.FromInt(100000) : Fix.Zero);
                if (best == null || d < bestD) { best = g; bestD = d; }
            }
            return best;
        }

        // ---- 성벽 자리 ----

        /// <summary>
        /// 성벽 배치 (R-145): 부대 가운데에서 계단으로 갈 수 있는 성벽 위 칸을 가장 가까운 적 부대(없으면 성문 밖)에 가까운 순으로, 칸마다 wallPerCell명.
        /// 병사는 id 순, 자리까지 맵 칸 길 (계단으로 오름). 길이 없으면 자리 없음 (대열대로).
        /// </summary>
        private void AssignWallSpots(CrowdArmy a)
        {
            var r = Rules;
            int w = Terrain.Map.Width, n = w * Terrain.Map.Height;
            var field = Terrain.DistanceField(a.Troop, a.Center);
            int foeArmy = NearestEnemyArmy(a.Id);
            var g = NearestGate(a.Center);
            Vec foe = foeArmy >= 0 ? Armies[foeArmy].Centroid() : g.Center - g.Inward * (Terrain.CellSize * 6);
            var cells = new List<(Fix d, int i)>();
            for (int i = 0; i < n; i++)
            {
                if (field[i] == long.MaxValue) continue;
                var c = Terrain.CellCenter(i % w, i / w);
                if (!Terrain.IsRaised(c)) continue;
                cells.Add((Vec.DistanceSquared(c, foe), i));
            }
            cells.Sort((p, q) => { int k = p.d.CompareTo(q.d); return k != 0 ? k : p.i.CompareTo(q.i); });
            var alive = new List<CrowdSoldier>();
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral) alive.Add(s);
            int per = System.Math.Max(1, r.WallPerCell);
            Fix off = Terrain.CellSize / 5;
            for (int k = 0; k < alive.Count && k / per < cells.Count; k++)
            {
                int ci = cells[k / per].i;
                var center = Terrain.CellCenter(ci % w, ci / w);
                int m = k % per;
                var spot = per == 1 ? center : center + new Vec(m % 2 == 0 ? -off : off, m / 2 % 2 == 0 ? -off : off);
                SetSpot(alive[k], spot);
            }
        }

        /// <summary>병사 한 명이 따로 갈 자리 (성벽 위 · 성 안): 맵 칸 길 (성벽 위 · 계단도 지나감).</summary>
        private void SetSpot(CrowdSoldier s, Vec spot)
        {
            var path = Terrain.FindPath(Armies[s.Army].Troop, s.Pos, spot);
            if (path.Count == 0) { s.Spot = null; s.SpotPath = null; return; }
            s.Spot = spot;
            s.SpotPath = path;
            s.SpotIdx = 0;
            s.Settled = false; s.BestDist = Fix.FromInt(9999); s.StuckTicks = 0;
        }

        /// <summary>따로 갈 자리로 가는 길의 이번 목표 (길 점에 0.5칸 안이면 다음 점).</summary>
        private Vec SpotTarget(CrowdSoldier s)
        {
            var path = s.SpotPath;
            Fix near = Terrain.CellSize / 2;
            while (s.SpotIdx < path.Count - 1 && (path[s.SpotIdx] - s.Pos).Length <= near) s.SpotIdx++;
            return path[s.SpotIdx];
        }

        // ---- 성문 막기 · 충차 ----

        /// <summary>성문 막기 % (R-145): 성문 안쪽 gateBraceRange칸 안 지키는 병사 가중치(병종별 gateBraceWeight, 없으면 1) 합 × gateBracePctPerWeight, 최대 gateBraceMaxPct.</summary>
        public Fix GateBracePct(CrowdGate g)
        {
            var r = Rules;
            var at = g.Center + g.Inward * (Terrain.CellSize * Fix.FromRatio(3, 2));
            Fix range2 = r.GateBraceRange * r.GateBraceRange;
            Fix sum = Fix.Zero;
            foreach (var s in Soldiers)
            {
                if (!s.Alive || s.IsGeneral || s.Team != g.Defender) continue;
                if (Vec.DistanceSquared(s.Pos, at) > range2) continue;
                sum += r.GateBraceWeight.TryGetValue(Armies[s.Army].Troop.BaseId, out var wgt) ? wgt : Fix.OneValue;
            }
            return Fix.Min(r.GateBraceMaxPct, sum * r.GateBracePctPerWeight);
        }

        /// <summary>충차 배수 (R-145): 그 부대가 충차 명령이고 성문 gateBraceRange칸 안에 그 부대 병사가 ramCrew명 이상이면 gateRamMult, 아니면 1.</summary>
        public Fix RamMult(CrowdArmy a, CrowdGate g)
        {
            if (a.SiegeTask != SiegeTasks.Ram) return Fix.OneValue;
            return RamManned(a, g) ? Rules.GateRamMult : Fix.OneValue;
        }

        /// <summary>충차를 미는 사람이 넉넉함 (화면이 충차를 그릴지도 이것으로).</summary>
        public bool RamManned(CrowdArmy a, CrowdGate g)
        {
            var r = Rules;
            Fix range2 = r.GateBraceRange * r.GateBraceRange;
            int crew = 0;
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral && Vec.DistanceSquared(s.Pos, g.Center) <= range2) crew++;
            return crew >= r.RamCrew;
        }

        /// <summary>충차 지붕 밑 (R-145, 2026-11-23 캡처 47: 성문 앞 충차 부대가 성벽 화살 · 돌에 녹아 무너짐): 충차 명령 부대이고 사람이 넉넉한 충차의 성문 gateBraceRange칸 안.</summary>
        private bool UnderRamRoof(CrowdSoldier s)
        {
            var a = Armies[s.Army];
            if (a.SiegeTask != SiegeTasks.Ram) return false;
            Fix range2 = Rules.GateBraceRange * Rules.GateBraceRange;
            foreach (var g in Gates)
                if (!g.Open && Vec.DistanceSquared(s.Pos, g.Center) <= range2 && RamManned(a, g)) return true;
            return false;
        }

        /// <summary>성문이 처음 부서짐 (R-145): 공성 명령을 모두 지우고 야전으로. 치던 쪽은 가장 가까운 적에게 돌입, 성벽 위 궁병은 그 자리에 남음.</summary>
        private void OnBreach()
        {
            BreachTick = Tick;
            foreach (var a in Armies)
            {
                string task = a.SiegeTask;
                if (task == null) continue;
                bool keepWall = task == SiegeTasks.Wall && a.Troop.Ranged;
                a.SiegeTask = null;
                if (!keepWall) foreach (var s in a.Soldiers) { s.Spot = null; s.SpotPath = null; }
                if (a.Leading && a.Team != SiegeDefender)
                {
                    int t = NearestEnemyArmy(a.Id);
                    if (t >= 0) OrderAttack(a.Id, t);
                }
            }
        }

        // ---- 사다리 ----

        /// <summary>
        /// 그 부대의 사다리 (없으면 걸 자리를 찾아 만듦): 성벽(wall) 칸 가운데 4방향 이웃 하나가 부대에서 갈 수 있는 성 밖 땅이고 맞은편이 성벽 위인 것,
        /// 성문 칸에서 ladderMinGateCells칸 넘게 떨어진 것 중 부대 가운데에 가장 가까운 것 (같으면 칸 번호 작은 것). 없으면 null.
        /// </summary>
        private CrowdLadder LadderFor(CrowdArmy a, CrowdGate gate)
        {
            foreach (var l in Ladders) if (l.Army == a.Id) return l;
            var r = Rules;
            int w = Terrain.Map.Width, h = Terrain.Map.Height;
            var field = Terrain.DistanceField(a.Troop, a.Center, true);
            Fix minGate = Terrain.CellSize * r.LadderMinGateCells;
            int best = -1, bestFoot = -1, bestTop = -1;
            Fix bestD = Fix.FromInt(999999);
            for (int row = 0; row < h; row++)
                for (int c = 0; c < w; c++)
                {
                    if (Terrain.Map.TerrainAt(c, row) != "wall") continue;
                    var wc = Terrain.CellCenter(c, row);
                    bool nearGate = false;
                    foreach (var g in Gates) if ((g.Center - wc).Length < minGate) nearGate = true;
                    if (nearGate) continue;
                    foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        int fx = c + dx, fy = row + dy, tx = c - dx, ty = row - dy;
                        if (fx < 0 || fy < 0 || fx >= w || fy >= h || tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                        int fi = fy * w + fx;
                        if (field[fi] == long.MaxValue || Terrain.Map.TerrainAt(tx, ty) != "wallTop") continue;
                        var foot = Terrain.CellCenter(fx, fy);
                        if (Terrain.IsRaised(foot)) continue;
                        Fix d = Vec.DistanceSquared(foot, a.Center);
                        if (best < 0 || d < bestD) { bestD = d; best = row * w + c; bestFoot = fi; bestTop = ty * w + tx; }
                    }
                }
            if (best < 0) return null;
            var lad = new CrowdLadder { Army = a.Id, Foot = Terrain.CellCenter(bestFoot % w, bestFoot / w), Top = Terrain.CellCenter(bestTop % w, bestTop / w) };
            Ladders.Add(lad);
            return lad;
        }

        /// <summary>
        /// 사다리 오르기 (병사 틱마다, R-145): 사다리 명령 부대 병사가 발에서 ladderReach칸 안이고 사다리가 비면 오르기 시작 (ladderClimbSeconds).
        /// 오르는 동안은 서 있기만 (치지도 움직이지도 않음). true면 이번 틱 이 병사는 오르는 중.
        /// </summary>
        private bool ClimbStep(CrowdSoldier s, CrowdArmy army, int i)
        {
            var r = Rules;
            if (s.ClimbEnd >= 0)
            {
                if (Tick < s.ClimbEnd) { _next[i] = s.Pos; return true; }
                // 꼭대기에 닿음: 성벽 위 지키는 병사가 막으면 떨어짐
                var l = Ladders[s.ClimbLadder];
                s.ClimbEnd = -1;
                int guards = 0;
                Fix g2 = r.LadderGuardRange * r.LadderGuardRange;
                foreach (var o in Soldiers) if (o.Alive && o.Team != s.Team && !o.IsGeneral && Vec.DistanceSquared(o.Pos, l.Top) <= g2) guards++;
                Fix push = Fix.Min(r.LadderPushMaxPct, Fix.FromInt(guards) * r.LadderPushPct);
                if (guards > 0 && _rng.Chance(push / 100))
                {
                    _damage[i] += System.Math.Max(1, s.MaxHp * r.LadderFallDamagePct / 100);
                    l.Thrown++;
                    _next[i] = s.Pos;
                    return true;
                }
                l.Climbed++;
                s.ClimbArrived = true; // 반영 뒤 꼭대기로 (걸음 막기를 지나지 않게)
                _next[i] = s.Pos;
                return true;
            }
            if (army.SiegeTask != SiegeTasks.Ladder || !SiegeActive || s.IsGeneral) return false;
            for (int k = 0; k < Ladders.Count; k++)
            {
                var l = Ladders[k];
                if (l.Army != army.Id || Tick < l.FreeTick) continue;
                if ((s.Pos - l.Foot).Length > r.LadderReach) continue;
                s.ClimbLadder = k;
                s.ClimbStart = Tick;
                s.ClimbEnd = Tick + System.Math.Max(1, (int)(r.LadderClimbSeconds / r.TickSeconds).RoundToLong());
                l.FreeTick = Tick + System.Math.Max(1, (s.ClimbEnd - Tick) / 2); // 사다리에 둘까지
                _next[i] = s.Pos;
                return true;
            }
            return false;
        }

        /// <summary>그 부대의 사다리 (없으면 null).</summary>
        public CrowdLadder LadderOf(CrowdArmy a)
        {
            foreach (var l in Ladders) if (l.Army == a.Id) return l;
            return null;
        }

        /// <summary>오른 병사를 꼭대기에 올려놓고, 성 안 성문 뒤로 가는 길을 준다 (성문을 안에서 연다).</summary>
        private void FinishClimbs()
        {
            foreach (var s in Soldiers)
            {
                if (!s.ClimbArrived) continue;
                s.ClimbArrived = false;
                if (!s.Alive) continue;
                var l = Ladders[s.ClimbLadder];
                s.Pos = l.Top;
                s.Moved = Vec.Zero;
                var g = NearestGate(l.Top);
                if (g != null && !g.Open) SetSpot(s, Terrain.Snap(Armies[s.Army].Troop, g.Center + g.Inward * Terrain.CellSize));
            }
        }

        // ---- 돌 떨어뜨리기 ----

        /// <summary>
        /// 성벽 위 지키는 보병 · 창병 (R-145): 쿨다운이 끝났으면 stoneRange칸 안 아래(1단 넘게 낮은) 적 가운데 가장 가까운 것에게 돌 (stoneSeconds마다, 맞을 확률 stoneHitChance, 피해 stoneDamage).
        /// </summary>
        private bool TryDropStone(CrowdSoldier s)
        {
            var r = Rules;
            if (s.IsGeneral || s.Cooldown > 0 || Terrain == null || !Terrain.IsRaised(s.Pos)) return false;
            int target = NearestEnemy(s, r.StoneRange * r.StoneRange, r.StoneRange);
            if (target < 0) return false;
            var e = Soldiers[target];
            if (Terrain.LevelAt(s.Pos) - Terrain.LevelAt(e.Pos) < Fix.OneValue) return false;
            int fall = System.Math.Max(1, (int)(Fix.FromRatio(1, 2) / r.TickSeconds).RoundToLong());
            Arrows.Add(new CrowdArrow { ShooterId = s.Id, TargetId = target, From = s.Pos, To = e.Pos, FireTick = Tick, ArriveTick = Tick + fall, Damage = r.StoneDamage, IsStone = true });
            s.Cooldown = System.Math.Max(1, (int)(r.StoneSeconds / r.TickSeconds).RoundToLong());
            s.LastAttackTick = Tick;
            s.LastTargetId = target;
            s.HitStructure = false;
            return true;
        }

        /// <summary>AI · 위임 부대의 공성 (R-145): 공성 중이면 공성 명령이 없을 때 기본 명령을 내림. true면 이번엔 공성으로 처리함.</summary>
        private bool AiSiege(CrowdArmy a)
        {
            if (!SiegeActive) return false;
            if (a.SiegeTask == null) OrderSiege(a.Id, DefaultSiegeTask(a));
            return true;
        }
    }
}

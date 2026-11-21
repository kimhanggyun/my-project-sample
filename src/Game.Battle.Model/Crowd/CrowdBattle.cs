using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Crowd
{
    /// <summary>샘플전투2 병사 하나 (장수도 병사 하나로, 맞으면 체력이 준다).</summary>
    public sealed class CrowdSoldier
    {
        public int Id;
        public int Team;
        public bool IsGeneral;
        public Vec Pos;
        /// <summary>대열 안 자리 (군단 기준, 앞 = +y).</summary>
        public Vec Slot;
        /// <summary>지난 틱에 움직인 양 (화면이 걷기 · 방향에 씀).</summary>
        public Vec Moved;
        public int Hp;
        public int MaxHp;
        public int Cooldown;
        /// <summary>마지막으로 친 틱 · 대상 (화면 공격 동작). 친 적 없으면 -1.</summary>
        public int LastAttackTick = -1;
        public int LastTargetId = -1;
        /// <summary>쓰러진 틱 (살아 있으면 -1).</summary>
        public int DiedTick = -1;
        public bool Alive => DiedTick < 0;
    }

    /// <summary>한쪽 군 (군단 하나): 대열 모양 · 가운데 · 앞 방향 · 명령.</summary>
    public sealed class CrowdArmy
    {
        public int Team;
        public string Formation;
        public Vec Center;
        public Vec Facing;
        /// <summary>true = 적 장수 쪽으로 전진, false = 제자리.</summary>
        public bool Advance;
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        public CrowdSoldier General;
        public int StartCount;

        public int AliveCount
        {
            get { int n = 0; foreach (var s in Soldiers) if (s.Alive && !s.IsGeneral) n++; return n; }
        }

        /// <summary>대열 자리의 전장 위치.</summary>
        public Vec SlotWorld(Vec slot)
        {
            var right = new Vec(Facing.Y, -Facing.X); // 앞이 +y일 때 오른쪽 +x
            return Center + right * slot.X + Facing * slot.Y;
        }
    }

    /// <summary>샘플전투2 시작 설정.</summary>
    public sealed class CrowdSetup
    {
        public int AllyCount = 200;
        public int EnemyCount = 200;
        public string AllyFormation = CrowdFormations.Line;
        public string EnemyFormation = CrowdFormations.Line;
        public bool AllyAdvance = true;
        public bool EnemyAdvance;
        public ulong Seed = 1;
    }

    /// <summary>
    /// 샘플전투2: 병사 단위 전투 (R-125, 시험 중 — docs/design/content/battle-crowd.md).
    /// 병사마다 자리 · 체력이 있고, 서로 밀어내 겹치지 않으며 (적과도), 가까운 적(EngageRange 안)에게 몰려가 닿으면 친다.
    /// 진형 모양이 그대로 결과를 바꾼다: 얇은 줄은 뚫리고, 두꺼운 줄은 뒷줄이 메우고, V는 여러 방향에서 동시에 닿는다.
    /// 규칙 계산이라 고정소수점 · 시드 난수만 쓰고, 병사를 id 순서로 계산한 뒤 한꺼번에 반영한다 (같은 입력 = 같은 결과).
    /// </summary>
    public sealed class CrowdBattle
    {
        public const int Ally = 0, Enemy = 1;

        public CrowdRules Rules { get; }
        public int Tick { get; private set; }
        public readonly CrowdArmy[] Armies = new CrowdArmy[2];
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        /// <summary>끝났으면 이긴 편 (Ally · Enemy), 비기면 -2, 진행 중이면 -1.</summary>
        public int Winner { get; private set; } = -1;
        /// <summary>끝난 까닭: general (장수 사로잡음) · wiped (전멸) · time.</summary>
        public string EndReason { get; private set; }
        public bool IsOver => Winner != -1;

        private readonly SeededRandom _rng;
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        private Vec[] _next = new Vec[0];
        private int[] _damage = new int[0];

        public CrowdBattle(CrowdRules rules, CrowdSetup setup)
        {
            Rules = rules;
            _rng = new SeededRandom(setup.Seed == 0 ? 1 : setup.Seed);
            var mid = rules.FieldWidth / 2;
            Armies[Ally] = MakeArmy(Ally, setup.AllyFormation, setup.AllyCount, new Vec(mid, rules.FieldDepth * Fix.FromRatio(3, 10)), new Vec(Fix.Zero, Fix.OneValue), setup.AllyAdvance);
            Armies[Enemy] = MakeArmy(Enemy, setup.EnemyFormation, setup.EnemyCount, new Vec(mid, rules.FieldDepth * Fix.FromRatio(7, 10)), new Vec(Fix.Zero, -Fix.OneValue), setup.EnemyAdvance);
        }

        private CrowdArmy MakeArmy(int team, string formation, int count, Vec center, Vec facing, bool advance)
        {
            var a = new CrowdArmy { Team = team, Formation = CrowdFormations.IsKnown(formation) ? formation : CrowdFormations.Line, Center = center, Facing = facing, Advance = advance, StartCount = count };
            var slots = CrowdFormations.Slots(a.Formation, count, Rules.Spacing);
            foreach (var slot in slots)
            {
                var s = new CrowdSoldier { Id = Soldiers.Count, Team = team, Slot = slot, Hp = Rules.SoldierHp, MaxHp = Rules.SoldierHp };
                s.Pos = a.SlotWorld(slot);
                s.Cooldown = _rng.NextInt(Rules.AttackTicks);
                a.Soldiers.Add(s);
                Soldiers.Add(s);
            }
            var g = new CrowdSoldier { Id = Soldiers.Count, Team = team, IsGeneral = true, Hp = Rules.GeneralHp, MaxHp = Rules.GeneralHp };
            g.Slot = new Vec(Fix.Zero, CrowdFormations.BackOf(slots) - Rules.GeneralBack);
            g.Pos = a.SlotWorld(g.Slot);
            a.General = g;
            a.Soldiers.Add(g);
            Soldiers.Add(g);
            return a;
        }

        /// <summary>
        /// 대열 바꾸기: 새 모양 자리를 병사들에게 다시 나눠 준다 (지금 위치의 좌우 순서를 지켜 서로 덜 엇갈리게).
        /// 병사는 다음 틱부터 새 자리로 걸어간다 (맞붙은 병사는 싸움을 계속).
        /// </summary>
        public void SetFormation(int team, string formation)
        {
            if (!CrowdFormations.IsKnown(formation) || IsOver) return;
            var a = Armies[team];
            a.Formation = formation;
            var alive = new List<CrowdSoldier>();
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral) alive.Add(s);
            var slots = CrowdFormations.Slots(formation, alive.Count, Rules.Spacing);
            var right = new Vec(a.Facing.Y, -a.Facing.X);
            // 지금 위치를 군단 기준 (오른쪽, 앞)으로 → 오른쪽 순, 같으면 앞 순으로 줄 세워 같은 순서의 자리와 짝짓기
            alive.Sort((p, q) =>
            {
                var dp = p.Pos - a.Center; var dq = q.Pos - a.Center;
                int c = dp.Dot(right).CompareTo(dq.Dot(right));
                if (c != 0) return c;
                c = dp.Dot(a.Facing).CompareTo(dq.Dot(a.Facing));
                return c != 0 ? c : p.Id.CompareTo(q.Id);
            });
            slots.Sort((p, q) => { int c = p.X.CompareTo(q.X); return c != 0 ? c : p.Y.CompareTo(q.Y); });
            for (int i = 0; i < alive.Count; i++) alive[i].Slot = slots[i];
            a.General.Slot = new Vec(Fix.Zero, CrowdFormations.BackOf(slots) - Rules.GeneralBack);
        }

        public void SetAdvance(int team, bool advance) { if (!IsOver) Armies[team].Advance = advance; }

        public void Step()
        {
            if (IsOver) return;
            Tick++;
            var r = Rules;
            Fix dt = r.TickSeconds;
            Fix stepLen = r.MoveSpeed * dt;
            Fix diameter = r.SoldierRadius * 2;
            Fix reach = diameter + r.AttackReach;
            Fix reach2 = reach * reach;
            Fix engage2 = r.EngageRange * r.EngageRange;

            int n = Soldiers.Count;
            if (_next.Length < n) { _next = new Vec[n]; _damage = new int[n]; }
            BuildGrid();

            // 1) 군단 가운데: 전진이면 적 장수 쪽으로 (맞붙은 병사 비율만큼 느리게)
            var engaged = new int[2];
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                if (s.LastAttackTick >= Tick - r.AttackTicks * 2) engaged[s.Team]++;
            }
            for (int t = 0; t < 2; t++)
            {
                var a = Armies[t];
                var foe = Armies[1 - t];
                if (!a.Advance) continue;
                var goal = foe.General.Alive ? foe.General.Pos : foe.Center;
                var d = goal - a.Center;
                if (d.LengthSquared <= Fix.OneValue) continue;
                a.Facing = d.Normalized();
                int alive = System.Math.Max(1, a.AliveCount);
                Fix slow = Fix.OneValue - Fix.Min(Fix.OneValue, Fix.FromRatio(engaged[t] * 2, alive));
                a.Center = a.Center + a.Facing * (r.AdvanceSpeed * dt * slow);
            }

            // 2) 병사마다 갈 곳 · 칠 대상 (id 순서로 계산, 반영은 한꺼번에)
            for (int i = 0; i < n; i++) { _next[i] = Soldiers[i].Pos; _damage[i] = 0; }
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                if (s.Cooldown > 0) s.Cooldown--;
                var army = Armies[s.Team];
                Vec want;
                int foe = s.IsGeneral ? -1 : NearestEnemy(s, engage2);
                if (foe >= 0)
                {
                    var e = Soldiers[foe];
                    var d = e.Pos - s.Pos;
                    Fix d2 = d.LengthSquared;
                    if (d2 <= reach2)
                    {
                        want = Vec.Zero; // 닿았다: 서서 친다
                        if (s.Cooldown == 0)
                        {
                            _damage[foe]++;
                            s.LastAttackTick = Tick;
                            s.LastTargetId = foe;
                            s.Cooldown = r.AttackTicks + (r.AttackJitterTicks > 0 ? _rng.NextInt(r.AttackJitterTicks + 1) : 0);
                        }
                    }
                    else
                    {
                        Fix len = Fix.Sqrt(d2);
                        Fix go = Fix.Min(stepLen, len - reach + Fix.FromRatio(1, 100));
                        want = len > Fix.Zero ? d * (go / len) : Vec.Zero;
                    }
                }
                else
                {
                    var d = army.SlotWorld(s.Slot) - s.Pos;
                    Fix len = d.Length;
                    want = len <= stepLen ? d : d * (stepLen / len);
                }
                _next[i] = s.Pos + want + Separation(s, diameter);
            }

            // 2.5) 다음 위치끼리 겹친 것을 두 번 풀어 준다 (뒤에서 미는 병사가 앞 병사 속으로 파고들지 않게)
            for (int pass = 0; pass < RelaxPasses; pass++) Relax(diameter);

            // 3) 반영: 위치 · 피해 · 쓰러짐
            Fix maxX = r.FieldWidth, maxY = r.FieldDepth;
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                var p = _next[i];
                p = new Vec(Fix.Clamp(p.X, Fix.Zero, maxX), Fix.Clamp(p.Y, Fix.Zero, maxY));
                s.Moved = p - s.Pos;
                s.Pos = p;
                if (_damage[i] > 0)
                {
                    s.Hp -= _damage[i];
                    if (s.Hp <= 0) { s.Hp = 0; s.DiedTick = Tick; s.Moved = Vec.Zero; }
                }
            }

            // 4) 끝: 장수를 사로잡거나 (체력 0) 병사가 다 쓰러지면, 시간이 다 되면 남은 병사 많은 쪽
            bool allyOut = !Armies[Ally].General.Alive || Armies[Ally].AliveCount == 0;
            bool enemyOut = !Armies[Enemy].General.Alive || Armies[Enemy].AliveCount == 0;
            if (allyOut || enemyOut)
            {
                Winner = allyOut && enemyOut ? -2 : allyOut ? Enemy : Ally;
                var loser = Armies[allyOut ? Ally : Enemy];
                EndReason = !loser.General.Alive ? "general" : "wiped";
            }
            else if (Tick >= r.MaxTicks)
            {
                int a = Armies[Ally].AliveCount, e = Armies[Enemy].AliveCount;
                Winner = a == e ? -2 : a > e ? Ally : Enemy;
                EndReason = "time";
            }
        }

        private const int RelaxPasses = 2;

        /// <summary>다음 위치(_next) 기준으로 겹친 두 병사를 반씩 떼어 놓는다 (id 순서, 이웃은 지금 칸 격자로).</summary>
        private void Relax(Fix diameter)
        {
            Fix d2max = diameter * diameter;
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
                for (long x = cx - 1; x <= cx + 1; x++)
                    for (long y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                        foreach (int j in list)
                        {
                            if (j <= i) continue;
                            var d = _next[i] - _next[j];
                            Fix d2 = d.LengthSquared;
                            if (d2 >= d2max || d2 == Fix.Zero) continue;
                            Fix len = Fix.Sqrt(d2);
                            var corr = d * ((diameter - len) / 2 / len);
                            // 장수는 덜 밀린다
                            bool gi = s.IsGeneral, gj = Soldiers[j].IsGeneral;
                            _next[i] = _next[i] + (gi && !gj ? corr * Fix.FromRatio(1, 4) : gj && !gi ? corr * Fix.FromRatio(7, 4) : corr);
                            _next[j] = _next[j] - (gj && !gi ? corr * Fix.FromRatio(1, 4) : gi && !gj ? corr * Fix.FromRatio(7, 4) : corr);
                        }
                    }
            }
        }

        // ---- 이웃 찾기: 1칸 격자 ----

        private static long Key(long x, long y) => x * 100003L + y;

        private void BuildGrid()
        {
            foreach (var kv in _grid) kv.Value.Clear();
            for (int i = 0; i < Soldiers.Count; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                long k = Key(s.Pos.X.FloorToLong(), s.Pos.Y.FloorToLong());
                if (!_grid.TryGetValue(k, out var list)) _grid[k] = list = new List<int>();
                list.Add(i);
            }
        }

        /// <summary>이 거리 제곱 안의 가장 가까운 적 (같으면 작은 id). 없으면 -1.</summary>
        private int NearestEnemy(CrowdSoldier s, Fix range2)
        {
            int best = -1;
            Fix bestD = range2;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            int cells = (int)Rules.EngageRange.FloorToLong() + 1;
            for (long x = cx - cells; x <= cx + cells; x++)
                for (long y = cy - cells; y <= cy + cells; y++)
                {
                    if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        var o = Soldiers[j];
                        if (o.Team == s.Team) continue;
                        Fix d2 = Vec.DistanceSquared(o.Pos, s.Pos);
                        if (d2 < bestD || (d2 == bestD && best >= 0 && j < best)) { bestD = d2; best = j; }
                    }
                }
            return best;
        }

        /// <summary>겹친 이웃(편 상관없이)에게서 겹친 만큼의 절반씩 밀려난다.</summary>
        private Vec Separation(CrowdSoldier s, Fix diameter)
        {
            Fix px = Fix.Zero, py = Fix.Zero;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            Fix d2max = diameter * diameter;
            for (long x = cx - 1; x <= cx + 1; x++)
                for (long y = cy - 1; y <= cy + 1; y++)
                {
                    if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j == s.Id) continue;
                        var o = Soldiers[j];
                        var d = s.Pos - o.Pos;
                        Fix d2 = d.LengthSquared;
                        if (d2 >= d2max) continue;
                        if (d2 == Fix.Zero)
                        {
                            // 같은 자리: id로 정한 방향으로 살짝
                            Fix e = Fix.FromRatio(1, 100);
                            px += s.Id < j ? -e : e;
                            continue;
                        }
                        Fix len = Fix.Sqrt(d2);
                        Fix push = (diameter - len) / 2;
                        // 장수는 덜 밀린다 (대열 뒤에서 버팀)
                        if (s.IsGeneral) push = push / 2;
                        px += d.X * push / len;
                        py += d.Y * push / len;
                    }
                }
            return new Vec(px, py);
        }

        /// <summary>결과 지문 (같은 입력이면 같아야 함 — 결정성 시험용).</summary>
        public long Fingerprint()
        {
            unchecked
            {
                long h = 17;
                foreach (var s in Soldiers)
                {
                    h = h * 31 + s.Pos.X.Raw;
                    h = h * 31 + s.Pos.Y.Raw;
                    h = h * 31 + s.Hp;
                    h = h * 31 + s.DiedTick;
                }
                return h * 31 + Tick;
            }
        }
    }
}

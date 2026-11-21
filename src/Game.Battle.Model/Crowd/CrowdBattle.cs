using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Data.Defs;
using Game.Foundation.Math;
using Game.Foundation.Random;

namespace Game.Battle.Model.Crowd
{
    /// <summary>샘플전투2 병사 하나 (장수도 병사 하나로, 맞으면 체력이 준다).</summary>
    public sealed class CrowdSoldier
    {
        public int Id;
        public int Team;
        /// <summary>속한 부대 (CrowdBattle.Armies 번호).</summary>
        public int Army;
        public bool IsGeneral;
        public Vec Pos;
        /// <summary>대열 안 자리 (부대 기준, 앞 = +y).</summary>
        public Vec Slot;
        /// <summary>지난 틱에 움직인 양 (화면이 걷기 · 달리기 · 방향에 씀).</summary>
        public Vec Moved;
        /// <summary>차지하는 반지름 (말 탄 병사는 크다).</summary>
        public Fix Radius;
        public int Hp;
        public int MaxHp;
        public int Cooldown;
        /// <summary>마지막으로 친(쏜) 틱 · 대상 (화면 공격 동작). 친 적 없으면 -1.</summary>
        public int LastAttackTick = -1;
        public int LastTargetId = -1;
        /// <summary>돌격해 부딪친 틱 (친 쪽 · 맞은 쪽 둘 다, 화면 흙먼지 · 흔들림). 없으면 -1.</summary>
        public int ImpactTick = -1;
        /// <summary>쓰러진 틱 (살아 있으면 -1).</summary>
        public int DiedTick = -1;
        /// <summary>무너져 도망치다 전장 밖으로 나간 틱 (R-128, 아니면 -1). 쓰러진 것은 아니지만 더는 싸우지 않는다.</summary>
        public int FledTick = -1;
        /// <summary>전장에서 싸울 수 있음 (쓰러지지도 나가지도 않음).</summary>
        public bool Alive => DiedTick < 0 && FledTick < 0;
    }

    /// <summary>날아가는 화살 (R-127): 쏜 자리 → 쏠 때 대상 자리, 닿는 틱에 대상이 살아 있으면 맞을 수 있다.</summary>
    public sealed class CrowdArrow
    {
        public int ShooterId;
        public int TargetId;
        public Vec From;
        public Vec To;
        public int FireTick;
        public int ArriveTick;
        public int Damage;
    }

    public static class CrowdOrders
    {
        public const string Hold = "hold", Move = "move", Attack = "attack";
    }

    /// <summary>부대 하나: 병종 · 대열 모양 · 가운데 · 앞 방향 · 명령 (제자리 · 땅으로 이동 · 적 부대 공격).</summary>
    public sealed class CrowdArmy
    {
        public int Id;
        public int Team;
        /// <summary>left · center · right (부대 하나면 center). 총대장 부대는 center.</summary>
        public string Place;
        public bool IsCommander;
        public CrowdTroop Troop = new CrowdTroop();
        public string Formation;
        public Vec Center;
        public Vec Facing;
        public string Order = CrowdOrders.Hold;
        public Vec OrderPoint;
        public int OrderTarget = -1;
        /// <summary>돌격 중 (공격 명령으로 적에게 ChargeRange 안까지 다가옴, 맞붙기 전까지) — 병사 · 부대가 빨라진다. 궁병은 돌격하지 않음.</summary>
        public bool Charging;
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        public CrowdSoldier General;
        public int StartCount;
        /// <summary>대열 반 폭 · 반 깊이 (자리에서, 칸).</summary>
        public Fix HalfWidth, HalfDepth;
        /// <summary>병종 빠르기 배수 (speed ÷ 기준 빠르기).</summary>
        public Fix SpeedFactor = Fix.OneValue;
        /// <summary>사기 (R-128, 0 ~ moraleStart).</summary>
        public Fix Morale;
        /// <summary>흔들림 (사기 waverAt 이하): 느려지고 덜 친다.</summary>
        public bool Wavering;
        /// <summary>무너짐 (사기 routAt 이하, 되돌아오지 않음): 도망치고 명령을 못 받는다.</summary>
        public bool Routed;
        public int RoutedTick = -1;
        /// <summary>포위됨 (최근 앞이 아닌 두 방향 이상에서 맞음) — 사기가 계속 깎인다.</summary>
        public bool Encircled;
        /// <summary>방향마다 마지막으로 맞은 틱 (0 앞 · 1 왼쪽 · 2 오른쪽 · 3 뒤).</summary>
        public readonly int[] SideHitTick = { -9999, -9999, -9999, -9999 };
        /// <summary>이번 틱 시작 때 살아 있는 병사 가운데 (옆 · 뒤 판정의 기준).</summary>
        public Vec TickCentroid;
        public int LastHitTick = -9999;

        public int AliveCount
        {
            get { int n = 0; foreach (var s in Soldiers) if (s.Alive && !s.IsGeneral) n++; return n; }
        }

        /// <summary>장수가 살아 있으면 명령을 받는다 (장수를 잃은 부대는 제자리에서 싸움만).</summary>
        public bool Leading => General.Alive && !Routed;

        public Vec Right => new Vec(Facing.Y, -Facing.X); // 앞이 +y일 때 오른쪽 +x

        /// <summary>대열 자리의 전장 위치.</summary>
        public Vec SlotWorld(Vec slot) => Center + Right * slot.X + Facing * slot.Y;

        /// <summary>살아 있는 병사 가운데 (없으면 부대 가운데).</summary>
        public Vec Centroid()
        {
            Fix sx = Fix.Zero, sy = Fix.Zero;
            int n = 0;
            foreach (var s in Soldiers) if (s.Alive && !s.IsGeneral) { sx += s.Pos.X; sy += s.Pos.Y; n++; }
            return n == 0 ? (General.Alive ? General.Pos : Center) : new Vec(sx / n, sy / n);
        }
    }

    /// <summary>샘플전투2 시작 설정.</summary>
    public sealed class CrowdSetup
    {
        /// <summary>편마다 병사 수 (부대가 여럿이면 나눠 가진다).</summary>
        public int AllyCount = 200;
        public int EnemyCount = 200;
        /// <summary>편마다 부대 수 (1 또는 3: 좌 · 중 · 우, 가운데가 총대장).</summary>
        public int AllyCorps = 1;
        public int EnemyCorps = 1;
        public string AllyFormation = CrowdFormations.Line;
        public string EnemyFormation = CrowdFormations.Line;
        /// <summary>부대마다 병종 id (좌 · 중 · 우 순, 하나면 [0]). 비면 보병 (R-127).</summary>
        public string[] AllyTroops = { "infantry" };
        public string[] EnemyTroops = { "infantry" };
        /// <summary>true면 시작부터 우리 부대가 모두 적 총대장 부대를 공격 (시험 · 캡처). 사용자 조작이면 false.</summary>
        public bool AllyAdvance = true;
        /// <summary>true면 적 부대가 가장 가까운 우리 부대를 공격, false면 제자리.</summary>
        public bool EnemyAdvance;
        public ulong Seed = 1;
    }

    /// <summary>
    /// 샘플전투2: 병사 단위 전투 (R-125 · R-126 · R-127, 시험 중 — docs/design/content/battle-crowd.md).
    /// 병사마다 자리 · 체력이 있고, 서로 밀어내 겹치지 않으며 (적과도), 가까운 적(EngageRange 안)에게 몰려가 닿으면 친다.
    /// 편마다 부대가 하나 또는 셋이고, 부대마다 병종 · 명령(제자리 · 이동 · 공격) · 진형을 따로 받는다.
    /// 병종 (R-127): 빠르기 · 닿는 거리(창이 길다) · 공격 · 방어 · 상성(troopCounters.json), 궁병은 멀리서 화살, 기병은 크고 빠르며 부딪침이 세다,
    /// 창병은 달려드는 기병을 받아친다.
    /// 규칙 계산이라 고정소수점 · 시드 난수만 쓰고, 병사를 id 순서로 계산한 뒤 한꺼번에 반영한다 (같은 입력 = 같은 결과).
    /// </summary>
    public sealed class CrowdBattle
    {
        public const int Ally = 0, Enemy = 1;
        public static readonly string[] Places3 = { "left", "center", "right" };

        public CrowdRules Rules { get; }
        public int Tick { get; private set; }
        public readonly List<CrowdArmy> Armies = new List<CrowdArmy>();
        public readonly List<CrowdSoldier> Soldiers = new List<CrowdSoldier>();
        /// <summary>날아가는 화살 (닿으면 빠진다).</summary>
        public readonly List<CrowdArrow> Arrows = new List<CrowdArrow>();
        /// <summary>끝났으면 이긴 편 (Ally · Enemy), 비기면 -2, 진행 중이면 -1.</summary>
        public int Winner { get; private set; } = -1;
        /// <summary>끝난 까닭: general (총대장 사로잡음) · wiped (전멸) · routed (모든 부대가 무너짐) · time.</summary>
        public string EndReason { get; private set; }
        public bool IsOver => Winner != -1;
        /// <summary>이번 틱 부딪침 수 (화면 흔들림).</summary>
        public int ImpactsThisTick { get; private set; }
        /// <summary>지금까지 맞은 화살 수 (시험 · 화면 정보).</summary>
        public int ArrowHits { get; private set; }

        private readonly bool _enemyAdvance;
        private readonly SeededRandom _rng;
        private readonly IReadOnlyList<TroopCounterRule> _counters;
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        private Vec[] _next = new Vec[0];
        private Vec[] _knock = new Vec[0];
        private int[] _damage = new int[0];
        private Fix _maxRadius;
        private readonly int[] _allRoutedSince = { -1, -1 };

        /// <param name="troops">병종 데이터 (없으면 모두 보병 기본값).</param>
        /// <param name="counters">병종 상성 (없으면 상성 없음).</param>
        public CrowdBattle(CrowdRules rules, CrowdSetup setup, IReadOnlyDictionary<string, TroopTypeDef> troops = null, IReadOnlyList<TroopCounterRule> counters = null)
        {
            Rules = rules;
            _counters = counters ?? new TroopCounterRule[0];
            _rng = new SeededRandom(setup.Seed == 0 ? 1 : setup.Seed);
            _enemyAdvance = setup.EnemyAdvance;
            _maxRadius = Fix.Max(rules.SoldierRadius, rules.MountedRadius);
            MakeTeam(Ally, setup.AllyCorps, setup.AllyCount, setup.AllyFormation, setup.AllyTroops, troops, rules.FieldDepth * Fix.FromRatio(3, 10), new Vec(Fix.Zero, Fix.OneValue));
            MakeTeam(Enemy, setup.EnemyCorps, setup.EnemyCount, setup.EnemyFormation, setup.EnemyTroops, troops, rules.FieldDepth * Fix.FromRatio(7, 10), new Vec(Fix.Zero, -Fix.OneValue));
            if (setup.AllyAdvance)
                foreach (var a in Armies) if (a.Team == Ally) OrderAttack(a.Id, Commander(Enemy).Id);
        }

        public CrowdArmy Commander(int team)
        {
            foreach (var a in Armies) if (a.Team == team && a.IsCommander) return a;
            return null;
        }

        public IEnumerable<CrowdArmy> TeamArmies(int team)
        {
            foreach (var a in Armies) if (a.Team == team) yield return a;
        }

        public int AliveCount(int team)
        {
            int n = 0;
            foreach (var a in Armies) if (a.Team == team) n += a.AliveCount;
            return n;
        }

        private void MakeTeam(int team, int corps, int count, string formation, string[] troopIds, IReadOnlyDictionary<string, TroopTypeDef> troops, Fix y, Vec facing)
        {
            corps = corps >= 3 ? 3 : 1;
            var form = CrowdFormations.IsKnown(formation) ? formation : CrowdFormations.Line;
            var made = new List<CrowdArmy>();
            for (int k = 0; k < corps; k++)
            {
                int share = count / corps + (k < count % corps ? 1 : 0);
                // 좌 · 우는 보는 쪽(우리 편) 기준: 적은 앞이 반대라 자기 오른쪽이 화면 왼쪽 → 이름을 뒤집어 화면 왼쪽 부대가 "좌" (2026-11-21 캡처: 우리 좌군이 적 좌군을 치러 전장을 가로질러 감)
                int pi = corps == 1 ? 1 : team == Ally ? k : 2 - k;
                string place = corps == 1 ? "center" : Places3[pi];
                string tid = troopIds != null && troopIds.Length > 0 ? troopIds[System.Math.Min(corps == 1 ? 0 : pi, troopIds.Length - 1)] : "infantry"; // 병종도 좌 · 중 · 우 이름 순서대로
                made.Add(MakeArmy(team, place, place == "center", form, share, facing, CrowdTroop.Of(tid, troops)));
            }
            // 좌 · 중 · 우로 나란히 (사이 CorpsGap), 편의 가운데가 전장 가운데. 오른쪽 = 부대의 Right (적은 좌우가 뒤집힘)
            Fix total = Fix.Zero;
            foreach (var a in made) total += a.HalfWidth * 2;
            total += Rules.CorpsGap * (made.Count - 1);
            Fix x = -total / 2;
            var mid = new Vec(Rules.FieldWidth / 2, y);
            foreach (var a in made)
            {
                Fix cx = x + a.HalfWidth;
                a.Center = mid + a.Right * cx;
                x += a.HalfWidth * 2 + Rules.CorpsGap;
                foreach (var s in a.Soldiers) s.Pos = a.SlotWorld(s.Slot);
            }
        }

        private Fix SpacingOf(CrowdTroop t) => t.Mounted ? Rules.Spacing * Rules.MountedRadius / Rules.SoldierRadius : Rules.Spacing;

        private CrowdArmy MakeArmy(int team, string place, bool commander, string formation, int count, Vec facing, CrowdTroop troop)
        {
            var a = new CrowdArmy { Id = Armies.Count, Team = team, Place = place, IsCommander = commander, Formation = formation, Facing = facing, StartCount = count, Troop = troop };
            a.SpeedFactor = troop.Speed / Rules.BaseSpeedStat;
            a.Morale = Rules.MoraleStart;
            var slots = CrowdFormations.Slots(formation, count, SpacingOf(troop));
            Fix radius = troop.Mounted ? Rules.MountedRadius : Rules.SoldierRadius;
            foreach (var slot in slots)
            {
                var s = new CrowdSoldier { Id = Soldiers.Count, Team = team, Army = a.Id, Slot = slot, Hp = Rules.SoldierHp, MaxHp = Rules.SoldierHp, Radius = radius };
                s.Cooldown = _rng.NextInt(Rules.AttackTicks);
                a.Soldiers.Add(s);
                Soldiers.Add(s);
            }
            var g = new CrowdSoldier { Id = Soldiers.Count, Team = team, Army = a.Id, IsGeneral = true, Hp = Rules.GeneralHp, MaxHp = Rules.GeneralHp, Radius = Rules.MountedRadius };
            g.Slot = new Vec(Fix.Zero, CrowdFormations.BackOf(slots) - Rules.GeneralBack);
            a.General = g;
            a.Soldiers.Add(g);
            Soldiers.Add(g);
            Measure(a, slots);
            Armies.Add(a);
            return a;
        }

        private static void Measure(CrowdArmy a, List<Vec> slots)
        {
            Fix minX = Fix.Zero, maxX = Fix.Zero, minY = Fix.Zero, maxY = Fix.Zero;
            foreach (var s in slots)
            {
                if (s.X < minX) minX = s.X; if (s.X > maxX) maxX = s.X;
                if (s.Y < minY) minY = s.Y; if (s.Y > maxY) maxY = s.Y;
            }
            a.HalfWidth = (maxX - minX) / 2;
            a.HalfDepth = (maxY - minY) / 2;
        }

        // ---- 명령 ----

        /// <summary>
        /// 대열 바꾸기: 새 모양 자리를 병사들에게 다시 나눠 준다 (지금 위치의 좌우 순서를 지켜 서로 덜 엇갈리게).
        /// 병사는 다음 틱부터 새 자리로 걸어간다 (맞붙은 병사는 싸움을 계속).
        /// </summary>
        public void SetFormation(int army, string formation)
        {
            if (!CrowdFormations.IsKnown(formation) || IsOver || army < 0 || army >= Armies.Count) return;
            var a = Armies[army];
            if (!a.Leading) return;
            a.Formation = formation;
            var alive = new List<CrowdSoldier>();
            foreach (var s in a.Soldiers) if (s.Alive && !s.IsGeneral) alive.Add(s);
            var slots = CrowdFormations.Slots(formation, alive.Count, SpacingOf(a.Troop));
            var right = a.Right;
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
            Measure(a, slots);
        }

        public void OrderHold(int army)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            a.Order = CrowdOrders.Hold; a.OrderTarget = -1; a.Charging = false;
        }

        /// <summary>땅 한 곳으로 이동 (가면서 만나는 적과는 싸운다, 도착하면 제자리).</summary>
        public void OrderMove(int army, Vec point)
        {
            if (IsOver || army < 0 || army >= Armies.Count || !Armies[army].Leading) return;
            var a = Armies[army];
            a.Order = CrowdOrders.Move;
            a.OrderPoint = new Vec(Fix.Clamp(point.X, Fix.Zero, Rules.FieldWidth), Fix.Clamp(point.Y, Fix.Zero, Rules.FieldDepth));
            a.OrderTarget = -1;
            a.Charging = false;
        }

        /// <summary>적 부대 공격 (그 부대 쪽으로 가서, 가까워지면 돌격 — 궁병은 사거리에서 멈춰 쏜다).</summary>
        public void OrderAttack(int army, int target)
        {
            if (IsOver || army < 0 || army >= Armies.Count || target < 0 || target >= Armies.Count) return;
            var a = Armies[army];
            if (!a.Leading || Armies[target].Team == a.Team) return;
            a.Order = CrowdOrders.Attack;
            a.OrderTarget = target;
        }

        /// <summary>그 부대가 아직 싸울 수 있음 (병사나 장수가 남음).</summary>
        public bool Standing(CrowdArmy a) => a.General.Alive || a.AliveCount > 0;

        /// <summary>원거리 병종의 사거리 (칸).</summary>
        public Fix RangeOf(CrowdTroop t) => t.AttackRange * Rules.RangeScale;

        // ---- 피해 ----

        /// <summary>
        /// 맞는 부대 기준 어느 쪽에서 맞았나 (R-128): 0 앞 · 1 왼쪽 · 2 오른쪽 · 3 뒤.
        /// 부대 가운데(병사 평균)에서 때린 자리 방향과 부대 앞 방향의 내적이 frontDot 이상이면 앞, rearDot 이하면 뒤, 아니면 옆.
        /// (병사 자리 기준이면 V자 팔 병사가 안쪽 적에게 맞는 것도 옆이 되어, 감싼 쪽이 오히려 포위당한 셈이 됨)
        /// </summary>
        public int SideOf(CrowdArmy defender, Vec defenderPos, Vec attackerPos)
        {
            var d = (attackerPos - defenderPos).Normalized();
            Fix dot = d.Dot(defender.Facing);
            if (dot >= Rules.FrontDot) return 0;
            if (dot <= Rules.RearDot) return 3;
            return d.Dot(defender.Right) < Fix.Zero ? 1 : 2;
        }

        private Fix SidePp(int side) => side == 3 ? Rules.RearPp : side == 0 ? Fix.Zero : Rules.FlankPp;

        /// <summary>맞은 쪽을 적어 둔다 (사기: 옆 · 뒤에서 맞는 동안 깎임 · 포위 판정).</summary>
        private void NoteHit(CrowdArmy defender, int side)
        {
            defender.SideHitTick[side] = Tick;
            defender.LastHitTick = Tick;
        }

        /// <summary>
        /// 한 번 친(쏜) 피해: hitDamage × (1 + (공격 − 방어) × statPpPerPoint% + 상성%), 1 이상.
        /// 상성 = troopCounters.json에서 공격 쪽 태그 → 방어 쪽 태그 규칙의 합.
        /// </summary>
        public int HitDamage(CrowdTroop atk, CrowdTroop def) => HitDamage(atk, def, Fix.Zero);

        /// <param name="extraPp">옆 · 뒤에서 친 피해 더 (R-128).</param>
        public int HitDamage(CrowdTroop atk, CrowdTroop def, Fix extraPp)
        {
            Fix pp = (atk.Attack - def.Defense) * Rules.StatPpPerPoint + extraPp;
            foreach (var c in _counters)
                if (atk.Has(c.AttackerTag) && def.Has(c.DefenderTag)) pp += c.Pp;
            Fix mult = Fix.OneValue + pp / 100;
            if (mult < Fix.FromRatio(1, 10)) mult = Fix.FromRatio(1, 10);
            long d = (Fix.FromInt(Rules.HitDamage) * mult).RoundToLong();
            return d < 1 ? 1 : (int)d;
        }

        // ---- 한 틱 ----

        public void Step()
        {
            if (IsOver) return;
            Tick++;
            var r = Rules;
            Fix dt = r.TickSeconds;
            Fix engage2 = r.EngageRange * r.EngageRange;

            int n = Soldiers.Count;
            if (_next.Length < n) { _next = new Vec[n]; _damage = new int[n]; _knock = new Vec[n]; }
            BuildGrid();
            ImpactsThisTick = 0;

            // 1) 부대: 적 AI 명령, 맞붙은 비율, 돌격 여부, 가운데 옮기기
            foreach (var a in Armies) a.TickCentroid = a.Centroid();
            if (_enemyAdvance && Tick % 10 == 1) EnemyThink();
            var engaged = new int[Armies.Count];
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (s.Alive && s.LastAttackTick >= Tick - r.AttackTicks * 2 && !Armies[s.Army].Troop.Ranged) engaged[s.Army]++;
            }
            foreach (var a in Armies) MoveArmy(a, engaged[a.Id]);

            // 2) 병사마다 갈 곳 · 칠 대상 (id 순서로 계산, 반영은 한꺼번에)
            for (int i = 0; i < n; i++) { _next[i] = Soldiers[i].Pos; _damage[i] = 0; _knock[i] = Vec.Zero; }
            for (int i = 0; i < n; i++)
            {
                var s = Soldiers[i];
                if (!s.Alive) continue;
                if (s.Cooldown > 0) s.Cooldown--;
                var army = Armies[s.Army];
                var troop = army.Troop;
                Fix walkStep = r.MoveSpeed * army.SpeedFactor * dt;
                Fix stepLen = army.Charging ? r.ChargeSpeed * army.SpeedFactor * dt : walkStep;
                if (army.Wavering) { walkStep = walkStep * r.WaverSpeed; stepLen = stepLen * r.WaverSpeed; }
                if (army.Routed)
                {
                    // 무너짐: 자기 쪽 가장자리로 도망 (가까운 적에게서 멀어지는 쪽으로 조금 비켜), 치지 않는다
                    var away = new Vec(Fix.Zero, s.Team == Ally ? -Fix.OneValue : Fix.OneValue);
                    int near = NearestEnemy(s, engage2, r.EngageRange);
                    if (near >= 0) away = (away + (s.Pos - Soldiers[near].Pos).Normalized()).Normalized();
                    _next[i] = s.Pos + away * (r.FleeSpeed * army.SpeedFactor * dt) + Separation(s);
                    continue;
                }
                Vec want;
                int foe = s.IsGeneral ? -1 : NearestEnemy(s, engage2, r.EngageRange);
                // 궁병: 맞붙을 만큼 가깝지 않으면 사거리 안의 적에게 쏜다 (자리를 지킨 채)
                if (!s.IsGeneral && troop.Ranged && foe < 0)
                {
                    if (s.Cooldown == 0)
                    {
                        Fix range = RangeOf(troop);
                        int target = NearestEnemy(s, range * range, range);
                        if (target >= 0) Shoot(s, target, troop);
                    }
                    _next[i] = s.Pos + ToSlot(army, s, walkStep) + Separation(s);
                    continue;
                }
                if (foe >= 0)
                {
                    var e = Soldiers[foe];
                    var d = e.Pos - s.Pos;
                    Fix d2 = d.LengthSquared;
                    Fix reach = s.Radius + e.Radius + r.AttackReach + (troop.AttackRange > Fix.OneValue && !troop.Ranged ? (troop.AttackRange - Fix.OneValue) * r.PolearmReach : Fix.Zero);
                    if (d2 <= reach * reach)
                    {
                        want = Vec.Zero; // 닿았다: 서서 친다
                        if (s.Cooldown == 0) Strike(s, e, d, d2, army, walkStep);
                    }
                    else
                    {
                        Fix len = Fix.Sqrt(d2);
                        Fix go = Fix.Min(stepLen, len - reach + Fix.FromRatio(1, 100));
                        want = len > Fix.Zero ? d * (go / len) : Vec.Zero;
                    }
                }
                else want = ToSlot(army, s, stepLen);
                _next[i] = s.Pos + want + Separation(s);
            }
            for (int i = 0; i < n; i++) if (!_knock[i].Equals(Vec.Zero)) _next[i] = _next[i] + _knock[i];

            // 2.5) 다음 위치끼리 겹친 것을 두 번 풀어 준다 (뒤에서 미는 병사가 앞 병사 속으로 파고들지 않게)
            for (int pass = 0; pass < RelaxPasses; pass++) Relax();

            // 2.7) 화살: 닿는 틱이면 대상이 살아 있을 때 맞을 수 있다 (시드 난수)
            for (int k = 0; k < Arrows.Count; k++)
            {
                var ar = Arrows[k];
                if (ar.ArriveTick > Tick) continue;
                var t = Soldiers[ar.TargetId];
                if (t.Alive && _rng.Chance(r.ArrowHitChance))
                {
                    var ta = Armies[t.Army];
                    int side = SideOf(ta, ta.TickCentroid, ar.From);
                    _damage[t.Id] += side == 0 ? ar.Damage : ar.Damage + System.Math.Max(0, (int)(Fix.FromInt(ar.Damage) * SidePp(side) / 100).RoundToLong());
                    NoteHit(ta, side);
                    ArrowHits++;
                }
                Arrows.RemoveAt(k);
                k--;
            }

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
                    if (s.Hp <= 0)
                    {
                        s.Hp = 0; s.DiedTick = Tick; s.Moved = Vec.Zero;
                        var da = Armies[s.Army];
                        if (s.IsGeneral) da.Morale -= r.GeneralLostMorale;
                        else da.Morale -= r.MoraleDeathTotal / System.Math.Max(1, da.StartCount);
                        continue;
                    }
                }
                // 무너진 병사가 자기 쪽 가장자리에 닿으면 전장 밖으로 (빠짐)
                if (Armies[s.Army].Routed && (s.Team == Ally ? p.Y <= Fix.FromRatio(1, 2) : p.Y >= maxY - Fix.FromRatio(1, 2))) { s.FledTick = Tick; s.Moved = Vec.Zero; }
            }
            UpdateMorale();
            foreach (var a in Armies)
                if (!a.General.Alive && a.Order != CrowdOrders.Hold) { a.Order = CrowdOrders.Hold; a.Charging = false; } // 장수를 잃으면 제자리

            // 4) 끝: 총대장을 사로잡거나 (체력 0) 병사가 다 쓰러지면, 시간이 다 되면 남은 병사 많은 쪽
            // 모두 무너지면 RoutEndTicks 동안 도망치는 모습을 보인 뒤 끝 (바로 끝내면 무너진 줄 모름)
            for (int t = 0; t < 2; t++) { if (!AllRouted(t)) _allRoutedSince[t] = -1; else if (_allRoutedSince[t] < 0) _allRoutedSince[t] = Tick; }
            bool allyOut = !Commander(Ally).General.Alive || AliveCount(Ally) == 0 || (_allRoutedSince[Ally] >= 0 && Tick - _allRoutedSince[Ally] >= r.RoutEndTicks);
            bool enemyOut = !Commander(Enemy).General.Alive || AliveCount(Enemy) == 0 || (_allRoutedSince[Enemy] >= 0 && Tick - _allRoutedSince[Enemy] >= r.RoutEndTicks);
            if (allyOut || enemyOut)
            {
                Winner = allyOut && enemyOut ? -2 : allyOut ? Enemy : Ally;
                int lt = allyOut ? Ally : Enemy;
                EndReason = !Commander(lt).General.Alive ? "general" : AliveCount(lt) == 0 ? "wiped" : "routed";
            }
            else if (Tick >= r.MaxTicks)
            {
                int a = AliveCount(Ally), e = AliveCount(Enemy);
                Winner = a == e ? -2 : a > e ? Ally : Enemy;
                EndReason = "time";
            }
        }

        /// <summary>그 편 부대가 모두 무너졌거나 싸울 수 없음.</summary>
        public bool AllRouted(int team)
        {
            foreach (var a in Armies) if (a.Team == team && !a.Routed && Standing(a)) return false;
            return true;
        }

        /// <summary>
        /// 사기 (R-128): 포위면 초당 encircleDrain 깎임, 맞지 않은 지 calmSeconds면 초당 moraleRegen 오름.
        /// waverAt 이하 = 흔들림, routAt 이하 = 무너짐 (되돌아오지 않음, 같은 편 부대 사기 allyRoutedMorale 깎임).
        /// </summary>
        private void UpdateMorale()
        {
            var r = Rules;
            Fix dt = r.TickSeconds;
            foreach (var a in Armies)
            {
                if (a.Routed || !Standing(a)) continue;
                int sides = 0;
                for (int k = 1; k < 4; k++) if (a.SideHitTick[k] >= Tick - r.EncircleTicks) sides++;
                a.Encircled = sides >= 2;
                // 이번 틱 옆 · 뒤에서 맞고 있으면 초당 flankDrain · rearDrain (여러 번 맞아도 한 번)
                if (a.SideHitTick[3] == Tick) a.Morale -= r.RearDrain * dt;
                else if (a.SideHitTick[1] == Tick || a.SideHitTick[2] == Tick) a.Morale -= r.FlankDrain * dt;
                if (a.Encircled) a.Morale -= r.EncircleDrain * dt;
                else if (a.LastHitTick < Tick - r.CalmTicks) a.Morale = Fix.Min(r.MoraleStart, a.Morale + r.MoraleRegen * dt);
                if (a.Morale < Fix.Zero) a.Morale = Fix.Zero;
                a.Wavering = a.Morale <= r.WaverAt;
            }
            foreach (var a in Armies)
            {
                if (a.Routed || !Standing(a) || a.Morale > r.RoutAt) continue;
                a.Routed = true;
                a.RoutedTick = Tick;
                a.Order = CrowdOrders.Hold;
                a.Charging = false;
                foreach (var o in Armies) if (o.Team == a.Team && o != a && !o.Routed) o.Morale -= r.AllyRoutedMorale;
            }
        }

        private Vec ToSlot(CrowdArmy army, CrowdSoldier s, Fix stepLen)
        {
            var d = army.SlotWorld(s.Slot) - s.Pos;
            Fix len = d.Length;
            return len <= stepLen ? d : d * (stepLen / len);
        }

        /// <summary>
        /// 근접 한 방. 빠르게 달려와(지난 틱 걸음의 1.1배 넘게) 처음 친 한 방 = 부딪침: 피해 더 (기병은 mountedImpact배), 상대를 밀침.
        /// 단, 기병이 창병에게 달려들면 받아치기: 부딪침 없이 기병이 피해를 입고 밀려난다.
        /// </summary>
        private void Strike(CrowdSoldier s, CrowdSoldier e, Vec d, Fix d2, CrowdArmy army, Fix walkStep)
        {
            var r = Rules;
            var troop = army.Troop;
            var foeArmy = Armies[e.Army];
            var foeTroop = foeArmy.Troop;
            int side = SideOf(foeArmy, foeArmy.TickCentroid, s.Pos);
            int dmg = HitDamage(troop, foeTroop, SidePp(side));
            _damage[e.Id] += dmg;
            NoteHit(foeArmy, side);
            Fix fast = walkStep * Fix.FromRatio(11, 10);
            bool rushed = s.Moved.LengthSquared > fast * fast && s.LastAttackTick < Tick - r.AttackTicks * 3;
            if (rushed)
            {
                Fix len = Fix.Sqrt(d2);
                if (troop.Mounted && foeTroop.Polearm && !e.IsGeneral)
                {
                    // 받아치기: 창끝에 걸린 기병이 피해를 입고 튕겨 나간다
                    _damage[s.Id] += HitDamage(foeTroop, troop) * r.BraceDamage;
                    if (len > Fix.Zero) _knock[s.Id] = _knock[s.Id] - d * (r.ImpactPush / len);
                    s.ImpactTick = Tick;
                    e.ImpactTick = Tick;
                    ImpactsThisTick++;
                }
                else
                {
                    int mult = troop.Mounted ? r.MountedImpact : 1;
                    _damage[e.Id] += dmg * r.ImpactDamage * mult;
                    if (len > Fix.Zero) _knock[e.Id] = _knock[e.Id] + d * (r.ImpactPush * mult / len);
                    s.ImpactTick = Tick;
                    e.ImpactTick = Tick;
                    ImpactsThisTick++;
                }
            }
            s.LastAttackTick = Tick;
            s.LastTargetId = e.Id;
            s.Cooldown = r.AttackTicks + (r.AttackJitterTicks > 0 ? _rng.NextInt(r.AttackJitterTicks + 1) : 0);
            if (army.Wavering) s.Cooldown = (int)(Fix.FromInt(s.Cooldown) * r.WaverAttack).RoundToLong(); // 흔들리면 덜 친다
        }

        /// <summary>화살 한 대: 쏜 틱 + 거리 ÷ 화살 빠르기 뒤에 닿는다.</summary>
        private void Shoot(CrowdSoldier s, int target, CrowdTroop troop)
        {
            var r = Rules;
            var e = Soldiers[target];
            Fix dist = (e.Pos - s.Pos).Length;
            int flight = System.Math.Max(1, (int)(dist / (r.ArrowSpeed * r.TickSeconds)).RoundToLong());
            Arrows.Add(new CrowdArrow { ShooterId = s.Id, TargetId = target, From = s.Pos, To = e.Pos, FireTick = Tick, ArriveTick = Tick + flight, Damage = System.Math.Max(1, (int)(Fix.FromInt(HitDamage(troop, Armies[e.Army].Troop)) * r.ArrowDamageScale).RoundToLong()) });
            s.LastAttackTick = Tick;
            s.LastTargetId = target;
            s.Cooldown = r.ShootTicks + (r.AttackJitterTicks > 0 ? _rng.NextInt(r.AttackJitterTicks * 2 + 1) : 0);
        }

        /// <summary>적 부대: 공격 대상이 없거나 무너졌으면 가장 가까운 우리 부대를 공격.</summary>
        private void EnemyThink()
        {
            foreach (var a in Armies)
            {
                if (a.Team != Enemy || !a.Leading) continue;
                if (a.Order == CrowdOrders.Attack && a.OrderTarget >= 0 && Standing(Armies[a.OrderTarget])) continue;
                int best = -1;
                Fix bestD = Fix.Zero;
                var me = a.Centroid();
                foreach (var o in Armies)
                {
                    if (o.Team == a.Team || !Standing(o)) continue;
                    Fix d = Vec.DistanceSquared(o.Centroid(), me);
                    if (best < 0 || d < bestD) { best = o.Id; bestD = d; }
                }
                if (best >= 0) OrderAttack(a.Id, best);
            }
        }

        /// <summary>부대 가운데를 명령대로 옮긴다. 공격이면 가까워질 때 돌격 (궁병은 사거리에서 멈춤), 맞붙으면 돌격 끝 · 느려짐.</summary>
        private void MoveArmy(CrowdArmy a, int engagedSoldiers)
        {
            var r = Rules;
            Fix dt = r.TickSeconds;
            if (a.Order == CrowdOrders.Hold || a.Routed) { a.Charging = false; return; }
            Vec goal;
            if (a.Order == CrowdOrders.Move) goal = a.OrderPoint;
            else
            {
                var t = Armies[a.OrderTarget];
                if (!Standing(t)) { a.Order = CrowdOrders.Hold; a.Charging = false; return; }
                goal = t.Centroid();
                Fix gap = (goal - a.Center).Length - a.HalfDepth - t.HalfDepth;
                if (a.Troop.Ranged)
                {
                    // 궁병: 사거리의 80% 안이면 멈춰서 쏜다 (방향만 맞춤)
                    a.Charging = false;
                    if (gap <= RangeOf(a.Troop) * Fix.FromRatio(4, 5))
                    {
                        var dd = goal - a.Center;
                        if (dd.LengthSquared > Fix.OneValue) a.Facing = dd.Normalized();
                        return;
                    }
                }
                else
                {
                    int alive0 = System.Math.Max(1, a.AliveCount);
                    bool contact = engagedSoldiers * 10 >= alive0; // 10% 넘게 맞붙음
                    if (contact) a.Charging = false;
                    else if (gap <= r.ChargeRange) a.Charging = true;
                }
            }
            var d = goal - a.Center;
            Fix dist = d.Length;
            Fix baseSpeed = (a.Charging ? r.ChargeAdvanceSpeed : r.AdvanceSpeed) * a.SpeedFactor;
            if (a.Order == CrowdOrders.Move && dist <= baseSpeed * dt) { a.Center = goal; a.Order = CrowdOrders.Hold; a.Charging = false; return; }
            if (a.Order == CrowdOrders.Attack && dist <= Fix.OneValue) return;
            if (dist > Fix.OneValue) a.Facing = d.Normalized(); // 도착 직전에는 방향을 그대로 (뒤집히지 않게)
            int alive = System.Math.Max(1, a.AliveCount);
            Fix slow = Fix.OneValue - Fix.Min(Fix.OneValue, Fix.FromRatio(engagedSoldiers * 2, alive));
            a.Center = a.Center + a.Facing * Fix.Min(dist, baseSpeed * dt * slow);
        }

        private const int RelaxPasses = 2;

        /// <summary>다음 위치(_next) 기준으로 겹친 두 병사를 반씩 떼어 놓는다 (id 순서, 이웃은 지금 칸 격자로).</summary>
        private void Relax()
        {
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
                            var o = Soldiers[j];
                            Fix diameter = s.Radius + o.Radius;
                            var d = _next[i] - _next[j];
                            Fix d2 = d.LengthSquared;
                            if (d2 >= diameter * diameter || d2 == Fix.Zero) continue;
                            Fix len = Fix.Sqrt(d2);
                            var corr = d * ((diameter - len) / 2 / len);
                            bool gi = s.IsGeneral, gj = o.IsGeneral;
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
        private int NearestEnemy(CrowdSoldier s, Fix range2, Fix range)
        {
            int best = -1;
            Fix bestD = range2;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            int cells = (int)range.FloorToLong() + 1;
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
        private Vec Separation(CrowdSoldier s)
        {
            Fix px = Fix.Zero, py = Fix.Zero;
            long cx = s.Pos.X.FloorToLong(), cy = s.Pos.Y.FloorToLong();
            for (long x = cx - 1; x <= cx + 1; x++)
                for (long y = cy - 1; y <= cy + 1; y++)
                {
                    if (!_grid.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j == s.Id) continue;
                        var o = Soldiers[j];
                        Fix diameter = s.Radius + o.Radius;
                        var d = s.Pos - o.Pos;
                        Fix d2 = d.LengthSquared;
                        if (d2 >= diameter * diameter) continue;
                        if (d2 == Fix.Zero)
                        {
                            Fix e = Fix.FromRatio(1, 100);
                            px += s.Id < j ? -e : e;
                            continue;
                        }
                        Fix len = Fix.Sqrt(d2);
                        Fix push = (diameter - len) / 2;
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

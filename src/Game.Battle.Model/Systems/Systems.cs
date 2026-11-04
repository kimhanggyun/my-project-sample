using System.Collections.Generic;
using Game.Battle.Model.Combat;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Systems
{
    // 틱 시스템. 한 시스템 = 한 클래스 (structure.md 3.2). 순서는 BattleSimulation.Step이 정한다.

    internal static class Tags
    {
        public const string Melee = "melee";
        public const string Ranged = "ranged";
    }

    /// <summary>군단 이동: 명령 → 목적지 → 길 찾기 → 이동. 소부대는 진형 칸 위치를 따른다.</summary>
    internal static class MovementSystem
    {
        private static readonly Fix Fifty = Fix.FromInt(50);

        public static void Step(BattleContext ctx)
        {
            foreach (var corps in ctx.Corps)
            {
                corps.MovedThisTick = false;
                if (!corps.InBattle) continue;
                Vec? dest = Destination(ctx, corps, out bool chasing);
                if (dest == null || ShouldStop(corps)) continue;

                Cell goal = Cell.Of(dest.Value);
                Cell here = Cell.Of(corps.Anchor);
                if (here.Equals(goal) && corps.Order.Kind == OrderKinds.MoveTo && !chasing)
                {
                    corps.AssignOrder(new CorpsOrder()); // 도착
                    continue;
                }
                var members = corps.AliveTroopTypes();
                bool stale = corps.PathGoal == null || !corps.PathGoal.Value.Equals(goal)
                             || corps.PathIndex >= corps.Path.Count
                             || (chasing && ctx.Tick - corps.PathPlannedTick >= ctx.Combat.AiThinkEveryTicks);
                if (stale)
                {
                    corps.Path = ctx.Field.FindPath(here, goal, members);
                    corps.PathIndex = 0;
                    corps.PathGoal = goal;
                    corps.PathPlannedTick = ctx.Tick;
                }
                var before = corps.Anchor;
                Move(ctx, corps, members);
                corps.MovedThisTick = !before.Equals(corps.Anchor);
            }
            foreach (var corps in ctx.Corps) PlaceUnits(ctx, corps);
        }

        /// <summary>강행·퇴각은 멈추지 않는다. 근접 소부대가 있으면 접전에 들면, 원거리만 남은 군단은 사거리에 들면 멈춘다.</summary>
        private static bool ShouldStop(CorpsState corps)
        {
            if (corps.Status == CorpsStatus.Retreating || corps.Order.Mode == MoveModes.Forced) return false;
            return corps.HasAliveMelee(Tags.Melee) ? corps.MeleeContact : corps.Engaged;
        }

        private static Vec? Destination(BattleContext ctx, CorpsState corps, out bool chasing)
        {
            chasing = false;
            var order = corps.Order;
            switch (order.Kind)
            {
                case OrderKinds.Retreat:
                    return RetreatSystem.EdgePoint(ctx, corps);
                case OrderKinds.Attack:
                {
                    var target = ctx.FindCorps(order.TargetCorpsId);
                    if (target == null || !target.InBattle)
                    {
                        corps.AssignOrder(new CorpsOrder());
                        return null;
                    }
                    chasing = true;
                    return target.Anchor;
                }
                case OrderKinds.Advance:
                {
                    var target = ctx.NearestEnemy(corps);
                    if (target == null) return ctx.AdvanceFallback(corps); // 안 보이면 마지막 목격 위치·적 시작 위치로
                    chasing = true;
                    return target.Anchor;
                }
                case OrderKinds.MoveTo:
                    if (order.Mode == MoveModes.AttackMove)
                    {
                        var near = ctx.NearestEnemy(corps);
                        var reach = ctx.Combat.PerceptionCells;
                        if (near != null && Vec.DistanceSquared(near.Anchor, corps.Anchor) <= reach * reach)
                        {
                            chasing = true;
                            return near.Anchor;
                        }
                    }
                    return order.Point;
                default:
                    return null;
            }
        }

        private static void Move(BattleContext ctx, CorpsState corps, List<TroopTypeDef> members)
        {
            if (members.Count == 0) return;
            Fix minSpeed = Fix.Zero;
            bool first = true;
            foreach (var m in members)
            {
                Fix s = m.Stats.Get(StatIds.Speed);
                if (first || s < minSpeed) { minSpeed = s; first = false; }
            }
            Fix cost = ctx.Field.CostAt(Cell.Of(corps.Anchor), members);
            Fix dist = ctx.Combat.MoveCellsPerSecondAtSpeed50 * minSpeed / Fifty * ctx.Combat.TickSeconds / cost;
            if (corps.PathIndex < corps.Path.Count)
            {
                // 바람(진행 방향)·날씨·시간대·병종 태그 이동 보정
                Vec dir = (corps.Path[corps.PathIndex].Center - corps.Anchor).Normalized();
                Fix pp = Fix.Max(Fix.FromInt(-90), ctx.Environment.MovePp(dir, members));
                dist = dist * (Fix.Hundred + pp) / Fix.Hundred;
            }
            while (dist > Fix.Zero && corps.PathIndex < corps.Path.Count)
            {
                Vec wp = corps.Path[corps.PathIndex].Center;
                Vec d = wp - corps.Anchor;
                Fix len = d.Length;
                if (len > Fix.Zero) corps.Facing = d.Normalized();
                if (len <= dist)
                {
                    corps.Anchor = wp;
                    dist -= len;
                    corps.PathIndex++;
                }
                else
                {
                    corps.Anchor += d.Normalized() * dist;
                    dist = Fix.Zero;
                }
            }
        }

        /// <summary>소부대 위치 = 장수 위치 + 진형 칸 (보는 방향 기준). 그 칸에 못 들어가는 병종이면 장수 위치.</summary>
        public static void PlaceUnits(BattleContext ctx, CorpsState corps)
        {
            Vec fwd = corps.Facing, right = fwd.RightOf();
            Fix maxX = Fix.FromInt(ctx.Field.Width) - Fix.FromRaw(1);
            Fix maxY = Fix.FromInt(ctx.Field.Height) - Fix.FromRaw(1);
            foreach (var u in corps.Units)
            {
                var off = corps.Formation.Slots[u.Slot];
                Vec p = corps.Anchor + right * off.X + fwd * off.Y;
                p = new Vec(Fix.Clamp(p.X, Fix.Zero, maxX), Fix.Clamp(p.Y, Fix.Zero, maxY));
                if (!ctx.Field.CanEnter(Cell.Of(p), u.Troop)) p = corps.Anchor;
                u.Position = p;
            }
        }
    }

    /// <summary>
    /// 전투. 피해는 동시에 계산한 뒤 한꺼번에 적용한다 (처리 순서가 결과를 바꾸지 않게).
    /// 1) 사거리 안의 (보이는) 적은 반드시 공격한다 (R-20): 소부대마다 사거리 안의 가장 가까운 적.
    ///    예외: 은신 중인 복병, 혼란 중인 소부대.
    /// 2) 접전: 근접 소부대 하나라도 적 군단에 닿으면 그 군단과 "접전" 상태가 되고, 사거리 밖의 근접 소부대도 접전 상대의
    ///    가장 가까운 소부대를 공격한다. 동시에 싸우는 수는 소부대 위치가 아니라 진형 특성치(정면 폭, 추후)가 정한다.
    /// 피해 = 병력 × 초당 기본 피해 × 틱 길이 × (100 + 보정 합) / 100 (R-1, R-2).
    /// </summary>
    internal sealed class CombatSystem
    {
        private readonly IReadOnlyList<IDamageModifierSource> _sources;
        private readonly List<DamageModifier> _mods = new List<DamageModifier>();
        private readonly AttackContext _attack = new AttackContext();
        private readonly List<CorpsState> _contacts = new List<CorpsState>();

        public CombatSystem(IReadOnlyList<IDamageModifierSource> sources) { _sources = sources; }

        public void Step(BattleContext ctx, DamageCalculator calculator)
        {
            var c = ctx.Combat;
            foreach (var corps in ctx.Corps)
            {
                corps.Engaged = false;
                corps.MeleeContact = false;
                corps.HitFromSide = false;
                corps.BeingHit = false;
                corps.LostThisTick = 0;
            }
            int variance = (int)c.DamageVariancePp.FloorToLong();
            _attack.Battle = ctx;

            foreach (var a in ctx.Corps)
            {
                if (!a.InBattle || a.Hidden) continue; // 은신 중인 복병은 들킬 때까지 공격하지 않는다 (R-20 예외)
                FindMeleeContacts(ctx, a);
                a.MeleeContact = _contacts.Count > 0;
                foreach (var u in a.Units)
                {
                    if (!u.Alive || u.Confused) continue;
                    Fix reach = u.Troop.AttackRange + c.ContactRadius;
                    if (!Nearest(ctx, a, u.Position, reach, null, u.Troop.HasTag(Tags.Ranged), out var dc, out var v))
                    {
                        if (!u.Troop.HasTag(Tags.Melee) || _contacts.Count == 0) continue;
                        Nearest(ctx, a, u.Position, null, _contacts, false, out dc, out v);
                    }

                    _attack.AttackerCorps = a;
                    _attack.Attacker = u;
                    _attack.DefenderCorps = dc;
                    _attack.Defender = v;
                    _attack.Side = Direction.Of(ctx, a, dc);
                    _mods.Clear();
                    foreach (var s in _sources) s.Collect(_attack, _mods);
                    if (variance > 0)
                        _mods.Add(new DamageModifier("variance", Fix.FromInt(ctx.Random.NextInt(-variance, variance + 1))));

                    Fix baseDamage = Fix.FromInt(u.Troops) * c.BaseDamagePerTroopPerSecond * c.TickSeconds;
                    var result = calculator.Calculate(baseDamage, _mods);
                    v.PendingLoss += result.Final;
                    a.Engaged = true;
                    dc.BeingHit = true;
                    if (_attack.Side != AttackSide.Front && ctx.IsEnabled(RuleFlagIds.FlankBonus)) dc.HitFromSide = true;
                }
            }

            foreach (var corps in ctx.Corps)
                foreach (var u in corps.Units)
                {
                    if (!u.Alive) { u.PendingLoss = Fix.Zero; continue; }
                    long lost = u.PendingLoss.FloorToLong();
                    if (lost <= 0) continue;
                    if (lost > u.Troops) lost = u.Troops;
                    u.Troops -= lost;
                    u.PendingLoss = u.Troops == 0 ? Fix.Zero : u.PendingLoss - Fix.FromInt(lost);
                    corps.LostThisTick += lost;
                }
        }

        /// <summary>a의 근접 소부대가 사거리 안에 둔 적 군단들 (군단 순서).</summary>
        private void FindMeleeContacts(BattleContext ctx, CorpsState a)
        {
            _contacts.Clear();
            var c = ctx.Combat;
            foreach (var e in ctx.Corps)
            {
                if (e.Team == a.Team || !e.InBattle || !ctx.IsVisibleTo(a.Team, e)) continue;
                bool touch = false;
                foreach (var u in a.Units)
                {
                    if (!u.Alive || !u.Troop.HasTag(Tags.Melee)) continue;
                    Fix reach = u.Troop.AttackRange + c.ContactRadius;
                    Fix reach2 = reach * reach;
                    foreach (var v in e.Units)
                        if (v.Alive && Vec.DistanceSquared(u.Position, v.Position) <= reach2) { touch = true; break; }
                    if (touch) break;
                }
                if (touch) _contacts.Add(e);
            }
        }

        /// <summary>
        /// 가장 가까운 적 소부대. reach가 있으면 그 거리 안 (원거리면 대상 방향의 바람·날씨 사거리 보정을 더함),
        /// only가 있으면 그 군단들 중에서.
        /// </summary>
        private static bool Nearest(BattleContext ctx, CorpsState self, Vec from, Fix? reach, List<CorpsState> only, bool ranged, out CorpsState corps, out SubUnitState unit)
        {
            corps = null;
            unit = null;
            Fix best = Fix.Zero;
            foreach (var e in ctx.Corps)
            {
                if (e.Team == self.Team || !e.InBattle || !ctx.IsVisibleTo(self.Team, e)) continue;
                if (only != null && !only.Contains(e)) continue;
                foreach (var v in e.Units)
                {
                    if (!v.Alive) continue;
                    Fix d = Vec.DistanceSquared(from, v.Position);
                    if (reach.HasValue)
                    {
                        Fix r = reach.Value;
                        if (ranged) r = Fix.Max(Fix.Zero, r + ctx.Environment.RangedRangeDelta(from, v.Position));
                        if (d > r * r) continue;
                    }
                    if (unit == null || d < best) { best = d; corps = e; unit = v; }
                }
            }
            return unit != null;
        }
    }

    /// <summary>
    /// 군량 소비 (R-6과 같은 식): 누적 Σ(병력 × 하루 소비량 × 하루 환산 × (100 + 날씨 %p)) × 틱 길이 ÷ 6000, 내림.
    /// 날씨 보정이 0이면 R-6 그대로.
    /// </summary>
    internal static class SupplySystem
    {
        public static void Step(BattleContext ctx)
        {
            if (!ctx.IsEnabled(RuleFlagIds.SupplyConsumption)) return;
            foreach (var corps in ctx.Corps)
            {
                if (!corps.InBattle) continue;
                long daily = 0;
                foreach (var u in corps.Units) daily = checked(daily + u.Troops * u.Troop.SupplyPerDay);
                long pct = 100 + ctx.Environment.EffectiveWeather.SupplyPp + ctx.Environment.EffectiveTime.SupplyPp;
                corps.SupplyAccumulator = checked(corps.SupplyAccumulator + daily * ctx.Balance.SupplyDaysPerMinute * pct);
                long consumed = (Fix.FromInt(corps.SupplyAccumulator) * ctx.Combat.TickSeconds / 6000).FloorToLong();
                corps.Supply = System.Math.Max(0, corps.StartSupply - consumed);
            }
        }
    }

    /// <summary>사기: 병력 손실·측면 피격·군량 바닥으로 떨어지고, 싸우지 않을 때 시작값까지 회복.</summary>
    internal static class MoraleSystem
    {
        public static void Step(BattleContext ctx)
        {
            if (!ctx.IsEnabled(RuleFlagIds.Morale)) return;
            var c = ctx.Combat;
            foreach (var corps in ctx.Corps)
            {
                if (!corps.InBattle) continue;
                Fix m = corps.Morale;
                long start = corps.StartTroops;
                if (corps.LostThisTick > 0 && start > 0)
                    m -= c.MoraleLossPerPercentTroops * Fix.FromRatio(corps.LostThisTick * 100, start);
                if (corps.HitFromSide) m -= c.MoraleFlankHitPerSecond * c.TickSeconds;
                if (!corps.Engaged && !corps.BeingHit && m < corps.StartMorale)
                    m = Fix.Min(corps.StartMorale, m + c.MoraleRecoverPerSecond * c.TickSeconds);
                if (corps.Supply == 0 && ctx.IsEnabled(RuleFlagIds.SupplyConsumption))
                    m -= c.MoraleSupplyExhaustedPerSecond * c.TickSeconds;
                m += ctx.Environment.Sum(x => x.MoralePerSecond) * c.TickSeconds; // 무더위·혹한
                corps.Morale = Fix.Clamp(m, Fix.Zero, Fix.Hundred);
            }
        }
    }

    /// <summary>궤멸·퇴각: 병력 0 → 궤멸. 사기 ≤ 성향 기준 또는 병력 ≤ 시작 × 비율 → 퇴각. 퇴각 방향 끝에 닿으면 이탈.</summary>
    internal static class RetreatSystem
    {
        public static void Step(BattleContext ctx)
        {
            var c = ctx.Combat;
            foreach (var corps in ctx.Corps)
            {
                if (!corps.InBattle) continue;
                if (corps.Troops == 0)
                {
                    corps.Status = CorpsStatus.Destroyed;
                    Events.EventLog.Add(ctx, Events.EventKinds.Destroyed, corps.Id);
                    corps.ClearPath();
                    continue;
                }
                if (corps.Status == CorpsStatus.Active)
                {
                    bool fewTroops = Fix.FromInt(corps.Troops) <= c.RetreatTroopRatio * corps.StartTroops;
                    bool broken = ctx.IsEnabled(RuleFlagIds.Morale) && corps.Morale <= corps.Temperament.RetreatMorale;
                    if (fewTroops || broken) BeginRetreat(ctx, corps);
                }
                if (corps.Status == CorpsStatus.Retreating && AtEdge(ctx, corps))
                {
                    corps.Status = CorpsStatus.Retreated;
                    corps.ClearPath();
                }
            }
        }

        /// <summary>퇴각 시작. 근처 아군 사기가 떨어진다 (battle.md 2.5.1 공통).</summary>
        public static void BeginRetreat(BattleContext ctx, CorpsState corps)
        {
            NearbyMoraleDrop(ctx, corps);
            Events.EventLog.Add(ctx, Events.EventKinds.RetreatStarted, corps.Id);
            corps.Status = CorpsStatus.Retreating;
            corps.AssignOrder(new CorpsOrder { Kind = OrderKinds.Retreat, Mode = MoveModes.Forced });
        }

        /// <summary>퇴각·포로가 생기면 반경 안 아군 군단 사기 감소.</summary>
        public static void NearbyMoraleDrop(BattleContext ctx, CorpsState leaving)
        {
            Fix r = ctx.Combat.NearbyMoraleRadius;
            foreach (var a in ctx.Corps)
            {
                if (a == leaving || a.Team != leaving.Team || !a.InBattle) continue;
                if (Vec.DistanceSquared(a.Anchor, leaving.Anchor) > r * r) continue;
                a.Morale = Fix.Clamp(a.Morale - ctx.Combat.NearbyMoraleDrop, Fix.Zero, Fix.Hundred);
            }
        }

        public static Vec EdgePoint(BattleContext ctx, CorpsState corps)
        {
            var here = Cell.Of(corps.Anchor);
            int w = ctx.Field.Width - 1, h = ctx.Field.Height - 1;
            switch (ctx.Field.Map.RetreatEdges[corps.Team])
            {
                case "north": return new Cell(here.X, 0).Center;
                case "south": return new Cell(here.X, h).Center;
                case "west": return new Cell(0, here.Y).Center;
                default: return new Cell(w, here.Y).Center;
            }
        }

        private static bool AtEdge(BattleContext ctx, CorpsState corps)
        {
            Fix m = ctx.Combat.RetreatEdgeMargin;
            Vec p = corps.Anchor;
            switch (ctx.Field.Map.RetreatEdges[corps.Team])
            {
                case "north": return p.Y <= m;
                case "south": return p.Y >= Fix.FromInt(ctx.Field.Height) - m;
                case "west": return p.X <= m;
                default: return p.X >= Fix.FromInt(ctx.Field.Width) - m;
            }
        }
    }
}

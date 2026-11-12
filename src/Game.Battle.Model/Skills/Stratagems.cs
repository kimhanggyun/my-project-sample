using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    /// <summary>땅에 숨겨 둔 함정 (R-80). 놓은 팀에게만 보인다. 상대 팀 간파에 걸리면 사라진다.</summary>
    public sealed class FieldTrap
    {
        public string Team { get; internal set; }
        public string OwnerCorpsId { get; internal set; }
        public Vec Point { get; internal set; }
        public Fix Radius { get; internal set; }
        /// <summary>이 틱부터 작동 (설치 준비).</summary>
        public long ArmedTick { get; internal set; }
        internal Fix LossPct { get; set; }
        internal long ConfuseTicks { get; set; }
    }

    /// <summary>위장 깃발 (R-80): 가짜 군단. 상대 팀에게는 깃발로 보이고 AI는 진짜로 여기고 다가간다.</summary>
    public sealed class Decoy
    {
        public string Id { get; internal set; }
        public string Team { get; internal set; }
        public Vec Point { get; internal set; }
        public long EndTick { get; internal set; }
    }

    /// <summary>함정 (trap): radius 안에 setupSec 뒤 작동. 상대 소부대가 밟으면 그 군단의 반경 안 소부대가 lossPct% 손실·confuseSec 혼란.</summary>
    internal sealed class TrapEffect : ISkillEffect
    {
        public string Type => "trap";
        public void Apply(SkillCast c, SkillPart p) => c.Battle.Traps.Add(new FieldTrap
        {
            Team = c.Caster.Team,
            OwnerCorpsId = c.Caster.Id,
            Point = c.Point,
            Radius = c.Skill.Radius,
            ArmedTick = c.Battle.Tick + SkillRules.Ticks(c.Battle, p.FixOr("setupSec", Fix.Zero)),
            LossPct = p.FixOr("lossPct", Fix.Zero),
            ConfuseTicks = SkillRules.Ticks(c.Battle, p.FixOr("confuseSec", Fix.Zero)),
        });
    }

    /// <summary>낙석 (rockfall): 반경 안 상대 소부대마다 병력 lossPct% 손실, confuseSec 혼란 (결속 저항). 전조는 스킬 telegraphSec.</summary>
    internal sealed class RockfallEffect : ISkillEffect
    {
        public string Type => "rockfall";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            long ticks = SkillRules.Ticks(ctx, p.FixOr("confuseSec", Fix.Zero));
            Fix pct = p.FixOr("lossPct", Fix.Zero);
            Fix r2 = c.Skill.Radius * c.Skill.Radius;
            foreach (var e in ctx.Corps)
            {
                if (e.Team == c.Caster.Team || !e.InBattle) continue;
                bool hit = false;
                foreach (var u in e.Units)
                {
                    if (!u.Alive || Vec.DistanceSquared(u.Position, c.Point) > r2) continue;
                    u.PendingLoss += Fix.FromInt(u.Troops) * pct / Fix.Hundred;
                    if (ticks > 0) Events.Confusion.Apply(ctx, u, ticks);
                    hit = true;
                }
                if (!hit) continue;
                e.BeingHit = true;
                if (e.Hidden) StealthSystem.Expose(e);
                Events.EventLog.Add(ctx, Events.EventKinds.RockfallHit, e.Id, c.Caster.Id);
            }
        }
    }

    /// <summary>위장 깃발 (decoy): 지점에 durationSec 동안 가짜 군단.</summary>
    internal sealed class DecoyEffect : ISkillEffect
    {
        public string Type => "decoy";
        public void Apply(SkillCast c, SkillPart p) => c.Battle.Decoys.Add(new Decoy
        {
            Id = "decoy." + c.Caster.Id + "." + c.Battle.Tick,
            Team = c.Caster.Team,
            Point = c.Point,
            EndTick = c.Battle.Tick + SkillRules.Ticks(c.Battle, p.Fix("durationSec")),
        });
    }

    /// <summary>
    /// 거짓 퇴각 (feignedRetreat): 적과 반대쪽으로 fallCells칸 강행 이동하며 durationSec 동안 퇴각하는 척.
    /// 끝나는 순간에도 나를 치고 있는(쫓아온) 적 군단은 소부대 disorderRatio(올림)가 disorderSec 혼란 (추격하다 흐트러짐),
    /// 나는 counterSec 동안 공격 +counterPp (돌아서서 반격).
    /// </summary>
    internal sealed class FeignedRetreatEffect : ISkillEffect
    {
        public string Type => "feignedRetreat";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            var me = c.Caster;
            var dir = Vec.Zero - ctx.Forward(me);
            var to = ctx.ClampToField(me.Anchor + dir * p.Fix("fallCells"));
            me.AssignOrder(new CorpsOrder { Kind = OrderKinds.MoveTo, Point = to, Mode = MoveModes.Forced });
            me.AddStatus(new StatusEffect { Id = StatusIds.FeignedRetreat, TicksLeft = SkillRules.Ticks(ctx, p.Fix("durationSec")) });
            me.Feign = new FeignPlan
            {
                DisorderRatio = p.Fix("disorderRatio"),
                DisorderTicks = SkillRules.Ticks(ctx, p.Fix("disorderSec")),
                CounterPp = p.Fix("counterPp"),
                CounterTicks = SkillRules.Ticks(ctx, p.Fix("counterSec")),
            };
        }
    }

    internal sealed class FeignPlan
    {
        public Fix DisorderRatio;
        public long DisorderTicks;
        public Fix CounterPp;
        public long CounterTicks;
    }

    /// <summary>조건 "casterAbove": 시전 군단이 선 칸의 높이가 대상 지점 칸보다 높다 (낙석, 위쪽을 차지).</summary>
    internal sealed class CasterAbove : ISkillCondition
    {
        public string Type => "casterAbove";
        public bool IsMet(SkillCast c, SkillPart p) =>
            c.Battle.Field.RuleAt(Cell.Of(c.Caster.Anchor)).Height > c.Battle.Field.RuleAt(Cell.Of(c.Point)).Height;
    }

    /// <summary>조건 "casterEngaged": 시전 군단이 접전 중 (거짓 퇴각).</summary>
    internal sealed class CasterEngaged : ISkillCondition
    {
        public string Type => "casterEngaged";
        public bool IsMet(SkillCast c, SkillPart p) => c.Caster.MeleeContact || c.Caster.BeingHit;
    }

    /// <summary>틱마다: 함정 작동·발견, 위장 깃발 만료·들킴, 거짓 퇴각 끝 (R-80).</summary>
    internal static class StratagemSystem
    {
        public static void Step(BattleContext ctx)
        {
            Traps(ctx);
            Decoys(ctx);
            Feigns(ctx);
        }

        private static bool RevealedBy(BattleContext ctx, string notTeam, Vec p)
        {
            foreach (var z in ctx.Reveals) if (z.Team != notTeam && z.Covers(p)) return true;
            return false;
        }

        private static void Traps(BattleContext ctx)
        {
            for (int i = ctx.Traps.Count - 1; i >= 0; i--)
            {
                var t = ctx.Traps[i];
                if (RevealedBy(ctx, t.Team, t.Point))
                {
                    ctx.Traps.RemoveAt(i);
                    Events.EventLog.Add(ctx, Events.EventKinds.TrapFound, t.OwnerCorpsId);
                    continue;
                }
                if (ctx.Tick < t.ArmedTick) continue;
                Fix r2 = t.Radius * t.Radius;
                CorpsState victim = null;
                foreach (var e in ctx.Corps)
                {
                    if (e.Team == t.Team || !e.InBattle) continue;
                    foreach (var u in e.Units) if (u.Alive && Vec.DistanceSquared(u.Position, t.Point) <= r2) { victim = e; break; }
                    if (victim != null) break;
                }
                if (victim == null) continue;
                ctx.Traps.RemoveAt(i);
                Fix wide = t.Radius * Fix.FromRatio(3, 2);
                foreach (var u in victim.Units)
                {
                    if (!u.Alive || Vec.DistanceSquared(u.Position, t.Point) > wide * wide) continue;
                    u.PendingLoss += Fix.FromInt(u.Troops) * t.LossPct / Fix.Hundred;
                    if (t.ConfuseTicks > 0) Events.Confusion.Apply(ctx, u, t.ConfuseTicks);
                }
                victim.BeingHit = true;
                Events.EventLog.Add(ctx, Events.EventKinds.TrapSprung, victim.Id, t.OwnerCorpsId);
            }
        }

        private static void Decoys(BattleContext ctx)
        {
            Fix near = Fix.FromRatio(3, 2);
            for (int i = ctx.Decoys.Count - 1; i >= 0; i--)
            {
                var d = ctx.Decoys[i];
                bool exposed = RevealedBy(ctx, d.Team, d.Point);
                foreach (var c in ctx.Corps)
                    if (c.Team != d.Team && c.InBattle && Vec.DistanceSquared(c.Anchor, d.Point) <= near * near) exposed = true;
                if (!exposed && ctx.Tick < d.EndTick) continue;
                ctx.Decoys.RemoveAt(i);
                if (exposed) Events.EventLog.Add(ctx, Events.EventKinds.DecoyExposed, null, d.Team);
            }
        }

        private static void Feigns(BattleContext ctx)
        {
            foreach (var me in ctx.Corps)
            {
                if (me.Feign == null) continue;
                var st = me.FindStatus(StatusIds.FeignedRetreat);
                if (!me.InBattle) { me.Feign = null; continue; }
                if (st != null && st.TicksLeft > 1) continue;
                var plan = me.Feign;
                me.Feign = null;
                me.RemoveStatus(StatusIds.FeignedRetreat);
                bool sprung = false;
                foreach (var e in ctx.Corps)
                {
                    if (e.Team == me.Team || !e.InBattle || e.Order.Kind != OrderKinds.Attack || e.Order.TargetCorpsId != me.Id) continue;
                    var alive = new List<SubUnitState>();
                    foreach (var u in e.Units) if (u.Alive) alive.Add(u);
                    int n = (int)System.Math.Min(alive.Count, (Fix.FromInt(alive.Count) * plan.DisorderRatio).CeilToLong());
                    for (int k = 0; k < n && alive.Count > 0; k++)
                    {
                        int pick = ctx.Random.NextInt(0, alive.Count);
                        Events.Confusion.Apply(ctx, alive[pick], plan.DisorderTicks);
                        alive.RemoveAt(pick);
                    }
                    sprung = true;
                    Events.EventLog.Add(ctx, Events.EventKinds.FeignSprung, e.Id, me.Id);
                }
                me.AddStatus(new StatusEffect { Id = "buff.feignedRetreat", TicksLeft = plan.CounterTicks, AttackPp = sprung ? plan.CounterPp : Fix.Zero });
                me.AssignOrder(new CorpsOrder()); // 돌아선다 (사거리 안 적과 싸움)
            }
        }
    }
}

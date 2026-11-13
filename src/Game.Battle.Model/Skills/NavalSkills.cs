using System.Collections.Generic;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    // 수상전 스킬 (skills.md 4차, R-85).

    /// <summary>
    /// 화선 (fireShip): 내 배의 sacrificePct%에 불을 붙여 보낸다 (그만큼 내 병력 손실).
    /// 전조 뒤 대상 지점 반경 안 적 소부대가 (보낸 병력 × damagePerTroop)를 나눠 받고 confuseSec 혼란, 그 군단 사기 + moralePp.
    /// </summary>
    internal sealed class FireShipEffect : ISkillEffect
    {
        public string Type => "fireShip";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            Fix pct = p.Fix("sacrificePct");
            long sent = 0;
            foreach (var u in c.Caster.Units)
            {
                if (!u.Alive) continue;
                long n = (Fix.FromInt(u.Troops) * pct / Fix.Hundred).FloorToLong();
                u.PendingLoss += Fix.FromInt(n);
                sent += n;
            }
            Fix r2 = c.Skill.Radius * c.Skill.Radius;
            var targets = new List<(CorpsState corps, SubUnitState unit)>();
            foreach (var e in ctx.Corps)
            {
                if (e.Team == c.Caster.Team || !e.InBattle) continue;
                foreach (var v in e.Units)
                    if (v.Alive && Vec.DistanceSquared(v.Position, c.Point) <= r2) targets.Add((e, v));
            }
            if (targets.Count == 0) return;
            Fix each = Fix.FromInt(sent) * p.Fix("damagePerTroop") / targets.Count;
            long ticks = SkillRules.Ticks(ctx, p.FixOr("confuseSec", Fix.Zero));
            var hitCorps = new HashSet<CorpsState>();
            foreach (var (e, v) in targets)
            {
                v.PendingLoss += each;
                if (ticks > 0) Events.Confusion.Apply(ctx, v, ticks);
                e.BeingHit = true;
                hitCorps.Add(e);
            }
            foreach (var e in hitCorps)
            {
                e.Morale = Fix.Clamp(e.Morale + p.FixOr("moralePp", Fix.Zero), Fix.Zero, Fix.Hundred);
                Events.EventLog.Add(ctx, Events.EventKinds.FireShipHit, e.Id, c.Caster.Id);
            }
        }
    }

    /// <summary>충각 돌격 (ramming): 대상 적을 공격하러 가고 durationSec 동안 공격 +attackPp, 대상 소부대 confuseRatio(올림)가 confuseSec 혼란.</summary>
    internal sealed class RammingEffect : ISkillEffect
    {
        public string Type => "ramming";
        public void Apply(SkillCast c, SkillPart p)
        {
            var ctx = c.Battle;
            var t = c.TargetCorps;
            if (t == null || !t.InBattle) return;
            c.Caster.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = t.Id });
            c.Caster.AddStatus(new StatusEffect { Id = "buff." + c.Skill.Id, TicksLeft = SkillRules.Ticks(ctx, p.Fix("durationSec")), AttackPp = p.Fix("attackPp") });
            var alive = new List<SubUnitState>();
            foreach (var u in t.Units) if (u.Alive) alive.Add(u);
            int n = (int)System.Math.Min(alive.Count, (Fix.FromInt(alive.Count) * p.Fix("confuseRatio")).CeilToLong());
            long ticks = SkillRules.Ticks(ctx, p.Fix("confuseSec"));
            for (int i = 0; i < n && alive.Count > 0; i++)
            {
                int k = ctx.Random.NextInt(0, alive.Count);
                Events.Confusion.Apply(ctx, alive[k], ticks);
                alive.RemoveAt(k);
            }
            Events.EventLog.Add(ctx, Events.EventKinds.Rammed, t.Id, c.Caster.Id);
        }
    }
}

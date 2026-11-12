using Game.Battle.Model.Geometry;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Skills
{
    // 3·4차 스킬 중 새 병종·맵이 필요 없는 것 (skills.md 6장, R-82).

    /// <summary>강행군 (forcedMarch): durationSec 동안 이동 +movePct%, 끝나면 사기 + endMorale (피로).</summary>
    internal sealed class ForcedMarchEffect : ISkillEffect
    {
        public string Type => "forcedMarch";
        public void Apply(SkillCast c, SkillPart p) => c.Caster.AddStatus(new StatusEffect
        {
            Id = "buff." + c.Skill.Id,
            TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")),
            MoveSpeedPct = p.Fix("movePct"),
            EndMorale = p.FixOr("endMorale", Fix.Zero),
        });
    }

    /// <summary>
    /// 은밀 행군 (stealthMarch): 바로 숨고 durationSec 동안 움직여도 들키지 않는다.
    /// 숨는 지형·밤·안개를 벗어나거나, 싸우거나(접전·피격), 간파에 걸리면 풀린다.
    /// </summary>
    internal sealed class StealthMarchEffect : ISkillEffect
    {
        public string Type => "stealthMarch";
        public void Apply(SkillCast c, SkillPart p)
        {
            c.Caster.AddStatus(new StatusEffect { Id = StatusIds.StealthMarch, TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")) });
            c.Caster.Hidden = true;
        }

        /// <summary>숨는 지형이거나 밤·안개.</summary>
        public static bool Cover(BattleContext ctx, CorpsState c) =>
            ctx.Field.RuleAt(Cell.Of(c.Anchor)).Conceals
            || ctx.Environment.EffectiveTime.Id == "night"
            || ctx.Environment.EffectiveWeather.Id == "fog";
    }

    /// <summary>조건 "stealthCover": 숨는 지형·밤·안개에서만 (은밀 행군).</summary>
    internal sealed class StealthCover : ISkillCondition
    {
        public string Type => "stealthCover";
        public bool IsMet(SkillCast c, SkillPart p) => StealthMarchEffect.Cover(c.Battle, c.Caster);
    }

    /// <summary>조건 "notInMelee": 칼이 맞닿아 있지 않음 (응급 치료 — 화살을 맞으면서는 된다).</summary>
    internal sealed class NotInMelee : ISkillCondition
    {
        public string Type => "notInMelee";
        public bool IsMet(SkillCast c, SkillPart p) => !c.Caster.MeleeContact;
    }

    /// <summary>선두 돌파 (spearhead): 대상 적 군단을 공격하러 가고, durationSec 동안 공격 +attackPp, 장수 포위 압력 +captureRiskPct%.</summary>
    internal sealed class SpearheadEffect : ISkillEffect
    {
        public string Type => "spearhead";
        public void Apply(SkillCast c, SkillPart p)
        {
            if (c.TargetCorps != null && c.TargetCorps.Team != c.Caster.Team)
                c.Caster.AssignOrder(new CorpsOrder { Kind = OrderKinds.Attack, TargetCorpsId = c.TargetCorps.Id });
            c.Caster.AddStatus(new StatusEffect
            {
                Id = "buff." + c.Skill.Id,
                TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")),
                AttackPp = p.Fix("attackPp"),
                CaptureRiskPct = p.FixOr("captureRiskPct", Fix.Zero),
            });
        }
    }

    /// <summary>
    /// 이간 (discord): 성공 확률 = baseChance + (내 지략 − 상대 지략) × perIntellect, [minChance, maxChance].
    /// 성공하면 대상 군단이 durationSec 동안 지휘 범위 밖(장수 능력치 영향 최소), 협공에서 빠지고, 그 군단의 눈은 같은 편에 보태지지 않는다.
    /// </summary>
    internal sealed class DiscordEffect : ISkillEffect
    {
        public string Type => "discord";
        public void Apply(SkillCast c, SkillPart p)
        {
            var t = c.TargetCorps;
            if (t == null || !t.InBattle || t.Setup.General == null || c.Caster.Setup.General == null) return;
            Fix chance = p.Fix("baseChance") + (c.Caster.Setup.General.Intellect - t.Setup.General.Intellect) * p.Fix("perIntellect");
            chance = Fix.Clamp(chance, p.Fix("minChance"), p.Fix("maxChance"));
            if (!c.Battle.Random.Chance(chance))
            {
                Events.EventLog.Add(c.Battle, Events.EventKinds.DiscordFailed, t.Id, c.Caster.Id);
                return;
            }
            t.AddStatus(new StatusEffect { Id = StatusIds.Discord, TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")), SourceCorpsId = c.Caster.Id });
            Events.EventLog.Add(c.Battle, Events.EventKinds.Discorded, t.Id, c.Caster.Id);
        }
    }

    /// <summary>유언비어 (rumor): 상대 팀 전장의 모든 군단 사기 + moralePp (반간을 건 군단은 막고 반간 효과를 낸다).</summary>
    internal sealed class RumorEffect : ISkillEffect
    {
        public string Type => "rumor";
        public void Apply(SkillCast c, SkillPart p)
        {
            Fix delta = p.Fix("moralePp");
            foreach (var e in c.Battle.Corps)
            {
                if (e.Team == c.Caster.Team || !e.InBattle) continue;
                if (CounterIntrigue.Blocks(c.Battle, c.Caster, e)) continue;
                e.Morale = Fix.Clamp(e.Morale + delta, Fix.Zero, Fix.Hundred);
            }
        }
    }

    /// <summary>반간 (counterIntrigue): durationSec 동안 나에게 오는 교란 계략 한 번을 막고, 건 장수 책략 점수 − drainPoints.</summary>
    internal sealed class CounterIntrigueEffect : ISkillEffect
    {
        public string Type => "counterIntrigue";
        public void Apply(SkillCast c, SkillPart p) => c.Caster.AddStatus(new StatusEffect
        {
            Id = StatusIds.CounterIntrigue,
            TicksLeft = SkillRules.Ticks(c.Battle, p.Fix("durationSec")),
            DrainPoints = p.Fix("drainPoints"),
        });
    }

    public static class CounterIntrigue
    {
        /// <summary>상대의 교란 계략이 target에 닿을 때: 반간이 걸려 있으면 막고(한 번) 건 쪽 점수를 깎는다. 막았으면 true.</summary>
        internal static bool Blocks(BattleContext ctx, CorpsState caster, CorpsState target)
        {
            if (target == null || target.Team == caster.Team) return false;
            var st = target.FindStatus(StatusIds.CounterIntrigue);
            if (st == null) return false;
            target.RemoveStatus(StatusIds.CounterIntrigue);
            caster.SkillPoints.Adjust(Fix.Zero - st.DrainPoints);
            Events.EventLog.Add(ctx, Events.EventKinds.CounterIntrigued, target.Id, caster.Id);
            return true;
        }
    }

    /// <summary>군량 소비 누적을 고쳐 군량을 더하거나 뺀다 (군량 = 가져온 양 − 누적 소비, SupplySystem).</summary>
    internal static class SupplyAdjust
    {
        public static long Add(BattleContext ctx, CorpsState c, long amount)
        {
            long before = c.Supply;
            long target = System.Math.Max(0, System.Math.Min(c.StartSupply, c.Supply + amount));
            // consumed = acc × tick / 6000 → acc = consumed × 6000 / tick
            long consumed = c.StartSupply - target;
            c.SupplyAccumulator = (Fix.FromInt(consumed) * Fix.FromInt(6000) / ctx.Combat.TickSeconds).CeilToLong();
            c.Supply = target;
            return target - before;
        }
    }

    /// <summary>군량 습격 (supplyRaid): 대상 적 군단의 지금 군량 pct%를 없애고, 그 stealPct%를 내 군량으로 (가져온 양까지).</summary>
    internal sealed class SupplyRaidEffect : ISkillEffect
    {
        public string Type => "supplyRaid";
        public void Apply(SkillCast c, SkillPart p)
        {
            var t = c.TargetCorps;
            if (t == null || !t.InBattle || t.Team == c.Caster.Team) return;
            long take = (Fix.FromInt(t.Supply) * p.Fix("pct") / Fix.Hundred).FloorToLong();
            long lost = -SupplyAdjust.Add(c.Battle, t, -take);
            SupplyAdjust.Add(c.Battle, c.Caster, (Fix.FromInt(lost) * p.FixOr("stealPct", Fix.Zero) / Fix.Hundred).FloorToLong());
            Events.EventLog.Add(c.Battle, Events.EventKinds.SupplyRaided, t.Id, lost.ToString());
        }
    }

    /// <summary>군량 보급 (supplyDrop): 대상 아군 군단 군량 + 가져온 양의 pct% (가져온 양까지).</summary>
    internal sealed class SupplyDropEffect : ISkillEffect
    {
        public string Type => "supplyDrop";
        public void Apply(SkillCast c, SkillPart p)
        {
            var t = c.TargetCorps ?? c.Caster;
            if (!t.InBattle) return;
            SupplyAdjust.Add(c.Battle, t, (Fix.FromInt(t.StartSupply) * p.Fix("pct") / Fix.Hundred).FloorToLong());
        }
    }

    /// <summary>응급 치료 (firstAid): 살아 있는 소부대마다 이번 전투에서 잃은 병력의 pct%를 되돌린다.</summary>
    internal sealed class FirstAidEffect : ISkillEffect
    {
        public string Type => "firstAid";
        public void Apply(SkillCast c, SkillPart p)
        {
            long healed = 0;
            foreach (var u in c.Caster.Units)
            {
                if (!u.Alive || u.Troops >= u.StartTroops) continue;
                long back = (Fix.FromInt(u.StartTroops - u.Troops) * p.Fix("pct") / Fix.Hundred).FloorToLong();
                u.Troops += back;
                healed += back;
            }
            Events.EventLog.Add(c.Battle, Events.EventKinds.FirstAid, c.Caster.Id, healed.ToString());
        }
    }
}

using System.Collections.Generic;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Combat
{
    /// <summary>
    /// 장수 특성의 전투 효과 (traits.md 3장, R-67). 군단 장수가 가진 특성의 효과 중 조건이 맞는 것을 더한다.
    /// 피해 보정은 가산 %p라 다른 보정과 함께 상한이 걸린다 (R-1). 스위치 traits로 끈다.
    /// </summary>
    public static class TraitEffects
    {
        public const string Flag = "traits";

        private static IEnumerable<TraitEffect> Of(BattleContext ctx, CorpsState corps, string type)
        {
            if (corps?.Setup.General?.Traits == null || !ctx.IsEnabled(Flag)) yield break;
            foreach (var id in corps.Setup.General.Traits)
                if (ctx.Defs.Traits.Traits.TryGetValue(id, out var t))
                    foreach (var e in t.Effects)
                        if (e.Type == type) yield return e;
        }

        private static bool When(BattleContext ctx, TraitEffect e, CorpsState corps, SubUnitState unit, CorpsState other)
        {
            if (e.Terrain != null && (unit == null || ctx.Field.TerrainAt(unit.Position) != e.Terrain)) return false;
            if (e.TimeOfDay != null && ctx.Environment.EffectiveTime.Id != e.TimeOfDay) return false;
            if (e.Weather != null && ctx.Environment.EffectiveWeather.Id != e.Weather) return false;
            if (e.TroopTag != null && (unit == null || !unit.Troop.HasTag(e.TroopTag))) return false;
            if (e.TargetRetreating && (other == null || other.Status != CorpsStatus.Retreating)) return false;
            if (e.Holding && corps.Order.Kind != OrderKinds.Hold) return false;
            return true;
        }

        /// <summary>공격 쪽 특성 공격 %p − 방어 쪽 특성 방어 %p.</summary>
        public static Fix DamagePp(AttackContext a)
        {
            var ctx = a.Battle;
            Fix pp = Fix.Zero;
            foreach (var e in Of(ctx, a.AttackerCorps, TraitEffect.AttackPp))
                if (When(ctx, e, a.AttackerCorps, a.Attacker, a.DefenderCorps)) pp += e.Value;
            foreach (var e in Of(ctx, a.DefenderCorps, TraitEffect.DefensePp))
                if (When(ctx, e, a.DefenderCorps, a.Defender, a.AttackerCorps)) pp -= e.Value;
            return pp;
        }

        /// <summary>손실 사기 하락 배율 (여러 개면 곱).</summary>
        public static Fix MoraleLossMultiplier(BattleContext ctx, CorpsState corps)
        {
            Fix m = Fix.OneValue;
            foreach (var e in Of(ctx, corps, TraitEffect.MoraleLossMul))
                if (When(ctx, e, corps, null, null)) m = m * e.Value;
            return m;
        }

        /// <summary>그 스킬 비용 (특성으로 줄거나 늚, 최소 0, 올림).</summary>
        public static Fix SkillCost(BattleContext ctx, CorpsState corps, string skillId, Fix baseCost)
        {
            Fix pct = Fix.Zero;
            foreach (var e in Of(ctx, corps, TraitEffect.SkillCostPct))
                if (e.Skills.Count == 0 || ((IList<string>)e.Skills).Contains(skillId)) pct += e.Value;
            if (pct == Fix.Zero) return baseCost;
            return Fix.Max(Fix.Zero, Fix.FromInt((baseCost * (Fix.Hundred + pct) / Fix.Hundred).CeilToLong()));
        }

        /// <summary>대결에서 주는 피해 % (장수 특성, 전투 밖 대결에도 쓴다).</summary>
        public static Fix ContestDamagePct(DefinitionSet defs, IEnumerable<string> traits)
        {
            Fix pct = Fix.Zero;
            if (traits == null) return pct;
            foreach (var id in traits)
                if (defs.Traits.Traits.TryGetValue(id, out var t))
                    foreach (var e in t.Effects)
                        if (e.Type == TraitEffect.ContestDamagePct) pct += e.Value;
            return pct;
        }
    }

    /// <summary>특성 피해 보정 출처 (R-67).</summary>
    internal sealed class TraitSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            var pp = TraitEffects.DamagePp(a);
            if (pp != Fix.Zero) into.Add(new DamageModifier("traits", pp, TraitEffects.Flag));
        }
    }
}

using System.Collections.Generic;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Combat
{
    /// <summary>
    /// 진형 특성치 (formations.md 2장, R-72). 스위치 formationStats.
    /// 공격: 정면이면 돌파력, 측면·배후면 포위력, 원거리 소부대면 원거리 효율. 방어: 맞는 방향의 방어(정면·측면·배후)를 뺀다.
    /// 이동 속도 %, 범위 피해(화살비·화계) 받는 %, 손실 사기 %도 여기서.
    /// </summary>
    public static class FormationStatEffects
    {
        public const string Flag = "formationStats";

        /// <summary>진형을 바꾸는 중이면 지금 진형(Formation) 기준 (R-56과 같은 규칙).</summary>
        private static FormationStats Of(BattleContext ctx, CorpsState c) =>
            ctx.IsEnabled(Flag) && c?.Formation?.Stats != null ? c.Formation.Stats : null;

        public static Fix MoveMultiplier(BattleContext ctx, CorpsState c)
        {
            var s = Of(ctx, c);
            return s == null ? Fix.OneValue : Fix.Max(Fix.FromInt(10), Fix.Hundred + s.MoveSpeedPct) / Fix.Hundred;
        }

        public static Fix AreaDamageMultiplier(BattleContext ctx, CorpsState c)
        {
            var s = Of(ctx, c);
            return s == null ? Fix.OneValue : Fix.Max(Fix.Zero, Fix.Hundred + s.AreaDamageTakenPct) / Fix.Hundred;
        }

        public static Fix MoraleLossMultiplier(BattleContext ctx, CorpsState c)
        {
            var s = Of(ctx, c);
            return s == null ? Fix.OneValue : Fix.Max(Fix.Zero, Fix.Hundred + s.MoraleLossPct) / Fix.Hundred;
        }

        internal static Fix DamagePp(AttackContext a)
        {
            Fix pp = Fix.Zero;
            var att = Of(a.Battle, a.AttackerCorps);
            var def = Of(a.Battle, a.DefenderCorps);
            if (att != null)
            {
                pp += a.Side == AttackSide.Front ? att.BreakthroughPp : att.EnvelopPp;
                if (a.Attacker.Troop.HasTag("ranged")) pp += att.RangedPp;
            }
            if (def != null)
                pp -= a.Side == AttackSide.Front ? def.FrontDefensePp : a.Side == AttackSide.Flank ? def.FlankDefensePp : def.RearDefensePp;
            return pp;
        }
    }

    /// <summary>피해 보정 출처: 진형 특성치 (R-72).</summary>
    internal sealed class FormationStatSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            var pp = FormationStatEffects.DamagePp(a);
            if (pp != Fix.Zero) into.Add(new DamageModifier("formationStats", pp, FormationStatEffects.Flag));
        }
    }
}

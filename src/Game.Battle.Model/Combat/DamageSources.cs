using System.Collections.Generic;
using Game.Battle.Model.Damage;
using Game.Battle.Model.Geometry;
using Game.Battle.Model.Morale;
using Game.Battle.Model.Simulation;
using Game.Data.Defs;
using Game.Foundation.Math;

namespace Game.Battle.Model.Combat
{
    /// <summary>공격 한 번의 상황. 보정 출처들이 이것만 보고 %p를 낸다.</summary>
    public sealed class AttackContext
    {
        public BattleContext Battle { get; internal set; }
        public CorpsState AttackerCorps { get; internal set; }
        public SubUnitState Attacker { get; internal set; }
        public CorpsState DefenderCorps { get; internal set; }
        public SubUnitState Defender { get; internal set; }
        public AttackSide Side { get; internal set; }
    }

    /// <summary>피해 보정 출처 하나 (R-1: 모든 보정은 %p로 더한 뒤 한 번만 적용). 새 보정(스킬·날씨 …)은 구현을 추가하고 등록한다.</summary>
    public interface IDamageModifierSource
    {
        void Collect(AttackContext a, List<DamageModifier> into);
    }

    public static class RuleFlagIds
    {
        public const string Morale = "morale";
        public const string TroopCounters = "troopCounters";
        public const string FormationCounters = "formationCounters";
        public const string FlankBonus = "flankBonus";
        public const string TerrainRules = "terrainRules";
        public const string TerrainCombat = "terrainCombat";
        public const string HeightBonus = "heightBonus";
        public const string SupplyConsumption = "supplyConsumption";
    }

    public static class StatIds
    {
        public const string Attack = "attack";
        public const string Defense = "defense";
        public const string Speed = "speed";
    }

    /// <summary>기본 보정 출처 목록 (등록 순서 = 계산 순서, 결과는 합이라 순서와 무관).</summary>
    public static class DamageSources
    {
        public static List<IDamageModifierSource> BuiltIns() => new List<IDamageModifierSource>
        {
            new StatSource(),
            new TroopCounterSource(),
            new FormationCounterSource(),
            new DirectionSource(),
            new TerrainCombatSource(),
            new HeightSource(),
            new MoraleSource(),
            new StatusAttackSource(),
            new ConditionSource(),
        };
    }

    /// <summary>능력치: (공격 − 방어) × 계수. 장수 무력은 공격, 통솔은 방어에 더한다.</summary>
    internal sealed class StatSource : IDamageModifierSource
    {
        private static readonly Fix Fifty = Fix.FromInt(50);
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            var c = a.Battle.Combat;
            Fix atk = a.Attacker.Troop.Stats.Get(StatIds.Attack) + (a.AttackerCorps.Setup.General.Might - Fifty) * c.GeneralMightToAttack;
            Fix def = a.Defender.Troop.Stats.Get(StatIds.Defense) + (a.DefenderCorps.Setup.General.Leadership - Fifty) * c.GeneralLeadershipToDefense;
            into.Add(new DamageModifier("stats", (atk - def) * c.StatPpPerPoint));
        }
    }

    /// <summary>병종 상성 (troopCounters.json, 태그 기준).</summary>
    internal sealed class TroopCounterSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            foreach (var r in a.Battle.Defs.TroopCounters)
                if (a.Attacker.Troop.HasTag(r.AttackerTag) && a.Defender.Troop.HasTag(r.DefenderTag))
                    into.Add(new DamageModifier("troopCounter", r.Pp, RuleFlagIds.TroopCounters));
        }
    }

    /// <summary>진형 상성 (formations.json wins). 핵심 진형끼리는 corePp, 그 밖은 situationalPp.</summary>
    internal sealed class FormationCounterSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            FormationDef att = a.AttackerCorps.Formation, def = a.DefenderCorps.Formation;
            var wins = a.Battle.Defs.FormationWins;
            int sign = 0;
            if (wins.TryGetValue(att.Id, out var w) && w.Contains(def.Id)) sign = 1;
            else if (wins.TryGetValue(def.Id, out var l) && l.Contains(att.Id)) sign = -1;
            if (sign == 0) return;
            Fix pp = att.Group == "core" && def.Group == "core" ? a.Battle.Combat.FormationCorePp : a.Battle.Combat.FormationSituationalPp;
            into.Add(new DamageModifier("formationCounter", sign > 0 ? pp : -pp, RuleFlagIds.FormationCounters));
        }
    }

    /// <summary>측면·배후 공격.</summary>
    internal sealed class DirectionSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            if (a.Side == AttackSide.Flank) into.Add(new DamageModifier("flank", a.Battle.Combat.FlankPp, RuleFlagIds.FlankBonus));
            else if (a.Side == AttackSide.Rear) into.Add(new DamageModifier("rear", a.Battle.Combat.RearPp, RuleFlagIds.FlankBonus));
        }
    }

    /// <summary>공격하는 소부대가 선 지형의 병종 보정 (R-17).</summary>
    internal sealed class TerrainCombatSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            string terrain = a.Battle.Field.TerrainAt(a.Attacker.Position);
            into.Add(new DamageModifier("terrain", TerrainCombat.ModifierPp(a.Battle.Defs, terrain, a.Attacker.Troop), RuleFlagIds.TerrainCombat));
        }
    }

    /// <summary>높은 곳에서 공격.</summary>
    internal sealed class HeightSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            int diff = a.Battle.Field.HeightAt(a.Attacker.Position) - a.Battle.Field.HeightAt(a.Defender.Position);
            if (diff != 0) into.Add(new DamageModifier("height", a.Battle.Combat.HeightPpPerLevel * diff, RuleFlagIds.HeightBonus));
        }
    }

    /// <summary>사기 (R-3): 공격 쪽 공격 보정 − 방어 쪽 방어 보정.</summary>
    internal sealed class MoraleSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            var b = a.Battle.Balance;
            var ag = a.AttackerCorps.Setup.General;
            var dg = a.DefenderCorps.Setup.General;
            var (atk, _) = MoraleModifier.Calculate(b, a.AttackerCorps.Morale, ag.Might, ag.Intellect);
            var (_, def) = MoraleModifier.Calculate(b, a.DefenderCorps.Morale, dg.Might, dg.Intellect);
            into.Add(new DamageModifier("morale", atk, RuleFlagIds.Morale));
            into.Add(new DamageModifier("moraleDefense", -def, RuleFlagIds.Morale));
        }
    }

    /// <summary>공격하는 군단에 걸린 상태(돌격 함성, 기습 …)의 공격 보정.</summary>
    internal sealed class StatusAttackSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            foreach (var st in a.AttackerCorps.Statuses)
                if (st.AttackPp != Fix.Zero) into.Add(new DamageModifier("status." + st.Id, st.AttackPp));
        }
    }

    /// <summary>날씨·시간대: 병종 태그 공격·방어 보정, 원거리 공격 보정 (스위치 weather, timeOfDay).</summary>
    internal sealed class ConditionSource : IDamageModifierSource
    {
        public void Collect(AttackContext a, List<DamageModifier> into)
        {
            var env = a.Battle.Environment;
            Fix pp = env.TagSum(c => c.TagAttackPp, a.Attacker.Troop) - env.TagSum(c => c.TagDefensePp, a.Defender.Troop);
            if (a.Attacker.Troop.HasTag("ranged") && Vec.DistanceSquared(a.Attacker.Position, a.Defender.Position) > a.Battle.Combat.ContactRadius * a.Battle.Combat.ContactRadius)
                pp += env.Sum(c => c.RangedAttackPp);
            if (pp != Fix.Zero) into.Add(new DamageModifier("conditions", pp));
        }
    }

    internal static class Direction
    {
        /// <summary>공격 군단이 방어 군단의 어느 쪽에 있는지 (방어 군단이 보는 방향 기준).</summary>
        public static AttackSide Of(BattleContext ctx, CorpsState attacker, CorpsState defender)
        {
            Vec toAttacker = (attacker.Anchor - defender.Anchor).Normalized();
            if (toAttacker.Equals(Vec.Zero)) return AttackSide.Front;
            Fix dot = toAttacker.Dot(defender.Facing);
            if (dot >= ctx.Combat.FrontDot) return AttackSide.Front;
            if (dot <= ctx.Combat.RearDot) return AttackSide.Rear;
            return AttackSide.Flank;
        }
    }
}

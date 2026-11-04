using System.Collections.Generic;
using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Battle.Model.Damage
{
    /// <summary>보정 하나. 출처(예: "troopCounter", "flank", "terrain")와 %p 값, 이 보정을 켜고 끄는 규칙 스위치.</summary>
    public readonly struct DamageModifier
    {
        public readonly string Source;
        public readonly Fix Pp;
        public readonly string FlagId;

        public DamageModifier(string source, Fix pp, string flagId = null)
        {
            Source = source;
            Pp = pp;
            FlagId = flagId;
        }
    }

    public sealed class DamageResult
    {
        public Fix Final { get; internal set; }
        public Fix TotalPp { get; internal set; }
        public Fix ClampedPp { get; internal set; }
        /// <summary>실제로 더해진 보정들 (꺼진 스위치 제외). 합 = TotalPp.</summary>
        public IReadOnlyList<DamageModifier> Applied { get; internal set; }
    }

    /// <summary>
    /// 피해 계산 (verification R-1, R-2): 보정을 %p로 더함 → 상·하한으로 자름 → 기본 피해에 한 번만 적용.
    /// </summary>
    public sealed class DamageCalculator
    {
        private readonly BalanceData _balance;
        private readonly RuleFlags _flags;

        public DamageCalculator(BalanceData balance, RuleFlags flags)
        {
            _balance = balance;
            _flags = flags;
        }

        public DamageResult Calculate(Fix baseDamage, IEnumerable<DamageModifier> modifiers)
        {
            var applied = new List<DamageModifier>();
            Fix total = Fix.Zero;
            foreach (var m in modifiers)
            {
                if (m.FlagId != null && !_flags.IsEnabled(m.FlagId)) continue;
                applied.Add(m);
                total += m.Pp;
            }
            Fix clamped = Fix.Clamp(total, _balance.DamageModifierMinPp, _balance.DamageModifierMaxPp);
            Fix final = baseDamage * (Fix.Hundred + clamped) / Fix.Hundred;
            return new DamageResult { Final = final, TotalPp = total, ClampedPp = clamped, Applied = applied };
        }
    }
}

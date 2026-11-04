using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Battle.Model.Morale
{
    /// <summary>
    /// 사기 보정 (verification R-3).
    /// 사기 ≥ 기준: 공격 = (사기−기준)/(100−기준) × 계수 × 무력, 방어 = 같은 식 × 지략.
    /// 사기 &lt; 기준: 공격·방어 모두 (기준−사기)/기준 × 최저 감산 만큼 감산.
    /// </summary>
    public static class MoraleModifier
    {
        public static (Fix attackPp, Fix defensePp) Calculate(BalanceData b, Fix morale, Fix might, Fix intellect)
        {
            Fix baseline = b.MoraleBase;
            if (morale >= baseline)
            {
                Fix ratio = (morale - baseline) / (Fix.Hundred - baseline);
                return (ratio * b.MoraleBonusCoefficient * might, ratio * b.MoraleBonusCoefficient * intellect);
            }
            Fix penaltyRatio = (baseline - morale) / baseline;
            Fix penalty = -(penaltyRatio * b.MoraleMinPenaltyPp);
            return (penalty, penalty);
        }
    }
}

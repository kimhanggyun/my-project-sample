using Game.Data.Rules;
using Game.Foundation.Math;

namespace Game.Campaign.Troops
{
    /// <summary>
    /// 장수 통솔 → 군단 병력 한도 (verification R-4). 최저 + (통솔−최저통솔)/(최고통솔−최저통솔) × (최고−최저), 내림.
    /// </summary>
    public static class TroopCapCalculator
    {
        public static long Cap(BalanceData b, Fix leadership)
        {
            Fix lead = Fix.Clamp(leadership, b.TroopCapMinLeadership, b.TroopCapMaxLeadership);
            Fix span = b.TroopCapMaxLeadership - b.TroopCapMinLeadership;
            Fix extra = (lead - b.TroopCapMinLeadership) * (b.TroopCapMax - b.TroopCapMin) / span;
            return b.TroopCapMin + extra.FloorToLong();
        }
    }
}

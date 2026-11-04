using Game.Data.Rules;

namespace Game.Economy
{
    /// <summary>
    /// 부상병 즉시 치료 비용 (verification R-10) = 같은 수를 징병(군량) 또는 모병(돈)하는 비용 × 치료 비율. 올림.
    /// </summary>
    public static class HealCostCalculator
    {
        public static long ConscriptCost(BalanceData b, long soldiers) => checked(soldiers * b.ConscriptSupplyPerSoldier);
        public static long RecruitCost(BalanceData b, long soldiers) => checked(soldiers * b.RecruitGoldPerSoldier);

        public static long HealCostInSupply(BalanceData b, long wounded) =>
            (b.HealCostRatio * ConscriptCost(b, wounded)).CeilToLong();

        public static long HealCostInGold(BalanceData b, long wounded) =>
            (b.HealCostRatio * RecruitCost(b, wounded)).CeilToLong();
    }
}

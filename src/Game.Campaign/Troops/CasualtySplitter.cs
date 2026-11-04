using Game.Data.Rules;

namespace Game.Campaign.Troops
{
    /// <summary>전투 손실 → 부상·사망 (verification R-9). 부상 = 손실 × 부상 비율(내림), 사망 = 나머지.</summary>
    public static class CasualtySplitter
    {
        public static (long wounded, long dead) Split(BalanceData b, long losses, bool woundedRecoveryEnabled)
        {
            if (losses <= 0) return (0, 0);
            if (!woundedRecoveryEnabled) return (0, losses);
            long wounded = (b.WoundedRatio * losses).FloorToLong();
            return (wounded, losses - wounded);
        }
    }
}

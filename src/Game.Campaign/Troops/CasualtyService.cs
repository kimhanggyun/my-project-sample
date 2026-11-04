using Game.Campaign.Roster;
using Game.Data.Rules;

namespace Game.Campaign.Troops
{
    /// <summary>전투 손실을 보유 부대에 반영 (R-9): 병력에서 빼고, 부상병은 따로 모은다 (치료하면 돌아옴).</summary>
    public static class CasualtyService
    {
        public const string WoundedRecoveryFlag = "woundedRecovery";

        public static (long wounded, long dead) Apply(BalanceData b, RuleFlags flags, UnitState unit, long losses)
        {
            if (losses <= 0) return (0, 0);
            if (losses > unit.Troops) losses = unit.Troops;
            var (wounded, dead) = CasualtySplitter.Split(b, losses, flags.IsEnabled(WoundedRecoveryFlag));
            unit.Troops -= losses;
            unit.Wounded = checked(unit.Wounded + wounded);
            return (wounded, dead);
        }

        /// <summary>부상병 치료 → 병력으로 복귀 (비용은 Economy의 HealCostCalculator로 따로 낸다).</summary>
        public static long Heal(UnitState unit, long count)
        {
            if (count > unit.Wounded) count = unit.Wounded;
            if (count <= 0) return 0;
            unit.Wounded -= count;
            unit.Troops = checked(unit.Troops + count);
            return count;
        }
    }
}
